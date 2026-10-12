using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Luma.Models;
using Luma.Services;

namespace Luma.Tests;

// Framework-independent: invoke RunAsync in a Windows test host with the four vault source files.
public static class VaultTests
{
    public static async Task RunAsync()
    {
        Check(SecureVaultService.NormalizeOrigin("https://EXAMPLE.com:443/path?q=x") == "https://example.com");
        Check(SecureVaultService.NormalizeOrigin("https://bücher.example") == "https://xn--bcher-kva.example");
        Check(SecureVaultService.NormalizeOrigin("https://[::1]:8443/a") == "https://[::1]:8443");
        Check(SecureVaultService.IsExactOrigin("https://example.com", "https://example.com:443/login"));
        foreach (var bad in new[] { "http://example.com", "https://sub.example.com", "https://example.com:444", "https://example.com.evil.test",
            "https://example.com@evil.test", "file:///example.com", "javascript:alert(1)", "https://example.com\\@evil.test", "https://example.com\n" })
            Check(!SecureVaultService.IsExactOrigin("https://example.com", bad));
        var login = new LoginVaultEntry { Label = "Example", Origin = "https://example.com", Username = "user" };
        var address = new AddressVaultEntry { Label = "Home", Origin = login.Origin, FullName = "Test Person", CountryCode = "KR" };
        var payment = new PaymentVaultEntry { Label = "Card", Origin = login.Origin, Brand = "Visa", Last4 = "1234", Holder = "Test Person", ExpiryMonth = 12, ExpiryYear = 2030 };
        SecureVaultService.ValidateEntry(login); SecureVaultService.ValidateEntry(address); SecureVaultService.ValidateEntry(payment);
        Reject(() => SecureVaultService.ValidateEntry(payment with { Last4 = "4111111111111111" }));
        Reject(() => SecureVaultService.ValidateEntry(payment with { Last4 = "1234\n" }));
        Reject(() => SecureVaultService.ValidateEntry(payment with { Holder = "4111 1111 1111 1111" }));
        foreach (var forbidden in new[] { "4111111111111111", "Card 4111-1111-1111-1111", "４１１１ １１１１ １１１１ １１１１",
            "CVV:123", "cvc = 1234", "CVV2#123", "Security code: 123", "Card verification code 123", "ＣＶＶ：１２３" })
        {
            Reject(() => SecureVaultService.ValidateEntry(payment with { Label = forbidden }));
            Reject(() => SecureVaultService.ValidateEntry(payment with { Holder = forbidden }));
        }
        SecureVaultService.ValidateEntry(payment with { Label = "Visa •••• 1234" });
        SecureVaultService.ValidateEntry(payment with { Label = "Visa ending 1234 - expiry 12/2030", Holder = "Test Person 1234" });
        Reject(() => SecureVaultService.ValidateEntry(payment with { Brand = "CVV:123" }));
        Reject(() => SecureVaultService.ValidateEntry(payment with { ExpiryMonth = 0 }));
        Reject(() => SecureVaultService.ValidateEntry(login with { SecretStore = (LoginSecretStore)99 }));
        Reject(() => SecureVaultService.ValidateEntry(login with { Id = Guid.Empty }));
        Reject(() => SecureVaultService.ValidateEntry(address with { CountryCode = "korea" }));
        foreach (var entry in new VaultEntry[] { login, address, payment })
            foreach (var token in new[] { "cc-number", "cc-csc", "cvv", "cvc", "one-time-code", "new-password", "password", "section-a username", "billing cc-name", "username ", "USERNAME" })
                Check(!VaultAutofillService.IsAllowedField(entry, token));
        Check(VaultAutofillService.IsAllowedField(login, "current-password"));
        Check(!VaultAutofillService.IsAllowedField(address, "current-password"));
        Check(VaultAutofillService.IsAllowedField(payment, "cc-name"));
        Check(!VaultAutofillService.IsAllowedField(payment, "cc-type"));
        Check(!typeof(LoginVaultEntry).GetProperties().Any(p => p.Name.Contains("Password") || p.Name.Contains("Secret") && p.Name != "SecretStore"));
        var hostile = "\";window.pwned=true;//</script>\u2028";
        var script = BuildScript(login, hostile, login.Origin + "/", "key", "token");
        Check(!script.Contains(hostile));
        Check(script.Contains("\\u003C/script\\u003E"));
        var paymentScript = BuildScript(payment, null, login.Origin + "/", "key", "token");
        Check(!paymentScript.Contains("1234") && !paymentScript.Contains("Visa"));
        Check(!script.Contains("dispatchEvent(") && !script.Contains(".submit(") && !script.Contains(".click("));
        if (Environment.GetEnvironmentVariable("LUMA_VAULT_TEST_SCRIPT_DIRECTORY") is { Length: > 0 } scripts)
        {
            await File.WriteAllTextAsync(Path.Combine(scripts, "login-fill.js"), script);
            await File.WriteAllTextAsync(Path.Combine(scripts, "payment-fill.js"), paymentScript);
        }

        var root = Path.Combine(Environment.GetEnvironmentVariable("LUMA_VAULT_TEST_ROOT") ?? Path.GetTempPath(),
            "LumaVaultTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vault = new SecureVaultService(root);
            await vault.SaveLoginAsync(login, hostile);
            await vault.SaveAddressAsync(address);
            await vault.SavePaymentAsync(payment);
            var listed = await new SecureVaultService(root).ListAsync();
            Check(listed.Count == 3 && listed.OfType<LoginVaultEntry>().Single().Username == "user");
            Check(!JsonSerializer.Serialize(listed).Contains(hostile));
            Check(await UseEntryAsync(vault, login.Id, (e, secret) => Task.FromResult(secret == hostile), default));
            var path = Path.Combine(root, "Vault", "Typed-v1", login.Id.ToString("N") + ".vault");
            var cipher = await File.ReadAllBytesAsync(path);
            Check(!Encoding.UTF8.GetString(cipher).Contains("Example"));
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                await RejectAsync<OperationCanceledException>(() => vault.SaveLoginAsync(login, "replacement", cancelled.Token));
            }
            Check(await UseEntryAsync(vault, login.Id, (e, secret) => Task.FromResult(secret == hostile), default));
            await RejectAsync<InvalidOperationException>(() => vault.SaveLoginAsync(login with { Origin = "https://evil.test" }, "replacement"));
            // A valid DPAPI blob substituted under another ID must fail closed.
            var addressPath = Path.Combine(root, "Vault", "Typed-v1", address.Id.ToString("N") + ".vault");
            var originalAddress = await File.ReadAllBytesAsync(addressPath);
            await File.WriteAllBytesAsync(addressPath, cipher);
            await RejectAsync<InvalidDataException>(() => vault.ListAsync());
            await File.WriteAllBytesAsync(addressPath, originalAddress);
            // Unknown payment fields are refused, even inside an otherwise valid encrypted envelope.
            var paymentPath = Path.Combine(root, "Vault", "Typed-v1", payment.Id.ToString("N") + ".vault");
            var originalPayment = await File.ReadAllBytesAsync(paymentPath);
            var plain = ProtectedData.Unprotect(originalPayment, null, DataProtectionScope.CurrentUser);
            try
            {
                var json = Encoding.UTF8.GetString(plain).Replace("\"Last4\":\"1234\"", "\"Last4\":\"1234\",\"PAN\":\"forbidden\"");
                await File.WriteAllBytesAsync(paymentPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser));
                await RejectAsync<InvalidDataException>(() => vault.ListAsync());
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
            await File.WriteAllBytesAsync(paymentPath, originalPayment);
            cipher[^1] ^= 0x80;
            await File.WriteAllBytesAsync(path, cipher);
            await RejectAsync<CryptographicException>(() => vault.ListAsync());
            await File.WriteAllBytesAsync(path, ProtectedData.Protect(Encoding.UTF8.GetBytes("{}"), null, DataProtectionScope.CurrentUser));
            await RejectAsync<InvalidDataException>(() => vault.ListAsync());
            File.Delete(path);
            await vault.DeleteAsync(address.Id); await vault.DeleteAsync(payment.Id);
            Check((await vault.ListAsync()).Count == 0);

            var credLogin = login with { Id = Guid.NewGuid(), SecretStore = LoginSecretStore.WindowsCredentialManager };
            try
            {
                await vault.SaveLoginAsync(credLogin, "test-only-secret");
                Check(await UseEntryAsync(vault, credLogin.Id, (e, secret) => Task.FromResult(secret == "test-only-secret"), default));
                var credPath = Path.Combine(root, "Vault", "Typed-v1", credLogin.Id.ToString("N") + ".vault");
                var originalCredentialRecord = await File.ReadAllBytesAsync(credPath);
                // Force publication failure after the new version's native credential write.
                File.SetAttributes(credPath, FileAttributes.ReadOnly);
                try
                {
                    bool failed = false;
                    try { await vault.SaveLoginAsync(credLogin with { Username = "changed-user" }, "changed-secret"); }
                    catch (UnauthorizedAccessException) { failed = true; }
                    catch (IOException) { failed = true; }
                    Check(failed);
                }
                finally { File.SetAttributes(credPath, FileAttributes.Normal); }
                Check((await File.ReadAllBytesAsync(credPath)).SequenceEqual(originalCredentialRecord));
                Check(await UseEntryAsync(vault, credLogin.Id, (e, secret) => Task.FromResult(
                    ((LoginVaultEntry)e).Username == "user" && secret == "test-only-secret"), default));
                await vault.SaveLoginAsync(credLogin with { Username = "updated-user" }, "updated-secret");
                Check(await UseEntryAsync(vault, credLogin.Id, (e, secret) => Task.FromResult(
                    ((LoginVaultEntry)e).Username == "updated-user" && secret == "updated-secret"), default));
                var envelope = ProtectedData.Unprotect(await File.ReadAllBytesAsync(credPath), null, DataProtectionScope.CurrentUser);
                try { Check(!Encoding.UTF8.GetString(envelope).Contains("updated-secret")); }
                finally { CryptographicOperations.ZeroMemory(envelope); }
                await RejectAsync<InvalidOperationException>(() => vault.SaveLoginAsync(credLogin with { SecretStore = LoginSecretStore.Dpapi }, "replacement"));
                await vault.DeleteAsync(credLogin.Id);
                Check((await vault.ListAsync()).Count == 0);
            }
            finally { await vault.DeleteAsync(credLogin.Id); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
    // Reflection keeps test hosts using an app reference from requiring production secret APIs.
    private static string BuildScript(VaultEntry entry, string? secret, string href, string key, string token) =>
        (string)typeof(VaultAutofillService).GetMethod("BuildFillScript",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { entry, secret, href, key, token })!;
    private static Task<T> UseEntryAsync<T>(SecureVaultService vault, Guid id,
        Func<VaultEntry, string?, Task<T>> use, CancellationToken cancellationToken) =>
        (Task<T>)typeof(SecureVaultService).GetMethod("UseEntryAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(T)).Invoke(vault, new object[] { id, use, cancellationToken })!;
    private static void Check(bool condition) { if (!condition) throw new Exception("Vault test assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Expected validation refusal.");
    }
    private static async Task RejectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception("Expected vault operation refusal.");
    }
}
