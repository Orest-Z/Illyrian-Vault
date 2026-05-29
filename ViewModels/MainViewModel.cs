/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyrianVault.Models;
using IllyrianVault.Services;

namespace IllyrianVault.ViewModels;

public enum BreachStatus  { NotChecked, Checking, Safe, Breached, Error }
public enum WorkspaceState { Vault, Settings, Generator }

public partial class PasswordHistoryItemVm : ObservableObject
{
    private readonly string            _encryptedPassword;
    private readonly EncryptionService _crypto;
    private readonly byte[]            _key;

    public string DateLabel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRevealed))]
    private bool _isRevealed;

    public bool IsNotRevealed => !IsRevealed;

    [ObservableProperty]
    private string _plaintext = "••••••••";

    public PasswordHistoryItemVm(string encryptedPassword, DateTime createdAt, EncryptionService crypto, byte[] key)
    {
        _encryptedPassword = encryptedPassword;
        _crypto            = crypto;
        _key               = key;
        DateLabel          = createdAt.ToLocalTime().ToString("MMM dd, yyyy  HH:mm");
    }

    [RelayCommand]
    private Task RevealAsync()
    {
        Plaintext  = _crypto.Decrypt(_encryptedPassword, _key);
        IsRevealed = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Hide()
    {
        Plaintext  = "••••••••";
        IsRevealed = false;
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
        if (CurrentPayload is not LoginPayload lp || string.IsNullOrEmpty(lp.Password)) return;

        BreachStatus  = BreachStatus.Checking;
        BreachMessage = string.Empty;

        try
        {
            var result    = await BreachCheckService.CheckPasswordAsync(lp.Password);
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
    [NotifyPropertyChangedFor(nameof(IsEditingLogin))]
    private bool _isEditing;

    public bool IsNotEditing   => !IsEditing;
    public bool IsEditingLogin => IsEditing && CurrentPayload is LoginPayload;

    [ObservableProperty] private string _editTitle = string.Empty;

    private CancellationTokenSource? _revealCts;

    // JSON snapshot of CurrentPayload captured on Edit() — used to revert on Cancel.
    private string _payloadCache = string.Empty;

    // ── Payload ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPassword))]
    [NotifyPropertyChangedFor(nameof(DisplayUsername))]
    [NotifyPropertyChangedFor(nameof(IsEditingLogin))]
    private IEntryPayload _currentPayload = new LoginPayload();

    public PasswordGeneratorViewModel Generator { get; } = new();

    [RelayCommand]
    private void Edit()
    {
        EditTitle    = Model.Title;
        _payloadCache = JsonSerializer.Serialize(CurrentPayload, CurrentPayload.GetType());
        IsEditing    = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Archive old password and sync flat fields for Login entries.
        if (CurrentPayload is LoginPayload lp)
        {
            var newEncrypted = string.IsNullOrEmpty(lp.Password)
                ? string.Empty
                : _crypto.Encrypt(lp.Password, _key);

            if (!string.IsNullOrEmpty(Model.EncryptedPassword) && Model.EncryptedPassword != newEncrypted)
                await _db.InsertPasswordHistoryAsync(Model.Id, Model.EncryptedPassword);

            Model.EncryptedPassword = newEncrypted;
            Model.Username          = lp.Username;
            Model.Url               = lp.Url;
            Model.Notes             = lp.Notes;
        }
        else
        {
            Model.EncryptedPassword = string.Empty;
            Model.Username          = string.Empty;
            Model.Url               = string.Empty;
            Model.Notes             = string.Empty;
        }

        Model.Title          = EditTitle;
        var json             = JsonSerializer.Serialize(CurrentPayload, CurrentPayload.GetType());
        Model.EncryptedPayload = _crypto.Encrypt(json, _key);

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
    private void CancelEdit()
    {
        if (!string.IsNullOrEmpty(_payloadCache))
            CurrentPayload = DeserializePayload(_payloadCache, Model.Category);
        EditTitle = Model.Title;
        IsEditing = false;
    }

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

    partial void OnIsPasswordRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayPassword));
        _revealCts?.Cancel();
        _revealCts?.Dispose();
        _revealCts = null;
        if (!value) return;
        var cts = new CancellationTokenSource();
        _revealCts = cts;
        _ = Task.Delay(TimeSpan.FromSeconds(30), cts.Token).ContinueWith(_ =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => IsPasswordRevealed = false);
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

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

    public string DisplayTitle => Model.Title;

    public string DisplayUsername => CurrentPayload switch
    {
        LoginPayload    lp => lp.Username,
        CardPayload     cp => cp.CardholderName,
        IdentityPayload ip => ip.FullName,
        _                  => string.Empty,
    };

    public string TileInitial      => string.IsNullOrEmpty(Model.Title) ? "?" : Model.Title[0].ToString().ToUpperInvariant();
    public string UpdatedFormatted => Model.UpdatedAt.ToString("MMM dd, yyyy");

    public string DisplayPassword
    {
        get
        {
            if (CurrentPayload is not LoginPayload lp || string.IsNullOrEmpty(lp.Password))
                return string.Empty;
            return IsPasswordRevealed ? lp.Password : "••••••••••••••••";
        }
    }

    // ── Payload helpers ────────────────────────────────────────────────────────

    private IEntryPayload LoadPayload()
    {
        if (!string.IsNullOrEmpty(Model.EncryptedPayload))
        {
            var json = _crypto.Decrypt(Model.EncryptedPayload, _key);
            return DeserializePayload(json, Model.Category);
        }
        // Lazy migration: build payload from legacy flat columns.
        return Model.Category switch
        {
            "Note"     => new NotePayload(),
            "Card"     => new CardPayload(),
            "Identity" => new IdentityPayload(),
            _          => new LoginPayload
            {
                Username = Model.Username,
                Password = string.IsNullOrEmpty(Model.EncryptedPassword)
                    ? string.Empty
                    : _crypto.Decrypt(Model.EncryptedPassword, _key),
                Url  = Model.Url,
                Notes = Model.Notes,
            },
        };
    }

    private static IEntryPayload DeserializePayload(string json, string category) =>
        category switch
        {
            "Note"     => JsonSerializer.Deserialize<NotePayload>(json)     ?? new NotePayload(),
            "Card"     => JsonSerializer.Deserialize<CardPayload>(json)     ?? new CardPayload(),
            "Identity" => JsonSerializer.Deserialize<IdentityPayload>(json) ?? new IdentityPayload(),
            _          => JsonSerializer.Deserialize<LoginPayload>(json)    ?? new LoginPayload(),
        };

    public EntryViewModel(PasswordEntry model, EncryptionService crypto, byte[] key, DatabaseService db)
    {
        Model           = model;
        _crypto         = crypto;
        _key            = key;
        _db             = db;
        _isFavorite     = model.IsFavorite;
        _currentPayload = LoadPayload();
        Generator.PasswordAccepted += p => { if (CurrentPayload is LoginPayload lp) lp.Password = p; };
    }
}

public partial class MainViewModel : BaseViewModel
{
    private readonly DatabaseService   _db;
    private readonly EncryptionService _crypto;
    private readonly byte[]            _sessionKey;

    public event Action?      LockRequested;
    public event Action?      NewEntryRequested;
    public event Action?      ExportRequested;
    public event Action<TimeSpan>? IdleTimeoutChanged;
    public event Func<bool>?  ConfirmDeleteRequested;

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

    // ── Idle auto-lock ─────────────────────────────────────────────────────────
    public IReadOnlyList<int> IdleTimeoutOptions { get; } = new[] { 1, 2, 5, 10, 15, 30 };

    [ObservableProperty]
    private int _idleTimeoutMinutes = 5;

    partial void OnIdleTimeoutMinutesChanged(int value) =>
        IdleTimeoutChanged?.Invoke(TimeSpan.FromMinutes(value));

    // ── Entry collections ──────────────────────────────────────────────────────
    private readonly ObservableCollection<EntryViewModel> _allEntries = [];

    public IReadOnlyList<EntryViewModel> AllEntries => _allEntries;

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

    // ── Sort options ───────────────────────────────────────────────────────────
    // Exposed as a plain instance property so XAML can bind with {Binding SortOptions}.
    // The list is readonly; only the selected item is observable.
    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "Recently used",
        "Alphabetical (A-Z)",
        "Alphabetical (Z-A)",
        "Date created",
    };

    [ObservableProperty]
    private string _selectedSortOption = "Recently used";

    partial void OnSelectedSortOptionChanged(string value) => ApplyFilter();

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

    partial void OnSelectedSectionChanged(string value)
    {
        CurrentWorkspace = value switch
        {
            "settings"  => WorkspaceState.Settings,
            "generator" => WorkspaceState.Generator,
            _           => WorkspaceState.Vault,
        };
        ApplyFilter();
    }

    [ObservableProperty]
    private WorkspaceState _currentWorkspace = WorkspaceState.Vault;

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
    private void RequestExport() => ExportRequested?.Invoke();

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
        if (ConfirmDeleteRequested?.Invoke() != true) return;
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
        var text = SelectedEntry.CurrentPayload switch
        {
            LoginPayload    lp => lp.Username,
            CardPayload     cp => cp.CardholderName,
            IdentityPayload ip => ip.FullName,
            _                  => string.Empty,
        };
        if (!string.IsNullOrEmpty(text))
            ClipboardGuard.SetAndScheduleWipe(text);
    }

    [RelayCommand]
    private void CopyPassword()
    {
        if (SelectedEntry?.CurrentPayload is not LoginPayload lp) return;
        if (!string.IsNullOrEmpty(lp.Password))
            ClipboardGuard.SetAndScheduleWipe(lp.Password);
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
        if (SelectedEntry is not null)
        {
            SelectedEntry.History.Clear();
            SelectedEntry.ShowHistory = false;
        }
        App.ClearSessionKey();
        ClipboardGuard.ClearNow();
        await App.Database.DisposeAsync();
        LockRequested?.Invoke();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (SelectedEntry is not null)
        {
            SelectedEntry.History.Clear();
            SelectedEntry.ShowHistory = false;
        }
        App.ClearSessionKey();
        ClipboardGuard.ClearNow();
        await App.Database.DisposeAsync();
        LockRequested?.Invoke();
    }

    // ── Filtering & Sorting ────────────────────────────────────────────────────
    private void ApplyFilter()
    {
        var q = _allEntries.AsEnumerable();

        // 1. Category / section filter
        q = SelectedSection switch
        {
            "favorites"  => q.Where(e => e.IsFavorite),
            "logins"     => q.Where(e => e.Model.Category == "Login"),
            "notes"      => q.Where(e => e.Model.Category == "Note"),
            "cards"      => q.Where(e => e.Model.Category == "Card"),
            "identities" => q.Where(e => e.Model.Category == "Identity"),
            _            => q,
        };

        // 2. Search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var f = SearchQuery.Trim();
            q = q.Where(e =>
                e.Model.Title.Contains(f, StringComparison.OrdinalIgnoreCase)     ||
                e.DisplayUsername.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.Model.Url.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        // 3. Sort — applied after filtering so only visible items are ordered
        q = SelectedSortOption switch
        {
            "Alphabetical (A-Z)" => q.OrderBy(e => e.Model.Title,
                                               StringComparer.OrdinalIgnoreCase),
            "Alphabetical (Z-A)" => q.OrderByDescending(e => e.Model.Title,
                                                         StringComparer.OrdinalIgnoreCase),
            "Date created"       => q.OrderByDescending(e => e.Model.CreatedAt),
            _                    => q.OrderByDescending(e => e.Model.UpdatedAt), // "Recently used"
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

    public MainViewModel(DatabaseService db, EncryptionService crypto, byte[] sessionKey, string username)
    {
        _db         = db;
        _crypto     = crypto;
        _sessionKey = sessionKey;
        ProfileUsername = username;
    }
}
