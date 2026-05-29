using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace IllyrianVault.Services;

/// <summary>
/// Writes sensitive text to the Windows clipboard and automatically wipes it
/// after a configurable delay.
///
/// THREAT MODEL: other processes (or malware) polling the global clipboard
/// can read any text placed there. A 15-second auto-clear window significantly
/// reduces the exposure period compared to leaving credentials indefinitely.
///
/// OWNERSHIP VERIFICATION: instead of relying on the Win32 GetClipboardOwner
/// HWND (unreliable through WPF's DataObject wrapper), we store a SHA-256 hash
/// of the text we placed. Before wiping, we hash the current clipboard content
/// and compare. If the user manually copied something different, the hashes
/// differ and we do NOT wipe — preventing accidental data loss.
///
/// CANCELLATION: every new copy cancels the previous wipe task via a
/// CancellationTokenSource, so rapidly copying multiple items correctly
/// schedules only one final wipe — for the most-recently copied value.
///
/// THREADING: SetAndScheduleWipe MUST be called on the WPF UI (STA) thread
/// because Clipboard.SetText has an STA requirement.  The wipe dispatch back
/// to the UI thread is handled internally via Application.Current.Dispatcher.
/// </summary>
public static class ClipboardGuard
{
    private static readonly object  _lock = new();
    private static CancellationTokenSource? _wipeCts;
    private static byte[]?          _ownerHash;   // SHA-256 of the text we placed

    public const int DefaultWipeSeconds = 15;

    /// <summary>
    /// Places <paramref name="content"/> on the Windows clipboard and schedules
    /// an automatic wipe after <paramref name="wipeAfterSeconds"/> seconds.
    ///
    /// Calling this a second time cancels the first wipe task: the clock resets.
    /// </summary>
    public static void SetAndScheduleWipe(string content, int wipeAfterSeconds = DefaultWipeSeconds)
    {
        // Compute ownership hash BEFORE touching the clipboard so a concurrent
        // clipboard poll between SetText and hash storage cannot cause a missed wipe.
        byte[] hash = ComputeHash(content);

        CancellationTokenSource newCts = new();
        CancellationTokenSource? oldCts;

        lock (_lock)
        {
            oldCts     = _wipeCts;
            _wipeCts   = newCts;
            _ownerHash = hash;

            // Clipboard.SetText must happen inside the lock so _ownerHash is
            // consistent with what is actually in the clipboard.
            Clipboard.SetText(content);
        }

        // Cancel the previous wipe task AFTER the lock is released to minimise
        // contention. The old task will catch OperationCanceledException.
        oldCts?.Cancel();
        oldCts?.Dispose();

        // Fire-and-forget. Exceptions are swallowed inside the task because a
        // failed wipe (e.g., clipboard access denied by another app) must never
        // crash the UI thread.
        _ = ScheduleWipeAsync(hash, wipeAfterSeconds, newCts.Token);
    }

    /// <summary>
    /// Immediately clears the clipboard if it still contains our content,
    /// and cancels any pending scheduled wipe.
    /// Call this on app Lock, Logout, or window close.
    /// </summary>
    public static void ClearNow()
    {
        CancellationTokenSource? cts;
        byte[]? hash;

        lock (_lock)
        {
            cts        = _wipeCts;
            hash       = _ownerHash;
            _wipeCts   = null;
            _ownerHash = null;
        }

        cts?.Cancel();
        cts?.Dispose();

        if (hash is null) return;

        // We're already on the UI thread when Lock/Logout fire (RelayCommand).
        if (!Clipboard.ContainsText()) return;
        if (HashesEqual(ComputeHash(Clipboard.GetText()), hash))
            Clipboard.Clear();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static async Task ScheduleWipeAsync(
        byte[]            hashAtCopyTime,
        int               delaySecs,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySecs), ct);

            // Marshal the actual clipboard operation back onto the STA UI thread.
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Confirm we are still the owner: another SetAndScheduleWipe call
                // (for a different piece of text) would have cancelled ct, but
                // the same text copied twice results in the same hash — both tasks
                // survive and both try to wipe. The second one finds an empty
                // clipboard via ContainsText() and returns harmlessly.
                lock (_lock)
                {
                    if (_ownerHash is null || !HashesEqual(_ownerHash, hashAtCopyTime))
                        return; // a different copy event superseded this one
                }

                if (!Clipboard.ContainsText()) return;

                byte[] currentHash = ComputeHash(Clipboard.GetText());
                if (HashesEqual(currentHash, hashAtCopyTime))
                    Clipboard.Clear();
            });
        }
        catch (OperationCanceledException)
        {
            // Normal: a newer copy cancelled this wipe — the newer task handles it.
        }
        catch
        {
            // Swallow all other exceptions (clipboard locked by another process, etc.)
            // A missed wipe is a degraded-mode outcome, not a crash-worthy event.
        }
    }

    private static byte[] ComputeHash(string text) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Constant-time comparison to prevent timing side-channels on hash equality.
    /// The clipboard content is not a secret, so this is defense-in-depth rather
    /// than strictly necessary, but it costs nothing.
    /// </summary>
    private static bool HashesEqual(byte[] a, byte[] b) =>
        a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
}
