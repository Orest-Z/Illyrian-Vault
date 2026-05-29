using System.IO;
using System.Runtime.InteropServices;
using System.Security;
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
                RecordFailure();
                ErrorMessage = "No vault found for that username.";
                return;
            }

            var metaPath = DatabaseService.GetMetaPath(Username);
            if (!File.Exists(metaPath))
            {
                RecordFailure();
                ErrorMessage = "Vault data is missing or corrupted.";
                return;
            }

            string[] lines = await File.ReadAllLinesAsync(metaPath);
            if (lines.Length < 2)
            {
                RecordFailure();
                ErrorMessage = "Vault data is missing or corrupted.";
                return;
            }

            byte[] salt      = Convert.FromBase64String(lines[0]);
            byte[] storedHash = Convert.FromBase64String(lines[1]);

            // ── Extract password bytes from SecureString (never a System.String) ──
            if (_securePassword is null || _securePassword.Length == 0)
            {
                ErrorMessage = "Please enter your master password.";
                return;
            }

            using PinnedBuffer pwdBuffer = SecureMemory.PinFromSecureString(_securePassword);

            // ── Verify password BEFORE deriving the DB key ─────────────────────
            // This reads the vault.meta sidecar (no DB I/O) and is fast enough
            // (~400 ms for 600 k iterations) to serve as the primary gate.
            if (!_crypto.VerifyPassword(pwdBuffer.RoSpan, salt, storedHash))
            {
                RecordFailure();
                ErrorMessage = BuildFailureMessage();
                return;
            }

            // ── Derive the 32-byte SQLCipher key ──────────────────────────────
            using PinnedBuffer keyBuffer = SecureMemory.DeriveKey(pwdBuffer.RoSpan, salt);
            // pwdBuffer is still alive here — Dispose() is deferred to the
            // end of the using block, so RoSpan remains valid.

            _db.SetProfile(Username);
            bool opened = await _db.TryOpenAsync(keyBuffer.Span.ToArray());
            // ToArray() creates a temporary byte[] copy for the async call.
            // keyBuffer.Dispose() zeros the PinnedBuffer's bytes at end of using.

            if (!opened)
            {
                RecordFailure();
                ErrorMessage = "Could not unlock the vault. The database may be corrupted.";
                return;
            }

            // ── Commit key to session store ────────────────────────────────────
            // AllocateSessionKey copies into a NEW pinned allocation so keyBuffer
            // can be zeroed independently when its using block exits.
            var (sessionKey, _) = SecureMemory.AllocateSessionKey(keyBuffer);
            App.SetSessionKey(sessionKey);

            // ── Success: zero failure counter, clear secure password ───────────
            _consecutiveFailures = 0;
            _lockedUntil         = DateTime.MinValue;

            // Dispose and null out the SecureString — its kernel buffer is zeroed.
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

    private void RecordFailure()
    {
        int newCount = Interlocked.Increment(ref _consecutiveFailures);
        if (newCount >= HardLockoutThreshold)
            _lockedUntil = DateTime.UtcNow.AddSeconds(HardLockoutSeconds);
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
    private void Exit() => System.Windows.Application.Current.Shutdown();
}
