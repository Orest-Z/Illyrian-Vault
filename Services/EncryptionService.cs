using System.Security.Cryptography;
using System.Text;

namespace IllyriaVault.Services;

/// <summary>
/// All cryptographic operations for Illyria Vault.
/// Java analogy:
///   DeriveKey() ≈ SecretKeyFactory.generateSecret(new PBEKeySpec(...))
///   Encrypt()   ≈ Cipher.getInstance("AES/GCM/NoPadding") + cipher.doFinal()
/// </summary>
public sealed class EncryptionService
{
    private const int KeyBytes         = 32;        // AES-256
    private const int SaltBytes        = 32;        // 256-bit PBKDF2 salt
    private const int GcmNonceBytes    = 12;        // 96-bit GCM nonce (NIST recommendation)
    private const int GcmTagBytes      = 16;        // 128-bit authentication tag
    private const int Pbkdf2Iterations = 600_000;   // OWASP 2023 minimum for PBKDF2-SHA256

    // ── Salt ───────────────────────────────────────────────────────────────────

    public byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltBytes);

    // ── Key derivation (PBKDF2-SHA256) ────────────────────────────────────────

    public byte[] DeriveKey(string masterPassword, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(masterPassword),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeyBytes);

    /// <summary>
    /// Returns the key as a lowercase hex string for SQLCipher:
    ///   PRAGMA key = "x'hexstring'"
    /// This bypasses SQLCipher's own internal PBKDF2 and uses our 256-bit key directly.
    /// </summary>
    public string DeriveHexKey(string masterPassword, byte[] salt) =>
        Convert.ToHexString(DeriveKey(masterPassword, salt)).ToLowerInvariant();

    // ── Password verification ──────────────────────────────────────────────────

    /// <summary>
    /// Token = SHA-256(derivedKey ‖ "ILLYRIA_VERIFY").
    /// Stored alongside the salt so we can check the password without opening the DB.
    /// </summary>
    public byte[] CreateVerificationHash(byte[] derivedKey)
    {
        var suffix  = "ILLYRIA_VERIFY"u8.ToArray();
        var payload = new byte[derivedKey.Length + suffix.Length];
        derivedKey.CopyTo(payload, 0);
        suffix.CopyTo(payload, derivedKey.Length);
        return SHA256.HashData(payload);
    }

    public bool VerifyPassword(string masterPassword, byte[] salt, byte[] storedHash)
    {
        var key       = DeriveKey(masterPassword, salt);
        var candidate = CreateVerificationHash(key);
        return CryptographicOperations.FixedTimeEquals(candidate, storedHash);
    }

    // ── Field-level AES-256-GCM ────────────────────────────────────────────────

    /// <summary>
    /// Wire format: [12 B nonce][16 B GCM tag][N B ciphertext] → Base64.
    /// A fresh nonce is generated per call.
    /// </summary>
    public string Encrypt(string plaintext, byte[] key)
    {
        var nonce          = RandomNumberGenerator.GetBytes(GcmNonceBytes);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext     = new byte[plaintextBytes.Length];
        var tag            = new byte[GcmTagBytes];

        using var aes = new AesGcm(key, GcmTagBytes);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var packed = new byte[GcmNonceBytes + GcmTagBytes + ciphertext.Length];
        nonce.CopyTo(packed, 0);
        tag.CopyTo(packed, GcmNonceBytes);
        ciphertext.CopyTo(packed, GcmNonceBytes + GcmTagBytes);
        return Convert.ToBase64String(packed);
    }

    public string Decrypt(string encryptedBase64, byte[] key)
    {
        var packed     = Convert.FromBase64String(encryptedBase64);
        var nonce      = packed[..GcmNonceBytes];
        var tag        = packed[GcmNonceBytes..(GcmNonceBytes + GcmTagBytes)];
        var ciphertext = packed[(GcmNonceBytes + GcmTagBytes)..];
        var plaintext  = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, GcmTagBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    // ── Recovery key ──────────────────────────────────────────────────────────

    /// <summary>
    /// Generates: ILVT-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX
    /// 16 random bytes → 32 uppercase hex chars → 8 groups of 4.
    /// </summary>
    public string GenerateRecoveryKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        var hex   = Convert.ToHexString(bytes).ToUpperInvariant();
        var parts = Enumerable.Range(0, 8).Select(i => hex.Substring(i * 4, 4));
        return "ILVT-" + string.Join("-", parts);
    }

    // ── Strength scoring (mirrors the HTML's scorePassword function) ───────────

    public static int ScorePassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return 0;
        int score = 0;
        if (password.Length >= 8)  score++;
        if (password.Length >= 14) score++;
        int variety = (password.Any(char.IsLower)                  ? 1 : 0)
                    + (password.Any(char.IsUpper)                  ? 1 : 0)
                    + (password.Any(char.IsDigit)                  ? 1 : 0)
                    + (password.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
        score += variety - 1;
        return Math.Clamp(score, 0, 5);
    }
}
