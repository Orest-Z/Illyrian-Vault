using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyriaVault.Models;
using IllyriaVault.Services;

namespace IllyriaVault.ViewModels;

public enum BreachStatus { NotChecked, Checking, Safe, Breached, Error }

public class PasswordHistoryItemVm
{
    public string Plaintext { get; }
    public string DateLabel { get; }

    public PasswordHistoryItemVm(string encryptedPassword, DateTime createdAt, EncryptionService crypto, byte[] key)
    {
        Plaintext = crypto.Decrypt(encryptedPassword, key);
        DateLabel = createdAt.ToLocalTime().ToString("MMM dd, yyyy  HH:mm");
    }
}

// Wraps a PasswordEntry with display-time computed properties.
// Java analogy: this is a DTO/Projection on top of the raw Model.
public partial class EntryViewModel : ObservableObject
{
    private readonly EncryptionService _crypto;
    private readonly byte[]            _key;
    private readonly DatabaseService   _db;

    public PasswordEntry Model { get; }

    // Fired after a successful save so MainViewModel can refresh SelectedEntry bindings.
    public event Action? SaveCompleted;

    [ObservableProperty]
    private bool _isPasswordRevealed;

    // Observable wrapper so UI reacts when ToggleFavorite runs.
    [ObservableProperty]
    private bool _isFavorite;

    // ── Breach check state ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBreachNotChecked))]
    [NotifyPropertyChangedFor(nameof(ShowBreachChecking))]
    [NotifyPropertyChangedFor(nameof(ShowBreachSafe))]
    [NotifyPropertyChangedFor(nameof(ShowBreachBreached))]
    [NotifyPropertyChangedFor(nameof(ShowBreachError))]
    [NotifyCanExecuteChangedFor(nameof(CheckBreachCommand))]
    private BreachStatus _breachStatus = BreachStatus.NotChecked;

    [ObservableProperty]
    private string _breachMessage = string.Empty;

    public bool ShowBreachNotChecked => BreachStatus == BreachStatus.NotChecked;
    public bool ShowBreachChecking   => BreachStatus == BreachStatus.Checking;
    public bool ShowBreachSafe       => BreachStatus == BreachStatus.Safe;
    public bool ShowBreachBreached   => BreachStatus == BreachStatus.Breached;
    public bool ShowBreachError      => BreachStatus == BreachStatus.Error;

    [RelayCommand(CanExecute = nameof(CanCheckBreach))]
    private async Task CheckBreachAsync()
    {
        if (string.IsNullOrEmpty(Model.EncryptedPassword)) return;

        var plaintext = _crypto.Decrypt(Model.EncryptedPassword, _key);
        BreachStatus  = BreachStatus.Checking;
        BreachMessage = string.Empty;

        try
        {
            var result    = await BreachCheckService.CheckPasswordAsync(plaintext);
            BreachStatus  = result.IsNetworkUnavailable ? BreachStatus.Error
                          : result.IsBreached           ? BreachStatus.Breached
                          :                               BreachStatus.Safe;
            BreachMessage = result.Message;
        }
        catch (Exception ex)
        {
            BreachStatus  = BreachStatus.Error;
            BreachMessage = $"Check failed: {ex.Message}";
        }
    }

    private bool CanCheckBreach() => BreachStatus != BreachStatus.Checking;

    // ── Edit state ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    private bool _isEditing;

    public bool IsNotEditing => !IsEditing;

    [ObservableProperty] private string _editTitle    = string.Empty;
    [ObservableProperty] private string _editUsername = string.Empty;
    [ObservableProperty] private string _editPassword = string.Empty;
    [ObservableProperty] private string _editUrl      = string.Empty;
    [ObservableProperty] private string _editNotes    = string.Empty;

