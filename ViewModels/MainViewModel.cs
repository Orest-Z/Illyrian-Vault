/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyrianVault.Models;
using IllyrianVault.Services;

namespace IllyrianVault.ViewModels;

public enum BreachStatus { NotChecked, Checking, Safe, Breached, Error }

// ── Password history item VM ───────────────────────────────────────────────────
public partial class PasswordHistoryItemVm : ObservableObject
{
    private readonly string            _encryptedPassword;
    private readonly EncryptionService _crypto;
    private readonly byte[]            _key;

    public string DateLabel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRevealed))]
    [NotifyPropertyChangedFor(nameof(Plaintext))]
    private bool _isRevealed;

    public bool   IsNotRevealed => !IsRevealed;
    public string Plaintext     => IsRevealed && !string.IsNullOrEmpty(_encryptedPassword)
                                   ? _crypto.Decrypt(_encryptedPassword, _key)
                                   : "••••••••••••••••";

    [RelayCommand] private void Reveal() => IsRevealed = true;
    [RelayCommand] private void Hide()   => IsRevealed = false;

    public PasswordHistoryItemVm(string encryptedPassword, DateTime createdAt,
                                 EncryptionService crypto, byte[] key)
    {
        _encryptedPassword = encryptedPassword;
        _crypto            = crypto;
        _key               = key;
        DateLabel          = createdAt.ToLocalTime().ToString("MMM dd, yyyy  HH:mm");
    }
}

// ── Entry view model ───────────────────────────────────────────────────────────
public partial class EntryViewModel : ObservableObject
{
    private readonly EncryptionService _crypto;
    private readonly byte[]            _key;
    private readonly DatabaseService   _db;

    public PasswordEntry Model { get; }

    // Fired after a successful save so MainViewModel can show the "Saved ✓" toast.
    public event Action? SaveCompleted;

    // ── List display ──────────────────────────────────────────────────────────
    public string DisplayTitle    => string.IsNullOrEmpty(Model.Title) ? "(untitled)" : Model.Title;
    public string DisplayUsername => Model.Username;

    // ── Tile / header ─────────────────────────────────────────────────────────
    public string TileInitial      => string.IsNullOrEmpty(Model.Title) ? "?" : Model.Title[0].ToString().ToUpperInvariant();
    public string UpdatedFormatted => Model.UpdatedAt.ToString("MMM dd, yyyy");

    // ── Favorite ──────────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isFavorite;

    partial void OnIsFavoriteChanged(bool value)
    {
        Model.IsFavorite = value;
        OnPropertyChanged(nameof(IsFavoriteColor));
    }

    public System.Windows.Media.Brush IsFavoriteColor =>
        IsFavorite
            ? (System.Windows.Application.Current.TryFindResource("BrushWarning")
               as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.Goldenrod
            : (System.Windows.Application.Current.TryFindResource("BrushTextSecondary")
               as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.Gray;

    // ── Edit mode ─────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    [NotifyPropertyChangedFor(nameof(IsEditingLogin))]
    private bool _isEditing;

    public bool IsNotEditing   => !IsEditing;
    public bool IsEditingLogin => IsEditing && Model.Category == "Login";

    [ObservableProperty]
    private string _editTitle = string.Empty;

    [RelayCommand]
    private void Edit()
    {
        EditTitle       = Model.Title;
        _currentPayload = BuildPayload(forEditing: true);
        OnPropertyChanged(nameof(CurrentPayload));
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Model.Title     = EditTitle.Trim();
        Model.UpdatedAt = DateTime.UtcNow;

        // Flush payload fields back into the flat Model (and EncryptedPayload for
        // structured types like Card and Identity).
        switch (_currentPayload)
        {
            case LoginPayload lp:
                Model.Username = lp.Username;
                if (!string.IsNullOrEmpty(lp.Password))
                {
                    // Archive the current password in history BEFORE overwriting it.
                    // Only archive if there is an existing password to save and the
                    // plaintext actually changed (avoids duplicate history rows on
                    // saves where the user didn't touch the password field).
                    if (!string.IsNullOrEmpty(Model.EncryptedPassword))
                    {
                        var oldPlain = _crypto.Decrypt(Model.EncryptedPassword, _key);
                        if (oldPlain != lp.Password)
                            await _db.InsertPasswordHistoryAsync(Model.Id, Model.EncryptedPassword);
                    }

                    Model.EncryptedPassword = _crypto.Encrypt(lp.Password, _key);

                    // If the history panel was already loaded (open or previously viewed),
                    // refresh it so the new row appears immediately without requiring
                    // the user to close and re-open the panel.
                    if (ShowHistory || History.Count > 0)
                        await LoadHistoryAsync();
                }
                Model.Url   = lp.Url;
                Model.Notes = lp.Notes;
                break;

            case NotePayload np:
                Model.Notes = np.Content;
                break;

            case CardPayload cp:
                // Cardholder name doubles as the "username" shown in the list row.
                Model.Username         = cp.CardholderName;
                Model.EncryptedPayload = _crypto.Encrypt(
                    JsonSerializer.Serialize(cp), _key);
                break;

            case IdentityPayload ip:
                Model.Username         = ip.FullName;
                Model.EncryptedPayload = _crypto.Encrypt(
                    JsonSerializer.Serialize(ip), _key);
                break;
        }

        await _db.UpdateEntryAsync(Model);

        // Reload payload in view mode so the detail panel reflects saved values.
        _currentPayload = BuildPayload(forEditing: false);
        IsEditing       = false;

        // Notify list row and detail header to refresh.
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplayUsername));
        OnPropertyChanged(nameof(TileInitial));
        OnPropertyChanged(nameof(UpdatedFormatted));
        OnPropertyChanged(nameof(CurrentPayload));

        SaveCompleted?.Invoke();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _currentPayload = BuildPayload(forEditing: false);
        IsEditing       = false;
        OnPropertyChanged(nameof(CurrentPayload));
    }

