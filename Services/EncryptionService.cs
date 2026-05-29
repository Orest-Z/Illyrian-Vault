using System.Security.Cryptography;
using System.Text;

namespace IllyrianVault.Services;

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

    /// <summary>
    /// Span-based overload: password bytes come from a PinnedBuffer extracted
    /// from a SecureString — no System.String is ever materialised.
    /// Preferred code path for all login operations.
    /// </summary>
    public byte[] DeriveKey(ReadOnlySpan<byte> passwordBytes, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeyBytes);

    /// <summary>
    /// Legacy string overload kept for RegisterViewModel (which shows a
    /// live strength-score on every keystroke and requires string access).
    /// Do NOT use this path in new security-critical code.
    /// </summary>
    public byte[] DeriveKey(string masterPassword, byte[] salt)
    {
        byte[] pwBytes = Encoding.UTF8.GetBytes(masterPassword);
        try   { return DeriveKey((ReadOnlySpan<byte>)pwBytes, salt); }
        finally { CryptographicOperations.ZeroMemory(pwBytes); }
    }

    // ── Password verification ──────────────────────────────────────────────────

    /// <summary>
    /// Token = SHA-256(derivedKey ‖ "ILLYRIA_VERIFY").
    /// Stored alongside the salt so we can check the password without opening the DB.
    /// </summary>
    public byte[] CreateVerificationHash(ReadOnlySpan<byte> derivedKey)
    {
        ReadOnlySpan<byte> suffix  = "ILLYRIA_VERIFY"u8;
        byte[]             payload = new byte[derivedKey.Length + suffix.Length];
        derivedKey.CopyTo(payload);
        suffix.CopyTo(payload.AsSpan(derivedKey.Length));
        return SHA256.HashData(payload);
    }

    /// <summary>
    /// Span-based verification — the derived key is pinned by caller and never
    /// surfaces as a plain string.  Uses a fixed-time comparison to prevent
    /// timing-oracle attacks on the verification hash.
    /// </summary>
    public bool VerifyPassword(ReadOnlySpan<byte> passwordBytes, byte[] salt, byte[] storedHash)
    {
        byte[] key = DeriveKey(passwordBytes, salt);
        try
        {
            byte[] candidate = CreateVerificationHash(key);
            return CryptographicOperations.FixedTimeEquals(candidate, storedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Legacy string overload — delegates to span-based path.</summary>
    public bool VerifyPassword(string masterPassword, byte[] salt, byte[] storedHash)
    {
        byte[] pwBytes = Encoding.UTF8.GetBytes(masterPassword);
        try   { return VerifyPassword((ReadOnlySpan<byte>)pwBytes, salt, storedHash); }
        finally { CryptographicOperations.ZeroMemory(pwBytes); }
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
