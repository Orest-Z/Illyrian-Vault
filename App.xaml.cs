/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Windows;
using IllyrianVault.Services;

namespace IllyrianVault;

public partial class App : Application
{
    // Global singletons — created once, shared with all ViewModels.
    // Java analogy: these are your @Singleton Spring beans, but wired manually.
    public static EncryptionService   Encryption   { get; } = new();
    public static DatabaseService     Database     { get; } = new();
    public static ThemeService        Theme        { get; } = new();
    public static LocalizationService Localization { get; } = new();

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

    private static void LogOrShow(string? message) =>
        MessageBox.Show(
            message ?? "An unknown error occurred.",
            "Illyrian Vault — Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

    protected override async void OnExit(ExitEventArgs e)
    {
        ClearSessionKey();
        ClipboardGuard.ClearNow();
        await Database.DisposeAsync();
        base.OnExit(e);
    }
}