    [RelayCommand]
    private void Edit()
    {
        EditTitle    = Model.Title;
        EditUsername = Model.Username;
        EditPassword = string.IsNullOrEmpty(Model.EncryptedPassword)
            ? string.Empty
            : _crypto.Decrypt(Model.EncryptedPassword, _key);
        EditUrl      = Model.Url;
        EditNotes    = Model.Notes;
        IsEditing    = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var oldEncrypted = Model.EncryptedPassword;
        var newEncrypted = string.IsNullOrWhiteSpace(EditPassword)
            ? string.Empty
            : _crypto.Encrypt(EditPassword, _key);

        // Archive the old password if it changed and there was one.
        if (!string.IsNullOrEmpty(oldEncrypted) && oldEncrypted != newEncrypted)
            await _db.InsertPasswordHistoryAsync(Model.Id, oldEncrypted);

        Model.Title             = EditTitle;
        Model.Username          = EditUsername;
        Model.EncryptedPassword = newEncrypted;
        Model.Url               = EditUrl;
        Model.Notes             = EditNotes;

        await _db.UpdateEntryAsync(Model);

        IsEditing = false;
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplayUsername));
        OnPropertyChanged(nameof(TileInitial));
        OnPropertyChanged(nameof(UpdatedFormatted));
        OnPropertyChanged(nameof(DisplayPassword));
        SaveCompleted?.Invoke();
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    // ── Password history ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHistoryEmpty))]
    private bool _showHistory;

    public ObservableCollection<PasswordHistoryItemVm> History { get; } = [];
    public bool ShowHistoryEmpty => History.Count == 0;

    [RelayCommand]
    private async Task ToggleHistoryAsync()
    {
        if (!ShowHistory)
        {
            History.Clear();
            var rows = await _db.GetPasswordHistoryAsync(Model.Id);
            foreach (var r in rows)
                History.Add(new PasswordHistoryItemVm(r.EncryptedPassword, r.CreatedAt, _crypto, _key));
            OnPropertyChanged(nameof(ShowHistoryEmpty));
        }
        ShowHistory = !ShowHistory;
    }

    // ── Display helpers ────────────────────────────────────────────────────────

    partial void OnIsPasswordRevealedChanged(bool value) =>
        OnPropertyChanged(nameof(DisplayPassword));

    partial void OnIsFavoriteChanged(bool value)
    {
        Model.IsFavorite = value;
        OnPropertyChanged(nameof(IsFavoriteColor));
    }

    // Foreground color helper: gold when favorite, muted otherwise.
    public System.Windows.Media.Brush IsFavoriteColor =>
        IsFavorite
            ? (System.Windows.Application.Current.TryFindResource("BrushWarning") as System.Windows.Media.Brush)
              ?? System.Windows.Media.Brushes.Goldenrod
            : (System.Windows.Application.Current.TryFindResource("BrushTextSecondary") as System.Windows.Media.Brush)
              ?? System.Windows.Media.Brushes.Gray;

    public string DisplayTitle    => Model.Title;
    public string DisplayUsername => Model.Username;
    public string TileInitial      => string.IsNullOrEmpty(Model.Title) ? "?" : Model.Title[0].ToString().ToUpperInvariant();
    public string UpdatedFormatted => Model.UpdatedAt.ToString("MMM dd, yyyy");

    public string DisplayPassword =>
        IsPasswordRevealed && !string.IsNullOrEmpty(Model.EncryptedPassword)
            ? _crypto.Decrypt(Model.EncryptedPassword, _key)
            : "••••••••••••••••";

    public EntryViewModel(PasswordEntry model, EncryptionService crypto, byte[] key, DatabaseService db)
    {
        Model       = model;
        _crypto     = crypto;
        _key        = key;
        _db         = db;
        _isFavorite = model.IsFavorite;
    }
}

public partial class MainViewModel : BaseViewModel
{
    private readonly DatabaseService   _db;
    private readonly EncryptionService _crypto;
    private readonly byte[]            _sessionKey;

    // Fired when the user locks the vault — MainWindow subscribes and shows AuthWindow.
    public event Action? LockRequested;

