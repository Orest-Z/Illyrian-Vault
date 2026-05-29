/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using CommunityToolkit.Mvvm.ComponentModel;

namespace IllyrianVault.Models;

public partial class IdentityPayload : ObservableObject, IEntryPayload
{
    [ObservableProperty] private string _fullName  = string.Empty;
    [ObservableProperty] private string _email     = string.Empty;
    [ObservableProperty] private string _phone     = string.Empty;
    [ObservableProperty] private string _birthdate = string.Empty;
    [ObservableProperty] private string _address   = string.Empty;
}
