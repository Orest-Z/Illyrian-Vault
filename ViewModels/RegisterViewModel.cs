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

    // VaultCreated carries the username so MainWindow can display it.
    public event Action<string>? VaultCreated;
    public event Action?         NavigateToLogin;

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

    // ── Step 1: Username + Passwords ──────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _username = string.Empty;

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
        1 => !string.IsNullOrWhiteSpace(Username) && NewPassword.Length >= 8 && NewPassword == ConfirmPassword,
        2 => IsKeySaved,
        _ => true,
    };

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            return;
        }

        // Step 1 → back means "cancel setup": wipe every partially typed field
        // before handing control back to the Login screen.
        Username        = string.Empty;
        NewPassword     = string.Empty;
        ConfirmPassword = string.Empty;
        RecoveryKey     = string.Empty;
        IsKeySaved      = false;
        ClearError();
        NavigateToLogin?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => NavigateToLogin?.Invoke();

    // ── Vault creation ─────────────────────────────────────────────────────────
    private async Task CreateVaultAsync()
    {
        IsBusy = true;
        ClearError();
        try
        {
            if (DatabaseService.ProfileExists(Username))
            {
                ErrorMessage = "Username already exists on this device.";
                return;
            }

            var salt       = _crypto.GenerateSalt();
            var key        = _crypto.DeriveKey(NewPassword, salt);
            var hexKey     = Convert.ToHexString(key).ToLowerInvariant();
            var verifyHash = _crypto.CreateVerificationHash(key);

            // Create the isolated profile folder and write the plaintext sidecar.
            // Login reads the sidecar to verify the password before opening the encrypted DB.
            var profileDir = DatabaseService.GetProfileDir(Username);
            var metaPath   = DatabaseService.GetMetaPath(Username);
            Directory.CreateDirectory(profileDir);
            await File.WriteAllLinesAsync(metaPath, [
                Convert.ToBase64String(salt),
                Convert.ToBase64String(verifyHash),
                Username,
            ]);

            var user = new VaultUser
            {
                Username         = Username,
                DisplayName      = "Local Profile",
                PasswordSalt     = salt,
                VerificationHash = verifyHash,
                RecoveryKeyHash  = RecoveryKey,
                CreatedAt        = DateTime.UtcNow,
            };

            App.SessionKey = key;
            _db.SetProfile(Username);
            await _db.OpenAsync(hexKey);
            await _db.SaveUserAsync(user);

            NewPassword     = string.Empty;
            ConfirmPassword = string.Empty;
            VaultCreated?.Invoke(Username);
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
