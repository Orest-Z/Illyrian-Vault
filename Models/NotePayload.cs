/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using CommunityToolkit.Mvvm.ComponentModel;

namespace IllyrianVault.Models;

public partial class NotePayload : ObservableObject, IEntryPayload
{
    [ObservableProperty] private string _content = string.Empty;
}
