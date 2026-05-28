namespace IllyrianVault.Models;

public sealed class PasswordEntry
{
    public long     Id                { get; set; }
    public long     UserId            { get; set; }
    public string   Title             { get; set; } = string.Empty;
    public string   Username          { get; set; } = string.Empty;
    public string   EncryptedPassword { get; set; } = string.Empty;
    public string   Url               { get; set; } = string.Empty;
    public string   Notes             { get; set; } = string.Empty;
    public string   Category          { get; set; } = "Login"; // Login | Note | Card | Identity
    public string   EncryptedPayload  { get; set; } = string.Empty;
    public bool     IsFavorite        { get; set; }
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt         { get; set; } = DateTime.UtcNow;
}