    // ── Payload ───────────────────────────────────────────────────────────────
    private IEntryPayload? _currentPayload;
    public  IEntryPayload? CurrentPayload => _currentPayload ??= BuildPayload(forEditing: false);

    private IEntryPayload BuildPayload(bool forEditing) => Model.Category switch
    {
        "Note"     => new NotePayload     { Content = Model.Notes },
        "Card"     => DeserializePayload<CardPayload>()     ?? new CardPayload(),
        "Identity" => DeserializePayload<IdentityPayload>() ?? new IdentityPayload(),
        _          => new LoginPayload
        {
            Username = Model.Username,
            Password = forEditing && !string.IsNullOrEmpty(Model.EncryptedPassword)
                       ? _crypto.Decrypt(Model.EncryptedPassword, _key)
                       : string.Empty,
            Url      = Model.Url,
            Notes    = Model.Notes,
        },
    };

    // Decrypt and JSON-deserialize Model.EncryptedPayload into T.
    // Returns null if the field is empty or decryption/deserialization fails.
    private T? DeserializePayload<T>() where T : class
    {
        if (string.IsNullOrEmpty(Model.EncryptedPayload)) return null;
        try
        {
            var json = _crypto.Decrypt(Model.EncryptedPayload, _key);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch { return null; }
    }

    // ── CVV reveal (card entries only) ────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotCvvRevealed))]
    private bool _isCvvRevealed;

    public bool IsNotCvvRevealed => !IsCvvRevealed;

    [RelayCommand] private void ToggleCvvReveal() => IsCvvRevealed = !IsCvvRevealed;

    // ── Password reveal (login entries only) ──────────────────────────────────
    [ObservableProperty]
    private bool _isPasswordRevealed;

    partial void OnIsPasswordRevealedChanged(bool value) =>
        OnPropertyChanged(nameof(DisplayPassword));

    public string DisplayPassword =>
        IsPasswordRevealed && !string.IsNullOrEmpty(Model.EncryptedPassword)
            ? _crypto.Decrypt(Model.EncryptedPassword, _key)
            : "••••••••••••••••";

    // ── Password history ──────────────────────────────────────────────────────
    public ObservableCollection<PasswordHistoryItemVm> History { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHistoryEmpty))]
    private bool _showHistory;

    public bool ShowHistoryEmpty => ShowHistory && History.Count == 0;

    [RelayCommand]
    private async Task ToggleHistoryAsync()
    {
        if (!ShowHistory && History.Count == 0)
            await LoadHistoryAsync();
        ShowHistory = !ShowHistory;
    }

    private async Task LoadHistoryAsync()
    {
        var records = await _db.GetPasswordHistoryAsync(Model.Id);
        History.Clear();
        foreach (var r in records)
            History.Add(new PasswordHistoryItemVm(r.EncryptedPassword, r.CreatedAt, _crypto, _key));
    }

    // ── Per-entry password generator ──────────────────────────────────────────
    public PasswordGeneratorViewModel Generator { get; }

    // ── Breach check ──────────────────────────────────────────────────────────
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

    // ── Constructor ───────────────────────────────────────────────────────────
    public EntryViewModel(PasswordEntry model, EncryptionService crypto, byte[] key, DatabaseService db)
    {
        Model       = model;
        _crypto     = crypto;
        _key        = key;
        _db         = db;
        _isFavorite = model.IsFavorite;

        Generator = new PasswordGeneratorViewModel();
        Generator.PasswordAccepted += pw =>
        {
            if (CurrentPayload is LoginPayload lp)
                lp.Password = pw;
        };
    }
}

// ── Main view model ────────────────────────────────────────────────────────────
public partial class MainViewModel : BaseViewModel
{
    private readonly DatabaseService   _db;
    private readonly EncryptionService _crypto;
    private readonly byte[]            _sessionKey;

    public event Action?           LockRequested;
    public event Action?           NewEntryRequested;
    public event Action?           ExportRequested;
    public event Action<TimeSpan>? IdleTimeoutChanged;
    public event Func<bool>?       ConfirmDeleteRequested;
    public event Action<string>?   ToastRequested;

    // ── Profile ────────────────────────────────────────────────────────────────
    public string ProfileUsername { get; }
    public string ProfileInitial  => string.IsNullOrEmpty(ProfileUsername) ? "?" : ProfileUsername[0].ToString().ToUpperInvariant();

