/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using IllyrianVault.Services;
using IllyrianVault.ViewModels;
using MahApps.Metro.IconPacks;

namespace IllyrianVault.Views;

public partial class AuthWindow : Window, INotifyPropertyChanged
{
    private object? _currentViewModel;

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set { _currentViewModel = value; PropertyChanged?.Invoke(this, new(nameof(CurrentViewModel))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AuthWindow()
    {
        InitializeComponent();
        DataContext = this;
        if (DatabaseService.AnyProfileExists())
            ShowLogin();
        else
            ShowRegister();
    }

    private void ShowLogin()
    {
        var vm = new LoginViewModel(App.Encryption, App.Database);
        vm.LoginSucceeded     += OnLoginSucceeded;
        vm.NavigateToRegister += ShowRegister;
        CurrentViewModel = vm;
    }

    private void ShowRegister()
    {
        var vm = new RegisterViewModel(App.Encryption, App.Database);
        vm.VaultCreated    += OnVaultCreated;
        vm.NavigateToLogin += ShowLogin;
        CurrentViewModel = vm;
    }

    private void OnLoginSucceeded(string username) => OpenDashboard(username);
    private void OnVaultCreated(string username)   => OpenDashboard(username);

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
