using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyriaVault.Models;
using IllyriaVault.Services;

namespace IllyriaVault.ViewModels;

public partial class RegisterViewModel : BaseViewModel
{
    private readonly EncryptionService _crypto;
    private readonly DatabaseService   _db;

    public event Action? VaultCreated;
    public event Action? NavigateToLogin;

    // ── Step tracking (1–3) ────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private int _currentStep = 1;

    public bool   IsStep1    => CurrentStep == 1;
    public bool   IsStep2    => CurrentStep == 2;
    public bool   IsStep3    => CurrentStep == 3;
    public string StepLabel  => $"{CurrentStep} / 3";

    // ── Step 1: Passwords ──────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrengthScore))]
    [NotifyPropertyChangedFor(nameof(StrengthLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _confirmPassword = string.Empty;

    public int StrengthScore => EncryptionService.ScorePassword(NewPassword);

    public string StrengthLabel => StrengthScore switch
    {
        <= 1 => App.Localization["StrengthWeak"],
        2    => App.Localization["StrengthFair"],
        3    => App.Localization["StrengthGood"],
        4    => App.Localization["StrengthStrong"],
        _    => App.Localization["StrengthExcellent"],
    };

    // ── Step 2: Backup key ─────────────────────────────────────────────────────
    [ObservableProperty]
    private string _recoveryKey = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _isKeySaved;

    [RelayCommand]
    private void DownloadKey()
    {
        // In Phase 2 (Views), this will open a SaveFileDialog.
        IsKeySaved = true;
    }

    // ── Commands ───────────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (CurrentStep < 3)
        {
            if (CurrentStep == 1)
                RecoveryKey = _crypto.GenerateRecoveryKey();
            CurrentStep++;
        }
        else
        {
            await CreateVaultAsync();
        }
    }

    private bool CanGoNext() => CurrentStep switch
    {
        1 => NewPassword.Length >= 8 && NewPassword == ConfirmPassword,
        2 => IsKeySaved,
        _ => true,
    };

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (CurrentStep > 1) CurrentStep--;
    }

    private bool CanGoBack() => CurrentStep > 1;

    [RelayCommand]
    private void Cancel() => NavigateToLogin?.Invoke();

    // ── Vault creation ─────────────────────────────────────────────────────────
    private async Task CreateVaultAsync()
    {
        IsBusy = true;
        ClearError();
        try
        {
            var salt       = _crypto.GenerateSalt();
            var key        = _crypto.DeriveKey(NewPassword, salt);
            var hexKey     = Convert.ToHexString(key).ToLowerInvariant();
            var verifyHash = _crypto.CreateVerificationHash(key);

            // Write unencrypted sidecar: salt + verification hash only.
            // Login reads this to verify the password before opening the encrypted DB.
            Directory.CreateDirectory(Path.GetDirectoryName(DatabaseService.MetaPath)!);
            await File.WriteAllLinesAsync(DatabaseService.MetaPath, [
                Convert.ToBase64String(salt),
                Convert.ToBase64String(verifyHash),
            ]);

            var user = new VaultUser
            {
                DisplayName      = "Local Profile",
                PasswordSalt     = salt,
                VerificationHash = verifyHash,
                RecoveryKeyHash  = RecoveryKey,
                CreatedAt        = DateTime.UtcNow,
            };

            await _db.OpenAsync(hexKey);
            await _db.SaveUserAsync(user);

            NewPassword     = string.Empty;
            ConfirmPassword = string.Empty;
            VaultCreated?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create vault: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public RegisterViewModel(EncryptionService crypto, DatabaseService db)
    {
        _crypto = crypto;
        _db     = db;
    }
}
