using CommunityToolkit.Mvvm.ComponentModel;

namespace IllyriaVault.Models;

public partial class NotePayload : ObservableObject, IEntryPayload
{
    [ObservableProperty] private string _content = string.Empty;
}
