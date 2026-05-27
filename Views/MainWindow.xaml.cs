using System.Windows;
using System.Windows.Input;

namespace IllyriaVault.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // MainViewModel will be wired here in Phase 2.
        // For now the window just confirms the vault opened successfully.
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
