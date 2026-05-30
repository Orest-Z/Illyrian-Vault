/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using CommunityToolkit.Mvvm.ComponentModel;
using IllyrianVault.Services;

namespace IllyrianVault.ViewModels;

public partial class AuthViewModel : ObservableObject
{
    private readonly EncryptionService _crypto;
    private readonly DatabaseService   _db;

    [ObservableProperty]
    private object? _currentViewModel;

    // Carries the username to MainWindow on successful auth.
    public event Action<string>? LoginSucceeded;

    public AuthViewModel(EncryptionService crypto, DatabaseService db)
    {
        _crypto = crypto;
        _db     = db;

        if (DatabaseService.AnyProfileExists())
            ShowLogin();
        else
            ShowRegister();
    }

    public void ShowLogin()
    {
        var vm = new LoginViewModel(_crypto, _db);
        vm.LoginSucceeded     += username => LoginSucceeded?.Invoke(username);
        vm.NavigateToRegister += ShowRegister;
        vm.NavigateToRecovery += ShowRecovery;
        CurrentViewModel = vm;
    }

    public void ShowRegister()
    {
        var vm = new RegisterViewModel(_crypto, _db);
        vm.VaultCreated    += username => LoginSucceeded?.Invoke(username);
        vm.NavigateToLogin += ShowLogin;
        CurrentViewModel = vm;
    }

    public void ShowRecovery()
    {
        var vm = new RecoveryViewModel(_crypto, _db);
        vm.RecoverySucceeded += username => LoginSucceeded?.Invoke(username);
        vm.NavigateToLogin   += ShowLogin;
        CurrentViewModel = vm;
    }
}
