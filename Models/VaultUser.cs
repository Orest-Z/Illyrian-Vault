namespace IllyriaVault.Models;

public sealed class VaultUser
{
    public long     Id               { get; set; }
    public string   DisplayName      { get; set; } = "Local Profile";
    public byte[]   PasswordSalt     { get; set; } = [];
    public byte[]   VerificationHash { get; set; } = [];
    public string   RecoveryKeyHash  { get; set; } = string.Empty;
    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
}