    public IEnumerable<EntryViewModel> AllEntries => _allEntries;

    // ── Standalone generator (Generator section) ──────────────────────────────
    public PasswordGeneratorViewModel Generator { get; } = new();
    public string AppVersion => System.Reflection.Assembly
        .GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    // ── Idle auto-lock ─────────────────────────────────────────────────────────
    public IReadOnlyList<int> IdleTimeoutOptions { get; } = new[] { 1, 5, 10, 15, 30 };

    [ObservableProperty]
    private int _idleTimeoutMinutes = 5;

    partial void OnIdleTimeoutMinutesChanged(int value) =>
        IdleTimeoutChanged?.Invoke(TimeSpan.FromMinutes(value));

    [RelayCommand]
    private void RequestExport() => ExportRequested?.Invoke();

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

    partial void OnCurrentLanguageChanged(string value)
    {
        App.Localization.Apply(value == "SQ" ? AppLanguage.Sq : AppLanguage.En);
        OnPropertyChanged(nameof(SortOptions));
    }

    // ── Entry collections ──────────────────────────────────────────────────────
    private readonly ObservableCollection<EntryViewModel> _allEntries = [];
    public  ObservableCollection<EntryViewModel> FilteredEntries { get; } = [];

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

    // ── Search / filter ────────────────────────────────────────────────────────
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

    // ── Sort ───────────────────────────────────────────────────────────────────
    public IReadOnlyList<string> SortOptions =>
    [
        App.Localization["SortRecentlyUsed"],
        App.Localization["SortAlphaAZ"],
        App.Localization["SortAlphaZA"],
        App.Localization["SortDateCreated"],
    ];

    [ObservableProperty]
    private int _selectedSortIndex = 0;

    partial void OnSelectedSortIndexChanged(int value) => ApplyFilter();

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
                _allEntries.Add(CreateEntryVm(e));
            ApplyFilter();
            RefreshCounts();
        }
        finally { IsBusy = false; }
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
        if (ConfirmDeleteRequested is not null && ConfirmDeleteRequested.Invoke() != true) return;
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
        ClipboardGuard.SetAndScheduleWipe(SelectedEntry.Model.Username);
        ShowCopiedToast();
    }

    [RelayCommand]
    private void CopyPassword()
    {
        if (SelectedEntry is null || string.IsNullOrEmpty(SelectedEntry.Model.EncryptedPassword)) return;
        var plaintext = _crypto.Decrypt(SelectedEntry.Model.EncryptedPassword, _sessionKey);
        ClipboardGuard.SetAndScheduleWipe(plaintext);
        ShowCopiedToast();
    }

    // Copies the card number for the currently selected Card entry.
    [RelayCommand]
    private void CopyCardNumber()
    {
        if (SelectedEntry?.CurrentPayload is not CardPayload cp) return;
        ClipboardGuard.SetAndScheduleWipe(cp.CardNumber);
        ShowCopiedToast();
    }

    // Copies the CVV for the currently selected Card entry.
    [RelayCommand]
    private void CopyCvv()
    {
        if (SelectedEntry?.CurrentPayload is not CardPayload cp) return;
        ClipboardGuard.SetAndScheduleWipe(cp.Cvv);
        ShowCopiedToast();
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
        _allEntries.Add(CreateEntryVm(e));
        ApplyFilter();
        RefreshCounts();
        SelectedEntry = FilteredEntries.FirstOrDefault(x => x.Model.Id == e.Id);
    }

    [RelayCommand]
    private async Task LockAsync()
    {
        App.ClearSessionKey();
        ClipboardGuard.ClearNow();
        await App.Database.DisposeAsync();
        LockRequested?.Invoke();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        App.ClearSessionKey();
        ClipboardGuard.ClearNow();
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

        q = SelectedSortIndex switch
        {
            1 => q.OrderBy(e => e.Model.Title, StringComparer.OrdinalIgnoreCase),
            2 => q.OrderByDescending(e => e.Model.Title, StringComparer.OrdinalIgnoreCase),
            3 => q.OrderByDescending(e => e.Model.CreatedAt),
            _ => q.OrderByDescending(e => e.Model.UpdatedAt),
        };

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

    private void ShowCopiedToast() =>
        ToastRequested?.Invoke(App.Localization["CopiedToClipboard"]);

    private void ShowSavedToast() =>
        ToastRequested?.Invoke(App.Localization["EntrySaved"]);

    // Factory method: creates an EntryViewModel and subscribes to its save event
    // so the window can show the "Saved ✓" toast without coupling the VM to the View.
    private EntryViewModel CreateEntryVm(PasswordEntry e)
    {
        var evm = new EntryViewModel(e, _crypto, _sessionKey, _db);
        evm.SaveCompleted += ShowSavedToast;
        return evm;
    }

    public MainViewModel(DatabaseService db, EncryptionService crypto, byte[] sessionKey, string username)
    {
        _db             = db;
        _crypto         = crypto;
        _sessionKey     = sessionKey;
        ProfileUsername = username;
        Generator.ClipboardWritten += ShowCopiedToast;
    }
}
