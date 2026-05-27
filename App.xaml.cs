using System.Windows;
using IllyriaVault.Services;

namespace IllyriaVault;

public partial class App : Application
{
    // Global singletons — created once, shared with all ViewModels.
    // Java analogy: these are your @Singleton Spring beans, but wired manually.
    public static EncryptionService   Encryption   { get; } = new();
    public static DatabaseService     Database     { get; } = new();
    public static ThemeService        Theme        { get; } = new();
    public static LocalizationService Localization { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // SQLCipher's native provider must be registered before the first connection.
        SQLitePCL.Batteries_V2.Init();

        // Global unhandled-exception handler for UI thread crashes.
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(
                ex.Exception.Message,
                "Illyria Vault — Unexpected Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ex.Handled = true;
        };

        // AuthWindow decides internally whether to show Login or Register
        // based on whether vault.db + vault.meta already exist.
        var auth = new Views.AuthWindow();
        auth.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await Database.DisposeAsync();
        base.OnExit(e);
    }
}
