using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Luma.Models;

namespace Luma.Services;

public sealed class SecureVaultService
{
    private readonly string _vaultDirectory;
    private readonly CredentialManagerStore _credentials;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public SecureVaultService(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _vaultDirectory = Path.Combine(Path.GetFullPath(dataRoot), "Vault", "Typed-v1");
        Directory.CreateDirectory(_vaultDirectory);
        _credentials = new CredentialManagerStore(Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(_vaultDirectory.ToUpperInvariant()))));
    }
    public Task SaveLoginAsync(LoginVaultEntry entry, string password, CancellationToken cancellationToken = default)
    {
        ValidateEntry(entry);
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length is < 1 or > 1280 || password.Contains('\0'))
            throw new ArgumentException("Login password length is invalid.", nameof(password));
        return SaveAsync(entry, password, cancellationToken);
    }
    public Task SaveAddressAsync(AddressVaultEntry entry, CancellationToken cancellationToken = default)
    { ValidateEntry(entry); return SaveAsync(entry, null, cancellationToken); }
    public Task SavePaymentAsync(PaymentVaultEntry entry, CancellationToken cancellationToken = default)
    { ValidateEntry(entry); return SaveAsync(entry, null, cancellationToken); }

    public async Task<IReadOnlyList<VaultEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = new List<VaultEntry>();
            foreach (var path in Directory.EnumerateFiles(_vaultDirectory, "*.vault"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id))
                    entries.Add((await ReadAsync(id, cancellationToken))!.Entry);
            }
            return entries.OrderBy(e => e.Label, StringComparer.Ordinal).ToArray();
        }
        finally { _gate.Release(); }
    }
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = await ReadAsync(id, cancellationToken);
            if (stored?.Entry is LoginVaultEntry { SecretStore: LoginSecretStore.WindowsCredentialManager })
                _credentials.Delete(id, stored.SecretVersion);
            File.Delete(PathFor(id));
        }
        finally { _gate.Release(); }
    }
    // Secret resolution is restricted to the autofill service, never a list/display model.
    internal async Task<T> UseEntryAsync<T>(Guid id, Func<VaultEntry, string?, Task<T>> use, CancellationToken cancellationToken)
    {
        ValidateId(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = await ReadAsync(id, cancellationToken) ?? throw new InvalidOperationException("Vault entry does not exist.");
            var password = stored.Entry is LoginVaultEntry { SecretStore: LoginSecretStore.WindowsCredentialManager }
                ? _credentials.Read(id, stored.SecretVersion) : stored.Password;
            if (stored.Entry is LoginVaultEntry && password is null)
                throw new InvalidOperationException("Login secret is unavailable.");
            return await use(stored.Entry, password);
        }
        finally { _gate.Release(); }
    }
    private async Task SaveAsync(VaultEntry entry, string? password, CancellationToken cancellationToken)
    {
        entry = entry with { Origin = NormalizeOrigin(entry.Origin) };
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await ReadAsync(entry.Id, cancellationToken);
            if (existing is not null && (existing.Entry.GetType() != entry.GetType() || existing.Entry.Origin != entry.Origin))
                throw new InvalidOperationException("Use a new entry ID when changing type or origin.");
            if (existing?.Entry is LoginVaultEntry previous && entry is LoginVaultEntry next && previous.SecretStore != next.SecretStore)
                throw new InvalidOperationException("Delete and recreate to change the login secret store.");
            bool credman = entry is LoginVaultEntry { SecretStore: LoginSecretStore.WindowsCredentialManager };
            var stored = new StoredRecord { Entry = entry, Password = credman ? null : password,
                SecretVersion = credman ? Guid.NewGuid() : Guid.Empty };
            var plain = JsonSerializer.SerializeToUtf8Bytes(stored, Json);
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            var temporary = PathFor(entry.Id) + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (credman) _credentials.Write(entry.Id, stored.SecretVersion, password!);
                try { File.Move(temporary, PathFor(entry.Id), overwrite: true); }
                catch
                {
                    // A failed publication cannot alter the previously referenced login secret.
                    if (credman) _credentials.Delete(entry.Id, stored.SecretVersion);
                    throw;
                }
                if (credman && existing is not null) _credentials.Delete(entry.Id, existing.SecretVersion);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { _gate.Release(); }
    }
    private async Task<StoredRecord?> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Vault record is invalid.");
        var cipher = await File.ReadAllBytesAsync(path, cancellationToken);
        var plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
        try
        {
            StoredRecord stored;
            try { stored = JsonSerializer.Deserialize<StoredRecord>(plain, Json) ?? throw new JsonException(); }
            catch (JsonException) { throw new InvalidDataException("Vault record is invalid."); }
            ValidateEntry(stored.Entry);
            if (stored.Version != 1 || stored.Entry.Id != id ||
                (stored.Entry is not LoginVaultEntry && stored.Password is not null) ||
                (stored.Entry is LoginVaultEntry { SecretStore: LoginSecretStore.WindowsCredentialManager } &&
                    (stored.Password is not null || stored.SecretVersion == Guid.Empty)) ||
                (stored.Entry is not LoginVaultEntry { SecretStore: LoginSecretStore.WindowsCredentialManager } && stored.SecretVersion != Guid.Empty) ||
                (stored.Entry is LoginVaultEntry { SecretStore: LoginSecretStore.Dpapi } &&
                (stored.Password is null || stored.Password.Length is < 1 or > 1280 || stored.Password.Contains('\0'))))
                throw new InvalidDataException("Vault record is invalid.");
            return stored;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public static string NormalizeOrigin(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (url.Any(char.IsControl) || url.Contains('\\') || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 ||
            uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
            throw new ArgumentException("A valid HTTPS origin without user information is required.");
        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.IPv6) host = "[" + host.Trim('[', ']') + "]";
        return "https://" + host + (uri.IsDefaultPort ? "" : ":" + uri.Port);
    }
    public static bool IsExactOrigin(string expected, string actual)
    {
        try { return NormalizeOrigin(expected) == NormalizeOrigin(actual); }
        catch (ArgumentException) { return false; }
    }
    public static void ValidateEntry(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateId(entry.Id);
        _ = NormalizeOrigin(entry.Origin);
        Text(entry.Label, 120, required: true);
        switch (entry)
        {
            case LoginVaultEntry login:
                Text(login.Username, 320, required: true);
                if (!Enum.IsDefined(login.SecretStore)) throw new ArgumentException("Unknown login secret store.");
                break;
            case AddressVaultEntry address:
                foreach (var value in new[] { address.FullName, address.AddressLine1, address.AddressLine2,
                    address.City, address.Region, address.PostalCode, address.Email, address.Phone }) Text(value, 320);
                Text(address.CountryCode, 2);
                if (address.CountryCode.Length != 0 && !Regex.IsMatch(address.CountryCode, "^[A-Z]{2}$", RegexOptions.CultureInvariant))
                    throw new ArgumentException("Country code must be two uppercase letters.");
                break;
            case PaymentVaultEntry payment:
                Text(payment.Holder, 120, required: true);
                ValidatePaymentText(payment.Label);
                ValidatePaymentText(payment.Holder);
                if (payment.Brand is not ("Visa" or "Mastercard" or "Amex" or "Discover" or "JCB" or "UnionPay" or "Other"))
                    throw new ArgumentException("Unsupported payment brand.");
                if (payment.Last4 is null || payment.Last4.Length != 4 || !Regex.IsMatch(payment.Last4, "^[0-9]{4}$", RegexOptions.CultureInvariant) ||
                    payment.ExpiryMonth is < 1 or > 12 || payment.ExpiryYear is < 2000 or > 9999)
                    throw new ArgumentException("Payment metadata is invalid.");
                break;
            default: throw new ArgumentException("Unknown vault entry type.");
        }
    }
    private static void ValidatePaymentText(string value)
    {
        // Normalize compatibility forms so full-width digits/labels cannot bypass this check.
        var normalized = value.Normalize(NormalizationForm.FormKC);
        if (Regex.IsMatch(normalized, @"(?:[0-9][\s-]*){12,}", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(normalized,
                @"(?<![\p{L}\p{N}])(?:cvv2?|cvc2?|csc|cid|security[\s_-]*code|card[\s_-]*verification(?:[\s_-]*(?:value|code))?)(?![\p{L}\p{N}])[\s:=#-]*[0-9]+",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Card numbers and security codes must not be stored in payment metadata.");
    }
    private static void Text(string value, int limit, bool required = false)
    {
        if (value is null || value.Length > limit || value.Any(char.IsControl) || (required && string.IsNullOrWhiteSpace(value)))
            throw new ArgumentException("Vault metadata field is invalid.");
    }
    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A vault entry ID is required.");
    }
    private string PathFor(Guid id) => Path.Combine(_vaultDirectory, id.ToString("N") + ".vault");
    private sealed class StoredRecord
    {
        public int Version { get; init; } = 1;
        public required VaultEntry Entry { get; init; }
        public string? Password { get; init; }
        public Guid SecretVersion { get; init; }
    }
}
