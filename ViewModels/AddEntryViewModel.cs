using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyriaVault.Models;
using IllyriaVault.Services;

namespace IllyriaVault.ViewModels;

public partial class AddEntryViewModel : BaseViewModel
{
    private readonly EncryptionService _crypto;
    private readonly byte[]            _sessionKey;

    public event Action? SaveRequested;
    public event Action? CancelRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _title = string.Empty;

    [ObservableProperty] private string _username         = string.Empty;
    [ObservableProperty] private string _password         = string.Empty;
    [ObservableProperty] private string _url              = string.Empty;
    [ObservableProperty] private string _notes            = string.Empty;
    [ObservableProperty] private string _selectedCategory = "Login";

    public AddEntryViewModel(EncryptionService crypto, byte[] sessionKey)
    {
        _crypto     = crypto;
        _sessionKey = sessionKey;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => SaveRequested?.Invoke();

    private bool CanSave() => !string.IsNullOrWhiteSpace(Title);

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();

    public PasswordEntry BuildEntry() => new()
    {
        UserId            = 1,
        Title             = Title.Trim(),
        Username          = Username.Trim(),
        EncryptedPassword = string.IsNullOrEmpty(Password)
            ? string.Empty
            : _crypto.Encrypt(Password, _sessionKey),
        Url               = Url.Trim(),
        Notes             = Notes.Trim(),
        Category          = SelectedCategory,
    };
}
