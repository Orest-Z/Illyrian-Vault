/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Windows;
using System.Windows.Input;
using IllyrianVault.ViewModels;
using MahApps.Metro.IconPacks;

namespace IllyrianVault.Views;

public partial class AuthWindow : Window
{
    private readonly AuthViewModel _vm;

    public AuthWindow()
    {
        InitializeComponent();
        _vm = new AuthViewModel(App.Encryption, App.Database);
        DataContext   = _vm;
        _vm.LoginSucceeded += OpenDashboard;
    }

    private void OpenDashboard(string username)
    {
        try
        {
            var main = new MainWindow(username);
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Illyrian Vault — Dashboard failed to open",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ── Title bar ─────────────────────────────────────────────────────────────
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { MaximizeClick(sender, e); return; }
        if (WindowState == WindowState.Normal) DragMove();
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        bool maximized           = WindowState == WindowState.Maximized;
        OuterBorder.Margin       = maximized ? new Thickness(0)   : new Thickness(20);
        OuterBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(10);
        MaximizeIcon.Kind        = maximized
            ? PackIconMaterialKind.FullscreenExit
            : PackIconMaterialKind.Fullscreen;
    }
}