    // Fired when the user clicks New Entry — MainWindow opens AddEntryWindow.
    public event Action? NewEntryRequested;

    // ── Profile ────────────────────────────────────────────────────────────────
    public string ProfileUsername { get; }
    public string ProfileInitial  => string.IsNullOrEmpty(ProfileUsername) ? "?" : ProfileUsername[0].ToString().ToUpperInvariant();

    // ── Tools ──────────────────────────────────────────────────────────────────
    public PasswordGeneratorViewModel Generator { get; } = new();
    public string AppVersion => System.Reflection.Assembly
        .GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    // ── Theme / Language ───────────────────────────────────────────────────────
    [ObservableProperty]
    private string _currentTheme = App.Theme.Current switch
    {
        AppTheme.Light => "Light",
        AppTheme.Dark  => "Dark",
        _              => "Red",
    };

    [ObservableProperty]
    private string _currentLanguage = App.Localization.Current == AppLanguage.Sq ? "SQ" : "EN";

    partial void OnCurrentThemeChanged(string value)
    {
        var theme = value switch { "Light" => AppTheme.Light, "Dark" => AppTheme.Dark, _ => AppTheme.Red };
        App.Theme.Apply(theme);
    }

    partial void OnCurrentLanguageChanged(string value) =>
        App.Localization.Apply(value == "SQ" ? AppLanguage.Sq : AppLanguage.En);

    // ── Entry collections ──────────────────────────────────────────────────────
    private readonly ObservableCollection<EntryViewModel> _allEntries = [];

