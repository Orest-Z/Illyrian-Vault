using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using IllyriaVault.ViewModels;

namespace IllyriaVault.Views;

// AuthWindow owns a tiny "CurrentViewModel" property.
// When it changes, the DataTemplates in AuthWindow.xaml automatically
// swap in the right UserControl — no code-behind navigation needed.
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
        ShowLogin();
    }

    private void ShowLogin()
    {
        var vm = new LoginViewModel(App.Encryption, App.Database);
        vm.LoginSucceeded    += OnLoginSucceeded;
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

    private void OnLoginSucceeded()
    {
        // Store the derived session key (LoginViewModel already opened the DB).
        // Open MainWindow, close AuthWindow.
        var main = new MainWindow();
        main.Show();
        Close();
    }

    private void OnVaultCreated()
    {
        // Vault was just created and the DB is open — go straight to the main window.
        var main = new MainWindow();
        main.Show();
        Close();
    }

    // Allow dragging the window by clicking anywhere on it.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}
