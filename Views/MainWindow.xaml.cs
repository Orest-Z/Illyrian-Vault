using System.Windows;
using System.Windows.Input;
using IllyriaVault.ViewModels;

namespace IllyriaVault.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(string username)
    {
        InitializeComponent();
        _vm = new MainViewModel(App.Database, App.Encryption, App.SessionKey, username);
        DataContext = _vm;

        _vm.LockRequested += OnLockRequested;

        Loaded += async (_, _) => await _vm.LoadEntriesCommand.ExecuteAsync(null);
    }

    private void OnLockRequested()
    {
        var auth = new AuthWindow();
        auth.Show();
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        DragMove();

    private void MinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e) =>
        Close();
}
