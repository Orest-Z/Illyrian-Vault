/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyrianVault.Services;

namespace IllyrianVault.ViewModels;

public partial class RecoveryViewModel : BaseViewModel
{
    private readonly EncryptionService _crypto;
    private readonly DatabaseService   _db;

    public event Action<string>? RecoverySucceeded;
    public event Action?         NavigateToLogin;

    // ── Step tracking ──────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private int _currentStep = 1;

    public bool   IsStep1   => CurrentStep == 1;
    public bool   IsStep2   => CurrentStep == 2;
    public bool   IsStep3   => CurrentStep == 3;
    public string StepLabel => $"{CurrentStep} / 3";

    // ── Step 1: username ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _username = string.Empty;

    // ── Step 2: recovery key ───────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _recoveryKeyInput = string.Empty;

    // ── Step 3: new password ───────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewStrengthScore))]
    [NotifyPropertyChangedFor(nameof(NewStrengthLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _confirmPassword = string.Empty;

    public int    NewStrengthScore => EncryptionService.ScorePassword(NewPassword);

    public string NewStrengthLabel => NewStrengthScore switch
    {
        <= 1 => App.Localization["StrengthWeak"],
        2    => App.Localization["StrengthFair"],
        3    => App.Localization["StrengthGood"],
        4    => App.Localization["StrengthStrong"],
        _    => App.Localization["StrengthExcellent"],
    };

    private bool CanGoNext() => CurrentStep switch
    {
        1 => !string.IsNullOrWhiteSpace(Username) && DatabaseService.ProfileExists(Username),
        2 => RecoveryKeyInput.StartsWith("ILVT-") && RecoveryKeyInput.Length > 10,
        3 => NewPassword.Length >= 8 && NewPassword == ConfirmPassword && NewStrengthScore >= 3,
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        ClearError();
        if (CurrentStep == 1) { CurrentStep++; return; }
        if (CurrentStep == 2)
        {
            await VerifyRecoveryKeyAsync();
            return;
        }
        await ResetPasswordAsync();
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1) { CurrentStep--; return; }
        NavigateToLogin?.Invoke();
    }

    // ── Verify recovery key against vault.meta ────────────────────────────────
    private async Task VerifyRecoveryKeyAsync()
    {
        IsBusy = true;
        try
        {
            var metaPath = DatabaseService.GetMetaPath(Username);
            if (!File.Exists(metaPath)) { ErrorMessage = "Vault data not found."; return; }

            var lines = await File.ReadAllLinesAsync(metaPath);
            if (lines.Length < 8) { ErrorMessage = "This vault was created before recovery support was added. Recovery is not available."; return; }

            // Verify the recovery key by attempting to decrypt the wrapped master key.
            // If AES-GCM authentication passes, the key is correct.
            var recoverySalt     = Convert.FromBase64String(lines[6]);
            var wrappedMasterKey = lines[7];

            var recoveryKeyBytes   = Encoding.UTF8.GetBytes(RecoveryKeyInput.Trim());
            var recoveryDerivedKey = Rfc2898DeriveBytes.Pbkdf2(
                recoveryKeyBytes, recoverySalt, 600_000, HashAlgorithmName.SHA512, 32);
            CryptographicOperations.ZeroMemory(recoveryKeyBytes);

            try
            {
                var masterKeyHex = _crypto.Decrypt(wrappedMasterKey, recoveryDerivedKey);
                CryptographicOperations.ZeroMemory(recoveryDerivedKey);
                // Stash the recovered master key hex for use in Step 3.
                _recoveredMasterKeyHex = masterKeyHex;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(recoveryDerivedKey);
                ErrorMessage = "Recovery key is incorrect.";
                return;
            }

            CurrentStep++;
        }
        finally { IsBusy = false; }
    }

    private string _recoveredMasterKeyHex = string.Empty;

    // ── Re-key the vault with the new master password ─────────────────────────
    private async Task ResetPasswordAsync()
    {
        IsBusy = true;
        try
        {
            var metaPath      = DatabaseService.GetMetaPath(Username);
            var lines         = (await File.ReadAllLinesAsync(metaPath)).ToList();
            var oldMasterKey  = Convert.FromHexString(_recoveredMasterKeyHex);
            _recoveredMasterKeyHex = string.Empty;

            // Open DB with the recovered old master key.
            _db.SetProfile(Username);
            bool opened = await _db.TryOpenAsync(oldMasterKey);
            if (!opened) { ErrorMessage = "Could not open vault. The vault file may be corrupted."; return; }

            // Derive the new master key.
            var newSalt      = _crypto.GenerateSalt();
            var newMasterKey = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(NewPassword), newSalt, 600_000, HashAlgorithmName.SHA512, 32);
            var newVerifyHash = _crypto.CreateVerificationHash(newMasterKey);

            // Re-encrypt all AES-GCM fields with the new key.
            var entries = await _db.GetAllEntriesAsync();
            foreach (var e in entries)
            {
                if (!string.IsNullOrEmpty(e.EncryptedPassword))
                    e.EncryptedPassword = _crypto.Encrypt(_crypto.Decrypt(e.EncryptedPassword, oldMasterKey), newMasterKey);
                if (!string.IsNullOrEmpty(e.EncryptedPayload))
                    e.EncryptedPayload = _crypto.Encrypt(_crypto.Decrypt(e.EncryptedPayload, oldMasterKey), newMasterKey);
                await _db.UpdateEntryAsync(e);
            }
            var history = await _db.GetAllPasswordHistoryAsync();
            foreach (var h in history)
            {
                var reEncrypted = _crypto.Encrypt(_crypto.Decrypt(h.EncryptedPassword, oldMasterKey), newMasterKey);
                await _db.UpdatePasswordHistoryEncryptedValueAsync(h.Id, reEncrypted);
            }

            // Re-key the SQLCipher database itself.
            await _db.RekeyAsync(newMasterKey);

            // Wrap the new master key with the same recovery key for future use.
            var recoverySalt       = _crypto.GenerateSalt();
            var recoveryKeyBytes   = Encoding.UTF8.GetBytes(RecoveryKeyInput.Trim());
            var recoveryDerivedKey = Rfc2898DeriveBytes.Pbkdf2(
                recoveryKeyBytes, recoverySalt, 600_000, HashAlgorithmName.SHA512, 32);
            CryptographicOperations.ZeroMemory(recoveryKeyBytes);
            var wrappedMasterKey = _crypto.Encrypt(Convert.ToHexString(newMasterKey), recoveryDerivedKey);
            CryptographicOperations.ZeroMemory(recoveryDerivedKey);

            // Update vault.meta.
            while (lines.Count < 8) lines.Add(string.Empty);
            lines[0] = Convert.ToBase64String(newSalt);
            lines[1] = Convert.ToBase64String(newVerifyHash);
            lines[3] = "v2";
            lines[4] = "0";
            lines[5] = "0";
            lines[6] = Convert.ToBase64String(recoverySalt);
            lines[7] = wrappedMasterKey;
            await File.WriteAllLinesAsync(metaPath, lines);

            CryptographicOperations.ZeroMemory(oldMasterKey);

            // Set session key so the user lands directly in the vault.
            var sessionCopy = new byte[newMasterKey.Length];
            Buffer.BlockCopy(newMasterKey, 0, sessionCopy, 0, newMasterKey.Length);
            App.SetSessionKey(sessionCopy);
            CryptographicOperations.ZeroMemory(newMasterKey);

            NewPassword     = string.Empty;
            ConfirmPassword = string.Empty;
            RecoverySucceeded?.Invoke(Username);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Recovery failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    public RecoveryViewModel(EncryptionService crypto, DatabaseService db)
    {
        _crypto = crypto;
        _db     = db;
    }
}
