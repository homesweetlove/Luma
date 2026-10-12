using System.Text.Json.Serialization;
namespace Luma.Models;
public enum LoginSecretStore { Dpapi, WindowsCredentialManager }
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LoginVaultEntry), "login")]
[JsonDerivedType(typeof(AddressVaultEntry), "address")]
[JsonDerivedType(typeof(PaymentVaultEntry), "payment")]
public abstract record VaultEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Label { get; init; } = "";
    public string Origin { get; init; } = "";
}
// Display metadata only. Passwords never enter these models.
public sealed record LoginVaultEntry : VaultEntry
{
    public string Username { get; init; } = "";
    public LoginSecretStore SecretStore { get; init; }
}
public sealed record AddressVaultEntry : VaultEntry
{
    public string FullName { get; init; } = "";
    public string AddressLine1 { get; init; } = "";
    public string AddressLine2 { get; init; } = "";
    public string City { get; init; } = "";
    public string Region { get; init; } = "";
    public string PostalCode { get; init; } = "";
    public string CountryCode { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
}
// No PAN, security code, PIN, or arbitrary field bag.
public sealed record PaymentVaultEntry : VaultEntry
{
    public string Brand { get; init; } = "";
    public string Last4 { get; init; } = "";
    public string Holder { get; init; } = "";
    public int ExpiryMonth { get; init; }
    public int ExpiryYear { get; init; }
}
public sealed record VaultFillResult(bool Success, int FilledFields, string? RefusalReason = null);
