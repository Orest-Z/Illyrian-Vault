using CommunityToolkit.Mvvm.ComponentModel;

namespace IllyrianVault.Models;

public partial class NotePayload : ObservableObject, IEntryPayload
{
    [ObservableProperty] private string _content = string.Empty;
}
