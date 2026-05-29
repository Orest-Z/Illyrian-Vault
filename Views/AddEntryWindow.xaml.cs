/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Windows;
using System.Windows.Input;
using IllyrianVault.ViewModels;

namespace IllyrianVault.Views;

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
