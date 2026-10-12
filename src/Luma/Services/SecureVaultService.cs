using System.Security.Cryptography;
using System.Text;

namespace Luma.Services;

public sealed class SecureVaultService
{
    private readonly string _vaultDirectory;

    public SecureVaultService(string dataRoot)
    {
        _vaultDirectory = Path.Combine(dataRoot, "Vault");
        Directory.CreateDirectory(_vaultDirectory);
    }

    public async Task SaveSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var plain = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(plain);
        await File.WriteAllBytesAsync(PathFor(key), protectedBytes, cancellationToken);
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var plain = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public Task DeleteSecretAsync(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_vaultDirectory, Convert.ToHexString(hash) + ".bin");
    }
}
