using System.Windows;
using System.Windows.Input;
using IllyriaVault.ViewModels;

namespace IllyriaVault.Views;

public partial class AddEntryWindow : Window
{
    public AddEntryWindow(AddEntryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.SaveRequested   += () => DialogResult = true;
        vm.CancelRequested += () => DialogResult = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        DragMove();
}
