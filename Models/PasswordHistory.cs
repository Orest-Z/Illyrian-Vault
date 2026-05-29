/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
namespace IllyrianVault.Models;

public class PasswordHistory
{
    public long     Id                { get; set; }
    public long     EntryId           { get; set; }
    public string   EncryptedPassword { get; set; } = string.Empty;
    public DateTime CreatedAt         { get; set; }
}
