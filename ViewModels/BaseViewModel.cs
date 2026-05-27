using CommunityToolkit.Mvvm.ComponentModel;

namespace IllyriaVault.ViewModels;

// Java analogy: this is your abstract Presenter base class that implements
// INotifyPropertyChanged via CommunityToolkit source generators.
// Any field decorated with [ObservableProperty] gets a full auto-generated
// property + PropertyChanged notification — zero boilerplate.
// IMPORTANT: The class must be marked 'partial' for the source generator to work.
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    protected void ClearError() => ErrorMessage = string.Empty;
}
