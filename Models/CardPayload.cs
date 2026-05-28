using CommunityToolkit.Mvvm.ComponentModel;

namespace IllyrianVault.Models;

public partial class CardPayload : ObservableObject, IEntryPayload
{
    [ObservableProperty] private string _cardholderName = string.Empty;
    [ObservableProperty] private string _cardNumber     = string.Empty;
    [ObservableProperty] private string _expiry         = string.Empty;
    [ObservableProperty] private string _cvv            = string.Empty;
}