    // The ListBox in the View binds to this filtered list.
    public ObservableCollection<EntryViewModel> FilteredEntries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEntry))]
    [NotifyPropertyChangedFor(nameof(HasNoSelectedEntry))]
    [NotifyPropertyChangedFor(nameof(ShowEntryDetail))]
    [NotifyPropertyChangedFor(nameof(ShowEntryEmpty))]
    private EntryViewModel? _selectedEntry;

    public bool HasSelectedEntry   => SelectedEntry is not null;
    public bool HasNoSelectedEntry => SelectedEntry is null;
    public bool IsVaultSection     => SelectedSection is not ("generator" or "settings");
    public bool IsGeneratorSection => SelectedSection == "generator";
    public bool IsSettingsSection  => SelectedSection == "settings";
    public bool ShowEntryDetail    => IsVaultSection && SelectedEntry is not null;
    public bool ShowEntryEmpty     => IsVaultSection && SelectedEntry is null;

    // ── Search / filter state ──────────────────────────────────────────────────
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVaultSection))]
    [NotifyPropertyChangedFor(nameof(IsGeneratorSection))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSection))]
    [NotifyPropertyChangedFor(nameof(ShowEntryDetail))]
    [NotifyPropertyChangedFor(nameof(ShowEntryEmpty))]
    private string _selectedSection = "all";

    partial void OnSelectedSectionChanged(string value) => ApplyFilter();

    // ── Nav badge counts ───────────────────────────────────────────────────────
    public int TotalCount    => _allEntries.Count;
    public int FavoriteCount => _allEntries.Count(e => e.IsFavorite);
    public int LoginCount    => _allEntries.Count(e => e.Model.Category == "Login");
    public int NoteCount     => _allEntries.Count(e => e.Model.Category == "Note");
    public int CardCount     => _allEntries.Count(e => e.Model.Category == "Card");
    public int IdentityCount => _allEntries.Count(e => e.Model.Category == "Identity");

    // ── Commands ───────────────────────────────────────────────────────────────
    [RelayCommand]
    private void RequestNewEntry() => NewEntryRequested?.Invoke();

    [RelayCommand]
    private async Task LoadEntriesAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await _db.GetAllEntriesAsync();
            _allEntries.Clear();
            foreach (var e in entries)
                AddEntryVm(new EntryViewModel(e, _crypto, _sessionKey, _db));
            ApplyFilter();
            RefreshCounts();
        }
        finally { IsBusy = false; }
    }

    private void AddEntryVm(EntryViewModel vm)
    {
        vm.SaveCompleted += () => OnPropertyChanged(nameof(SelectedEntry));
        _allEntries.Add(vm);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedEntry is null) return;
        SelectedEntry.IsFavorite = !SelectedEntry.IsFavorite;
        await _db.SetFavoriteAsync(SelectedEntry.Model.Id, SelectedEntry.IsFavorite);
        RefreshCounts();
    }

    [RelayCommand]
    private async Task DeleteEntryAsync()
    {
        if (SelectedEntry is null) return;
        await _db.DeleteEntryAsync(SelectedEntry.Model.Id);
        _allEntries.Remove(SelectedEntry);
        SelectedEntry = FilteredEntries.FirstOrDefault();
        ApplyFilter();
        RefreshCounts();
    }

    [RelayCommand]
    private void CopyUsername()
    {
        if (SelectedEntry is null) return;
        System.Windows.Clipboard.SetText(SelectedEntry.Model.Username);
    }

    [RelayCommand]
    private void CopyPassword()
    {
        if (SelectedEntry is null || string.IsNullOrEmpty(SelectedEntry.Model.EncryptedPassword)) return;
        var plaintext = _crypto.Decrypt(SelectedEntry.Model.EncryptedPassword, _sessionKey);
        System.Windows.Clipboard.SetText(plaintext);
    }

    [RelayCommand]
    private void TogglePasswordReveal()
    {
        if (SelectedEntry is null) return;
        SelectedEntry.IsPasswordRevealed = !SelectedEntry.IsPasswordRevealed;
    }

    [RelayCommand]
    private void NavigateSection(string section) => SelectedSection = section;

    public async Task InsertNewEntryAsync(PasswordEntry e)
    {
        e.Id = await _db.InsertEntryAsync(e);
        AddEntryVm(new EntryViewModel(e, _crypto, _sessionKey, _db));
        ApplyFilter();
        RefreshCounts();
        SelectedEntry = FilteredEntries.FirstOrDefault(x => x.Model.Id == e.Id);
    }

    [RelayCommand]
    private async Task LockAsync()
    {
        App.SessionKey = [];
        await App.Database.DisposeAsync();
        LockRequested?.Invoke();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        App.SessionKey = [];
        await App.Database.DisposeAsync();
        LockRequested?.Invoke();
    }

    // ── Filtering ──────────────────────────────────────────────────────────────
    private void ApplyFilter()
    {
        var q = _allEntries.AsEnumerable();

        q = SelectedSection switch
        {
            "favorites"  => q.Where(e => e.IsFavorite),
            "logins"     => q.Where(e => e.Model.Category == "Login"),
            "notes"      => q.Where(e => e.Model.Category == "Note"),
            "cards"      => q.Where(e => e.Model.Category == "Card"),
            "identities" => q.Where(e => e.Model.Category == "Identity"),
            _            => q,
        };

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var f = SearchQuery.Trim();
            q = q.Where(e =>
                e.Model.Title.Contains(f, StringComparison.OrdinalIgnoreCase)    ||
                e.Model.Username.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.Model.Url.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        FilteredEntries.Clear();
        foreach (var e in q) FilteredEntries.Add(e);

        if (SelectedEntry is not null && !FilteredEntries.Contains(SelectedEntry))
            SelectedEntry = FilteredEntries.FirstOrDefault();
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(LoginCount));
        OnPropertyChanged(nameof(NoteCount));
        OnPropertyChanged(nameof(CardCount));
        OnPropertyChanged(nameof(IdentityCount));
    }

    public MainViewModel(DatabaseService db, EncryptionService crypto, byte[] sessionKey, string username)
    {
        _db         = db;
        _crypto     = crypto;
        _sessionKey = sessionKey;
        ProfileUsername = username;
    }
}
