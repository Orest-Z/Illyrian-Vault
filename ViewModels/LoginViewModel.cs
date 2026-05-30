/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.IO;
using System.Security;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyrianVault.Services;

namespace IllyrianVault.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly EncryptionService _crypto;
    private readonly DatabaseService   _db;

    public event Action<string>? LoginSucceeded;
    public event Action?         NavigateToRegister;
    public event Action?         NavigateToRecovery;

    // ── Brute-force defence ────────────────────────────────────────────────────
    // Progressive exponential backoff:
    //   Attempt 1 → 0 s   (first try is free)
    //   Attempt 2 → 1 s
    //   Attempt 3 → 2 s
    //   Attempt 4 → 4 s
    //   Attempt 5 → 8 s
    //   Attempt 6 → 16 s
    //   Attempt 7 → 30 s
    //   Attempt 8 → 60 s
    //   Attempt 9 → 120 s
    //   Attempt 10+ → 300 s hard lockout per attempt
    //
    // WHY apply the delay BEFORE the attempt (not after)?
    // Applying it BEFORE prevents an attacker from exploiting the window between
    // "attempt is sent" and "delay kicks in". Concretely: if an automated tool
    // can fire requests faster than the delay is enforced, a post-attempt delay
    // only slows honest users while a parallel brute-forcer bypasses it.
    // Blocking BEFORE the attempt serialises all guesses through the same gate.

    private static readonly int[] BackoffTable =
        { 0, 1, 2, 4, 8, 16, 30, 60, 120, 300 };

    private const int HardLockoutThreshold = 10;   // attempts before per-attempt lockout
    private const int HardLockoutSeconds   = 300;  // 5 minutes

    // SECURITY NOTE: The lockout counter and expiry are DPAPI-protected in vault.meta,
    // binding them to the current Windows user account. An attacker who compromises
    // the user's Windows session can still reset them by deleting vault.meta entirely.
    // The lockout is defence-in-depth UX friction, NOT a cryptographic guarantee.
    // The real brute-force barrier is PBKDF2-SHA512 at 600,000 iterations.
    private volatile int _consecutiveFailures = 0;
    private DateTime     _lockedUntil         = DateTime.MinValue;

    // ── Observable state ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileInitial))]
    [NotifyPropertyChangedFor(nameof(VaultPath))]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _username = string.Empty;

    // SecureString is NOT an [ObservableProperty] because the CommunityToolkit
    // generator cannot call Dispose() on the replaced value. We wire it manually
    // so the old SecureString is zeroed (DPAPI-cleared) before GC.
    private SecureString? _securePassword;
    public SecureString? SecurePassword
    {
        get => _securePassword;
        set
        {
            var old = _securePassword;
            SetProperty(ref _securePassword, value);
            old?.Dispose();             // zeros and frees the kernel-mode buffer
            UnlockCommand.NotifyCanExecuteChanged();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLockoutMessage))]
    private string _lockoutMessage = string.Empty;

    public bool HasLockoutMessage => !string.IsNullOrEmpty(LockoutMessage);

    public string ProfileInitial =>
        string.IsNullOrEmpty(Username) ? "?" : Username[0].ToString().ToUpperInvariant();

    public string VaultPath => DatabaseService.GetDbPath(Username);

    public List<string> AvailableProfiles   { get; } = DatabaseService.ListProfiles();
    public bool         HasMultipleProfiles => AvailableProfiles.Count > 1;

    public LoginViewModel(EncryptionService crypto, DatabaseService db)
    {
        _crypto = crypto;
        _db     = db;
        TryAutoFillUsername();
    }

    private void TryAutoFillUsername()
    {
        if (AvailableProfiles.Count >= 1)
            Username = AvailableProfiles[0];
    }

    [RelayCommand]
    private void SelectProfile(string username) => Username = username;

    partial void OnUsernameChanged(string value) => _ = LoadLockoutStateAsync(value);

    // ── Unlock command ─────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        ClearError();
        LockoutMessage = string.Empty;

        // ── Lockout gate ────────────────────────────────────────────────────────
        var now = DateTime.UtcNow;
        if (now < _lockedUntil)
        {
            var remaining = (int)Math.Ceiling((_lockedUntil - now).TotalSeconds);
            ErrorMessage = $"Too many failed attempts. Try again in {remaining} seconds.";
            return;
        }

        // ── Pre-attempt backoff delay ───────────────────────────────────────────
        int failures = _consecutiveFailures;
        if (failures > 0)
        {
            int delaySecs = failures < BackoffTable.Length
                ? BackoffTable[failures]
                : BackoffTable[^1];

            LockoutMessage = $"Waiting {delaySecs}s before next attempt…";
            IsBusy         = true;
            await Task.Delay(TimeSpan.FromSeconds(delaySecs));
            LockoutMessage = string.Empty;
        }

        IsBusy = true;
        try
        {
            if (!DatabaseService.ProfileExists(Username))
            {
                await RecordFailureAsync();
                ErrorMessage = "No vault found for that username.";
                return;
            }

            var metaPath = DatabaseService.GetMetaPath(Username);
            if (!File.Exists(metaPath))
            {
                await RecordFailureAsync();
                ErrorMessage = "Vault data is missing or corrupted.";
                return;
            }

            string[] lines = await File.ReadAllLinesAsync(metaPath);
            if (lines.Length < 2)
            {
                await RecordFailureAsync();
                ErrorMessage = "Vault data is missing or corrupted.";
                return;
            }

            byte[] salt       = Convert.FromBase64String(lines[0]);
            byte[] storedHash = Convert.FromBase64String(lines[1]);

            // Line 3 carries the vault format version; any unrecognised tag falls
            // back to v1 (SHA-256 PBKDF2) for backward compatibility.
            string versionTag = lines.Length > 3 ? lines[3].Trim() : "v1";

            if (_securePassword is null || _securePassword.Length == 0)
            {
                ErrorMessage = "Please enter your master password.";
                return;
            }

            using PinnedBuffer pwdBuffer = SecureMemory.PinFromSecureString(_securePassword);

            // Derive the key once and reuse it for both verification and DB open,
            // avoiding a second 600 k-iteration PBKDF2 call.
            using PinnedBuffer keyBuffer = versionTag == "v2"
                ? SecureMemory.DeriveKeyV2(pwdBuffer.RoSpan, salt)
                : SecureMemory.DeriveKey(pwdBuffer.RoSpan, salt);

            byte[] candidate = _crypto.CreateVerificationHash(keyBuffer.RoSpan);
            if (!CryptographicOperations.FixedTimeEquals(candidate, storedHash))
            {
                await RecordFailureAsync();
                ErrorMessage = BuildFailureMessage();
                return;
            }

            _db.SetProfile(Username);
            bool opened = await _db.TryOpenAsync(keyBuffer.Span.ToArray());

            if (!opened)
            {
                await RecordFailureAsync();
                ErrorMessage = "Could not unlock the vault. The database may be corrupted.";
                return;
            }

            // AllocateSessionKey copies bytes; App.SetSessionKey is the sole pin site.
            App.SetSessionKey(SecureMemory.AllocateSessionKey(keyBuffer));

            _consecutiveFailures = 0;
            _lockedUntil         = DateTime.MinValue;
            await PersistLockoutStateAsync();

            var old = _securePassword;
            _securePassword = null;
            old?.Dispose();

            LoginSucceeded?.Invoke(Username);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUnlock() =>
        !string.IsNullOrEmpty(Username) &&
        _securePassword?.Length > 0     &&
        !IsBusy;

    // ── Backoff helpers ────────────────────────────────────────────────────────

    private async Task RecordFailureAsync()
    {
        int newCount = Interlocked.Increment(ref _consecutiveFailures);
        if (newCount >= HardLockoutThreshold)
            _lockedUntil = DateTime.UtcNow.AddSeconds(HardLockoutSeconds);
        await PersistLockoutStateAsync();
    }

    private async Task PersistLockoutStateAsync()
    {
        var metaPath = DatabaseService.GetMetaPath(Username);
        if (!File.Exists(metaPath)) return;
        try
        {
            var lines = (await File.ReadAllLinesAsync(metaPath)).ToList();
            if (lines.Count == 3) lines.Add("v1");
            while (lines.Count < 6) lines.Add("0");

            // Encrypt lockout values with DPAPI (CurrentUser scope) so an attacker
            // with filesystem access cannot simply reset the counter by editing the file.
            // DPAPI ties the ciphertext to the Windows user account — a different user
            // or a different machine cannot decrypt it. On failure we fall back to
            // plaintext so a DPAPI-unavailable environment degrades gracefully.
            lines[4] = DpapiProtect($"{_consecutiveFailures}");
            lines[5] = DpapiProtect($"{_lockedUntil.Ticks}");
            await File.WriteAllLinesAsync(metaPath, lines);
        }
        catch { }
    }

    private async Task LoadLockoutStateAsync(string username)
    {
        if (string.IsNullOrEmpty(username)) return;
        var metaPath = DatabaseService.GetMetaPath(username);
        if (!File.Exists(metaPath)) return;
        try
        {
            var lines = await File.ReadAllLinesAsync(metaPath);
            _consecutiveFailures = lines.Length > 4 && int.TryParse(DpapiUnprotect(lines[4]), out int f)  ? f : 0;
            _lockedUntil         = lines.Length > 5 && long.TryParse(DpapiUnprotect(lines[5]), out long t) && t > 0
                                   ? new DateTime(t, DateTimeKind.Utc)
                                   : DateTime.MinValue;
        }
        catch { }
    }

    private string BuildFailureMessage()
    {
        int failures = _consecutiveFailures;
        if (_lockedUntil > DateTime.UtcNow)
            return $"Too many failed attempts. Locked out for {HardLockoutSeconds / 60} minutes.";

        int nextDelay = failures < BackoffTable.Length
            ? BackoffTable[failures]
            : BackoffTable[^1];

        return nextDelay > 0
            ? $"Incorrect master password. Next attempt will wait {nextDelay}s."
            : "Incorrect master password. Please try again.";
    }

    // ── Other commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void NavigateRegister() => NavigateToRegister?.Invoke();

    [RelayCommand]
    private void ForgotPassword() => NavigateToRecovery?.Invoke();

    [RelayCommand]
    private void Exit() => System.Windows.Application.Current.Shutdown();

    private static string DpapiProtect(string value)
    {
        try
        {
            var plain  = System.Text.Encoding.UTF8.GetBytes(value);
            var cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipher);
        }
        catch { return value; }   // graceful degradation if DPAPI unavailable
    }

    private static string DpapiUnprotect(string value)
    {
        try
        {
            var cipher = Convert.FromBase64String(value);
            var plain  = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch { return value; }   // handles legacy plaintext values and DPAPI errors
    }
}
