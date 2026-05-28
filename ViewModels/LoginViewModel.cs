using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyriaVault.Services;

namespace IllyriaVault.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly EncryptionService _crypto;
    private readonly DatabaseService   _db;

    public event Action<string>? LoginSucceeded;
    public event Action?         NavigateToRegister;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileInitial))]
    [NotifyPropertyChangedFor(nameof(VaultPath))]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _masterPassword = string.Empty;

    public string ProfileInitial =>
        string.IsNullOrEmpty(Username) ? "?" : Username[0].ToString().ToUpperInvariant();

    public string VaultPath => DatabaseService.GetDbPath(Username);

    public LoginViewModel(EncryptionService crypto, DatabaseService db)
    {
        _crypto = crypto;
        _db     = db;
        TryAutoFillUsername();
    }

    private void TryAutoFillUsername()
    {
        var profiles = DatabaseService.ListProfiles();
        if (profiles.Count == 1)
            Username = profiles[0];
    }

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        ClearError();
        IsBusy = true;
        try
        {
            if (!DatabaseService.ProfileExists(Username))
            {
                ErrorMessage = "No vault found for that username.";
                return;
            }

            var metaPath = DatabaseService.GetMetaPath(Username);
            if (!File.Exists(metaPath))
            {
                ErrorMessage = "Vault data is missing or corrupted.";
                return;
            }

            var lines = await File.ReadAllLinesAsync(metaPath);
            if (lines.Length < 2)
            {
                ErrorMessage = "Vault data is missing or corrupted.";
                return;
            }

            var salt = Convert.FromBase64String(lines[0]);
            var hash = Convert.FromBase64String(lines[1]);

            if (!_crypto.VerifyPassword(MasterPassword, salt, hash))
            {
                ErrorMessage = "Incorrect master password. Please try again.";
                return;
            }

            var key    = _crypto.DeriveKey(MasterPassword, salt);
            App.SessionKey = key;
            var hexKey = Convert.ToHexString(key).ToLowerInvariant();

            _db.SetProfile(Username);
            var opened = await _db.TryOpenAsync(hexKey);
            if (!opened)
            {
                ErrorMessage = "Could not unlock the vault. The database may be corrupted.";
                return;
            }

            MasterPassword = string.Empty;
            LoginSucceeded?.Invoke(Username);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUnlock() =>
        !string.IsNullOrEmpty(Username) &&
        !string.IsNullOrEmpty(MasterPassword) &&
        !IsBusy;

    [RelayCommand]
    private void NavigateRegister() => NavigateToRegister?.Invoke();
}
