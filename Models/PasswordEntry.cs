namespace IllyriaVault.Models;

public sealed class PasswordEntry
{
    public long     Id                { get; set; }
    public string   Title             { get; set; } = string.Empty;
    public string   Username          { get; set; } = string.Empty;
    public string   EncryptedPassword { get; set; } = string.Empty;
    public string   Url               { get; set; } = string.Empty;
    public string   Notes             { get; set; } = string.Empty;
    public string   Category          { get; set; } = "Login"; // Login | Note | Card | Identity
    public bool     IsFavorite        { get; set; }
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt         { get; set; } = DateTime.UtcNow;
}
