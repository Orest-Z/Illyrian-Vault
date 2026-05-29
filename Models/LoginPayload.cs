/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using CommunityToolkit.Mvvm.ComponentModel;

namespace IllyrianVault.Models;

public partial class LoginPayload : ObservableObject, IEntryPayload
{
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _url      = string.Empty;
    [ObservableProperty] private string _notes    = string.Empty;
}
