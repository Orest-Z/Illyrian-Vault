/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyrianVault.Models;
using IllyrianVault.Services;

namespace IllyrianVault.ViewModels;

public partial class AddEntryViewModel : BaseViewModel
{
    private readonly EncryptionService _crypto;
    private readonly byte[]            _sessionKey;

    public event Action? SaveRequested;
    public event Action? CancelRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoginCategory))]
    private string _selectedCategory = "Login";

    [ObservableProperty]
    private IEntryPayload _currentPayload = new LoginPayload();

    public bool IsLoginCategory => SelectedCategory == "Login";

    partial void OnSelectedCategoryChanged(string value)
    {
        CurrentPayload = value switch
        {
            "Note"     => new NotePayload(),
            "Card"     => new CardPayload(),
            "Identity" => new IdentityPayload(),
            _          => new LoginPayload(),
        };
    }

    public PasswordGeneratorViewModel Generator { get; }

    public AddEntryViewModel(EncryptionService crypto, byte[] sessionKey)
    {
        _crypto     = crypto;
        _sessionKey = sessionKey;
        Generator   = new PasswordGeneratorViewModel();
        Generator.PasswordAccepted += p => { if (CurrentPayload is LoginPayload lp) lp.Password = p; };
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => SaveRequested?.Invoke();

    private bool CanSave() => !string.IsNullOrWhiteSpace(Title);

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();

    public PasswordEntry BuildEntry()
    {
        var json             = JsonSerializer.Serialize(CurrentPayload, CurrentPayload.GetType());
        var encryptedPayload = _crypto.Encrypt(json, _sessionKey);

        // Keep flat fields in sync for Login (legacy compat + search).
        string username          = string.Empty;
        string encryptedPassword = string.Empty;
        string url               = string.Empty;
        string notes             = string.Empty;

        if (CurrentPayload is LoginPayload lp)
        {
            username          = lp.Username;
            encryptedPassword = string.IsNullOrEmpty(lp.Password)
                ? string.Empty
                : _crypto.Encrypt(lp.Password, _sessionKey);
            url   = lp.Url;
            notes = lp.Notes;
        }

        return new()
        {
            UserId            = 1,
            Title             = Title.Trim(),
            Username          = username,
            EncryptedPassword = encryptedPassword,
            Url               = url,
            Notes             = notes,
            Category          = SelectedCategory,
            EncryptedPayload  = encryptedPayload,
        };
    }
}
