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
    public event Action? LoginSucceeded;
    public event Action? NavigateToRegister;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _masterPassword = string.Empty;

    [ObservableProperty]
    private string _vaultPath = DatabaseService.DbPath;

    public LoginViewModel(EncryptionService crypto, DatabaseService db)
    {
        _crypto = crypto;
        _db     = db;
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

            var hexKey = _crypto.DeriveHexKey(MasterPassword, meta.Value.Salt);
            var opened = await _db.TryOpenAsync(hexKey);
            if (!opened)
            {
                ErrorMessage = "Could not unlock the vault. The database may be corrupted.";
                return;
            }

            MasterPassword = string.Empty;
            LoginSucceeded?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUnlock() => !string.IsNullOrEmpty(MasterPassword) && !IsBusy;

    [RelayCommand]
    private void NavigateRegister() => NavigateToRegister?.Invoke();

    // Reads the plaintext sidecar file that stores just the salt + verification hash.
    // This lets us confirm the password BEFORE trying to open the encrypted SQLCipher DB.
    private static async Task<(byte[] Salt, byte[] Hash)?> ReadMetaAsync()
    {
        if (!File.Exists(DatabaseService.MetaPath)) return null;
        var lines = await File.ReadAllLinesAsync(DatabaseService.MetaPath);
        if (lines.Length < 2) return null;
        return (Convert.FromBase64String(lines[0]), Convert.FromBase64String(lines[1]));
    }
}
