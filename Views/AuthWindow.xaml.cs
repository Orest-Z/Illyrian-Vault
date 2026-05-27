using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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
                "Illyria Vault — Dashboard failed to open",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Allow dragging the window by clicking anywhere on it.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}
