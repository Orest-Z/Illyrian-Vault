using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyriaVault.Services;

namespace IllyriaVault.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly EncryptionService _crypto;
    private readonly DatabaseService   _db;

    // AuthWindow subscribes to these to navigate between screens.
    // LoginSucceeded carries the username so MainWindow can display it.
    public event Action<string>? LoginSucceeded;
    public event Action?         NavigateToRegister;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileInitial))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _masterPassword = string.Empty;

    [ObservableProperty]
    private string _vaultPath = DatabaseService.DbPath;

    public string ProfileInitial =>
        string.IsNullOrEmpty(Username) ? "?" : Username[0].ToString().ToUpperInvariant();

    public LoginViewModel(EncryptionService crypto, DatabaseService db)
    {
        _crypto = crypto;
        _db     = db;

        // Pre-fill username from the vault.meta sidecar if the vault exists.
        if (db.VaultExists)
            _ = PreFillUsernameAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        ClearError();
        IsBusy = true;
        try
        {
            var meta = await ReadMetaAsync();
            if (meta is null)
            {
                ErrorMessage = "Vault not found. Please create a new vault.";
                return;
            }

            // Fast verification check before touching the encrypted DB.
            if (!_crypto.VerifyPassword(MasterPassword, meta.Value.Salt, meta.Value.Hash))
            {
                ErrorMessage = "Incorrect master password. Please try again.";
                return;
            }

            var key    = _crypto.DeriveKey(MasterPassword, meta.Value.Salt);
            App.SessionKey = key;
            var hexKey = Convert.ToHexString(key).ToLowerInvariant();
            var opened = await _db.TryOpenAsync(hexKey);
            if (!opened)
            {
                ErrorMessage = "Could not unlock the vault. The database may be corrupted.";
                return;
            }

            var username   = string.IsNullOrWhiteSpace(Username) ? meta.Value.Username : Username;
            MasterPassword = string.Empty;
            LoginSucceeded?.Invoke(username);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUnlock() =>
        !string.IsNullOrEmpty(MasterPassword) &&
        !IsBusy;

    [RelayCommand]
    private void NavigateRegister() => NavigateToRegister?.Invoke();

    private async Task PreFillUsernameAsync()
    {
        var meta = await ReadMetaAsync();
        if (meta is not null && !string.IsNullOrEmpty(meta.Value.Username))
            Username = meta.Value.Username;
    }

    // Reads the plaintext sidecar: line 0 = salt, line 1 = verification hash, line 2 = username.
    private static async Task<(byte[] Salt, byte[] Hash, string Username)?> ReadMetaAsync()
    {
        if (!File.Exists(DatabaseService.MetaPath)) return null;
        var lines = await File.ReadAllLinesAsync(DatabaseService.MetaPath);
        if (lines.Length < 2) return null;
        var username = lines.Length >= 3 ? lines[2] : string.Empty;
        return (Convert.FromBase64String(lines[0]), Convert.FromBase64String(lines[1]), username);
    }
}
