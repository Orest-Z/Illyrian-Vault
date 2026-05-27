using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyriaVault.Models;
using IllyriaVault.Services;

namespace IllyriaVault.ViewModels;

// Wraps a PasswordEntry with display-time computed properties.
// Java analogy: this is a DTO/Projection on top of the raw Model.
public partial class EntryViewModel : ObservableObject
{
    private readonly EncryptionService _crypto;
    private readonly byte[]            _key;

    public PasswordEntry Model { get; }

    [ObservableProperty]
    private bool _isPasswordRevealed;

    partial void OnIsPasswordRevealedChanged(bool value) =>
        OnPropertyChanged(nameof(DisplayPassword));

    public string TileInitial      => string.IsNullOrEmpty(Model.Title) ? "?" : Model.Title[0].ToString().ToUpperInvariant();
    public string UpdatedFormatted => Model.UpdatedAt.ToString("MMM dd, yyyy");

    public string DisplayPassword =>
        IsPasswordRevealed && !string.IsNullOrEmpty(Model.EncryptedPassword)
            ? _crypto.Decrypt(Model.EncryptedPassword, _key)
            : "••••••••••••••••";

    public EntryViewModel(PasswordEntry model, EncryptionService crypto, byte[] key)
    {
        Model   = model;
        _crypto = crypto;
        _key    = key;
    }
}

public partial class MainViewModel : BaseViewModel
{
    private readonly DatabaseService   _db;
    private readonly EncryptionService _crypto;
    private readonly byte[]            _sessionKey;

    // Fired when the user locks the vault — MainWindow subscribes and shows AuthWindow.
    public event Action? LockRequested;

    // ── Entry collections ──────────────────────────────────────────────────────
    private readonly ObservableCollection<EntryViewModel> _allEntries = [];

    // The ListBox in the View binds to this filtered list.
    public ObservableCollection<EntryViewModel> FilteredEntries { get; } = [];

    [ObservableProperty]
    private EntryViewModel? _selectedEntry;

    // ── Search / filter state ──────────────────────────────────────────────────
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    [ObservableProperty]
    private string _selectedSection = "all";

    partial void OnSelectedSectionChanged(string value) => ApplyFilter();

    // ── Nav badge counts ───────────────────────────────────────────────────────
    public int TotalCount    => _allEntries.Count;
    public int FavoriteCount => _allEntries.Count(e => e.Model.IsFavorite);
    public int LoginCount    => _allEntries.Count(e => e.Model.Category == "Login");
    public int NoteCount     => _allEntries.Count(e => e.Model.Category == "Note");
    public int CardCount     => _allEntries.Count(e => e.Model.Category == "Card");
    public int IdentityCount => _allEntries.Count(e => e.Model.Category == "Identity");

    // ── Commands ───────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task LoadEntriesAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await _db.GetAllEntriesAsync();
            _allEntries.Clear();
            foreach (var e in entries)
                _allEntries.Add(new EntryViewModel(e, _crypto, _sessionKey));
            ApplyFilter();
            RefreshCounts();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedEntry is null) return;
        SelectedEntry.Model.IsFavorite = !SelectedEntry.Model.IsFavorite;
        await _db.SetFavoriteAsync(SelectedEntry.Model.Id, SelectedEntry.Model.IsFavorite);
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
    private void NavigateSection(string section) => SelectedSection = section;

    [RelayCommand]
    private void Lock() => LockRequested?.Invoke();

    // ── Filtering ──────────────────────────────────────────────────────────────
    private void ApplyFilter()
    {
        var q = _allEntries.AsEnumerable();

        q = SelectedSection switch
        {
            "favorites"  => q.Where(e => e.Model.IsFavorite),
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

    public MainViewModel(DatabaseService db, EncryptionService crypto, byte[] sessionKey)
    {
        _db         = db;
        _crypto     = crypto;
        _sessionKey = sessionKey;
    }
}
