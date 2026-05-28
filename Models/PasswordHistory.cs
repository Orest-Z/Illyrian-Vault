namespace IllyrianVault.Models;

public class PasswordHistory
{
    public long     Id                { get; set; }
    public long     EntryId           { get; set; }
    public string   EncryptedPassword { get; set; } = string.Empty;
    public DateTime CreatedAt         { get; set; }
}
