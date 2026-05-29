/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
namespace IllyrianVault.Models;

public sealed class VaultUser
{
    public long     Id               { get; set; }
    public string   Username         { get; set; } = string.Empty;
    public string   DisplayName      { get; set; } = "Local Profile";
    public byte[]   PasswordSalt     { get; set; } = [];
    public byte[]   VerificationHash { get; set; } = [];
    public string   RecoveryKeyHash  { get; set; } = string.Empty;
    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
}
