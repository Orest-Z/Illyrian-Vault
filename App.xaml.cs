/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.IO;
using System.Windows;
using IllyrianVault.Services;

namespace IllyrianVault;

public partial class App : Application
{
    // Global singletons — created once, shared with all ViewModels.
    // Java analogy: these are your @Singleton Spring beans, but wired manually.
    //
    // EncryptionService and DatabaseService are created eagerly (no I/O, no
    // heavy init). ThemeService and LocalizationService pre-parse ResourceDictionary
    // files, which requires the WPF pack:// URI handler to be registered first.
    // They are initialized in OnStartup (after base.OnStartup) to guarantee that.
    public static EncryptionService   Encryption   { get; } = new();
    public static DatabaseService     Database     { get; } = new();
    public static ThemeService        Theme        { get; private set; } = null!;
    public static LocalizationService Localization { get; private set; } = null!;

    // ── Session key management ─────────────────────────────────────────────────
    // The 32-byte AES session key is stored in a GC-Pinned byte[] so the GC
    // cannot move (and therefore duplicate) it.  ClearSessionKey() uses
    // CryptographicOperations.ZeroMemory — a volatile write that the JIT cannot
    // elide — before releasing the pin.  Never assign to SessionKey directly;
    // always use SetSessionKey / ClearSessionKey.

    private static byte[]              _sessionKey    = Array.Empty<byte>();
    private static System.Runtime.InteropServices.GCHandle _sessionKeyPin;

    public static byte[] SessionKey => _sessionKey;

    public static void SetSessionKey(byte[] key)
    {
        // Zero the old key before replacing it.
        Services.SecureMemory.ZeroSessionKey(ref _sessionKey, ref _sessionKeyPin);
        _sessionKey    = key;
        _sessionKeyPin = System.Runtime.InteropServices.GCHandle.Alloc(
                             _sessionKey,
                             System.Runtime.InteropServices.GCHandleType.Pinned);
    }

    public static void ClearSessionKey() =>
        Services.SecureMemory.ZeroSessionKey(ref _sessionKey, ref _sessionKeyPin);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // SQLCipher's native provider must be registered before the first connection.
        SQLitePCL.Batteries_V2.Init();

        // Initialized here (not in static initializer) because ResourceDictionary
        // requires the WPF pack:// URI handler, which is only registered after
        // base.OnStartup() runs Application.Init().
        Theme        = new ThemeService();
        Localization = new LocalizationService();

        DispatcherUnhandledException += (_, ex) =>
        {
            LogOrShow(ex.Exception?.ToString());
            ex.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            LogOrShow(ex.ExceptionObject?.ToString());

        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            ex.SetObserved();
            LogOrShow(ex.Exception?.Message);
        };

        var auth = new Views.AuthWindow();
        auth.Show();
    }

    private static void LogOrShow(string? message)
    {
        // Write the full details (stack trace etc.) to a local log file.
        // The user never sees raw exception strings — only a safe generic message.
        try
        {
            var logDir  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IllyriaVault", "Logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, $"error-{DateTime.Now:yyyy-MM-dd}.log");

            // Rotate at 512 KB to prevent unbounded log growth.
            const long MaxLogBytes = 512 * 1024;
            if (File.Exists(logFile) && new FileInfo(logFile).Length > MaxLogBytes)
            {
                var rotated = Path.ChangeExtension(logFile, ".1.log");
                File.Copy(logFile, rotated, overwrite: true);
                File.Delete(logFile);
            }

            File.AppendAllText(logFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}" +
                $"{message ?? "Unknown error"}{Environment.NewLine}" +
                $"{"─".PadRight(80, '─')}{Environment.NewLine}");
        }
        catch { /* log write failed — best effort only */ }

        MessageBox.Show(
            "An unexpected error occurred. Illyrian Vault will try to continue.\n\n" +
            "If the problem persists, details have been written to:\n" +
            $"%LOCALAPPDATA%\\IllyriaVault\\Logs",
            "Illyrian Vault — Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        ClearSessionKey();
        ClipboardGuard.ClearNow();
        await Database.DisposeAsync();
        base.OnExit(e);
    }
}
