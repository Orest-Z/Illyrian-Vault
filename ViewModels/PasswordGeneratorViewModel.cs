using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IllyrianVault.ViewModels;

public partial class PasswordGeneratorViewModel : ObservableObject
{
    private static readonly string[] _wordList =
    [
        "apple",  "brave",  "cloud",  "dance",  "eagle",  "flame",  "grace",  "heart",
        "ivory",  "jewel",  "knife",  "lunar",  "maple",  "night",  "ocean",  "piano",
        "queen",  "river",  "storm",  "tiger",  "ultra",  "valor",  "water",  "xenon",
        "yacht",  "zebra",  "amber",  "brick",  "cedar",  "drift",  "ember",  "frost",
        "globe",  "haven",  "inlet",  "joint",  "karma",  "lemon",  "music",  "noble",
        "onyx",   "pearl",  "quest",  "robin",  "solar",  "torch",  "unity",  "viper",
        "witch",  "yield",  "arena",  "blaze",  "crown",  "dream",  "earth",  "field",
        "grand",  "image",  "judge",  "light",  "magic",  "nerve",  "orbit",  "prism",
        "quartz", "rainy",  "shade",  "twist",  "umbra",  "venus",  "walls",  "young",
        "alpha",  "blast",  "crisp",  "dunes",  "flint",  "grail",  "heron",  "index",
        "lotus",  "nexus",  "oasis",  "pilot",  "quiet",  "realm",  "sigma",  "titan",
        "vivid",  "windy",  "stone",  "spark",  "ridge",  "swift",  "polar",  "crest",
        "creek",  "bluff",  "grove",  "shore",  "mesa",   "vale",   "brook",  "cliff"
    ];

    [ObservableProperty] private int  _length        = 16;
    [ObservableProperty] private bool _useUppercase  = true;
    [ObservableProperty] private bool _useLowercase  = true;
    [ObservableProperty] private bool _useNumbers    = true;
    [ObservableProperty] private bool _useSymbols    = false;
    [ObservableProperty] private bool _usePassphrase = false;
    [ObservableProperty] private int  _wordCount     = 4;
    [ObservableProperty] private bool _isExpanded    = false;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    [NotifyCanExecuteChangedFor(nameof(UsePasswordCommand))]
    private string _generatedPassword = string.Empty;

    // Raised when the user clicks "Use This Password" — consumed by AddEntryViewModel.
    public event Action<string>? PasswordAccepted;

    [RelayCommand]
    private void Generate() =>
        GeneratedPassword = UsePassphrase ? BuildPassphrase() : BuildPassword();

    [RelayCommand(CanExecute = nameof(HasPassword))]
    private void CopyToClipboard() =>
        System.Windows.Clipboard.SetText(GeneratedPassword);

    [RelayCommand(CanExecute = nameof(HasPassword))]
    private void UsePassword() =>
        PasswordAccepted?.Invoke(GeneratedPassword);

    private bool HasPassword() => !string.IsNullOrEmpty(GeneratedPassword);

    private string BuildPassword()
    {
        var pool = new StringBuilder();
        if (UseUppercase) pool.Append("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        if (UseLowercase) pool.Append("abcdefghijklmnopqrstuvwxyz");
        if (UseNumbers)   pool.Append("0123456789");
        if (UseSymbols)   pool.Append("!@#$%^&*()-_=+[]{}|;:,.<>?");
        if (pool.Length == 0) return string.Empty;

        var chars  = pool.ToString();
        var result = new char[Length];
        for (int i = 0; i < Length; i++)
            result[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        return new string(result);
    }

    private string BuildPassphrase()
    {
        var count = Math.Clamp(WordCount, 2, 10);
        var words = new string[count];
        for (int i = 0; i < count; i++)
            words[i] = _wordList[RandomNumberGenerator.GetInt32(_wordList.Length)];
        return string.Join("-", words);
    }
}
