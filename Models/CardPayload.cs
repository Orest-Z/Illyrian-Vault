/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using CommunityToolkit.Mvvm.ComponentModel;

namespace IllyrianVault.Models;

public partial class CardPayload : ObservableObject, IEntryPayload
{
    [ObservableProperty] private string _cardholderName = string.Empty;
    [ObservableProperty] private string _cardNumber     = string.Empty;
    [ObservableProperty] private string _expiry         = string.Empty;
    [ObservableProperty] private string _cvv            = string.Empty;
}
