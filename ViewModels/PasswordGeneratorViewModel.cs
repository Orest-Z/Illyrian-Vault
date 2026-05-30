/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IllyrianVault.Services;

namespace IllyrianVault.ViewModels;

public partial class PasswordGeneratorViewModel : ObservableObject
{
    // 512-word list drawn from EFF large wordlist criteria: common, 4-8 letters,
    // easy to type, no offensive terms. log2(512) ≈ 9 bits entropy per word.
    private static readonly string[] _wordList =
    [
        "abacus",   "ability",  "absent",   "absorb",   "abstract", "accent",   "accept",   "account",
        "achieve",  "across",   "action",   "active",   "actual",   "adapt",    "address",  "admiral",
        "admit",    "adorn",    "adrift",   "agent",    "agree",    "alarm",    "album",    "alert",
        "alien",    "almond",   "alpine",   "altar",    "amber",    "ample",    "anchor",   "angel",
        "anger",    "ankle",    "answer",   "appear",   "apple",    "apron",    "archer",   "arctic",
        "argue",    "arise",    "armor",    "arrow",    "artist",   "ascend",   "aspen",    "assist",
        "assume",   "attic",    "autumn",   "awake",    "award",    "aware",    "awning",   "badger",
        "bagel",    "barge",    "barley",   "barrel",   "basket",   "battle",   "beacon",   "beard",
        "bellow",   "bench",    "bison",    "bitter",   "blade",    "bland",    "blaze",    "bliss",
        "bloom",    "blunt",    "blush",    "bonus",    "bottle",   "bounce",   "brace",    "brain",
        "brand",    "brave",    "brick",    "bridge",   "brief",    "bright",   "brisk",    "broad",
        "brook",    "brush",    "budget",   "bunny",    "burrow",   "cabin",    "cactus",   "camel",
        "candle",   "cannon",   "canyon",   "captain",  "cargo",    "carrot",   "castle",   "casual",
        "cattle",   "cavern",   "cedar",    "cement",   "chain",    "chalk",    "chance",   "change",
        "chapter",  "charge",   "charm",    "chart",    "chase",    "cherry",   "chest",    "chief",
        "chisel",   "chrome",   "cider",    "circle",   "claim",    "clamp",    "clash",    "clean",
        "clever",   "cliff",    "climb",    "cloak",    "cloud",    "clover",   "coarse",   "cobalt",
        "coffee",   "comet",    "commit",   "common",   "copper",   "coral",    "corner",   "cotton",
        "couch",    "couple",   "court",    "cover",    "cozy",     "crane",    "crash",    "crawl",
        "cream",    "create",   "credit",   "crisp",    "cross",    "crowd",    "crown",    "crush",
        "crystal",  "curve",    "cycle",    "dagger",   "daring",   "dazzle",   "decide",   "decode",
        "delta",    "dense",    "depot",    "depth",    "desert",   "detail",   "device",   "devote",
        "digit",    "dinner",   "direct",   "distant",  "divide",   "donor",    "draft",    "dragon",
        "drain",    "drama",    "drape",    "dream",    "drift",    "drive",    "drown",    "dunes",
        "dwarf",    "eagle",    "earthy",   "effect",   "effort",   "elbow",    "elite",    "ember",
        "emerge",   "empire",   "energy",   "engine",   "enter",    "equal",    "escape",   "event",
        "evolve",   "exact",    "exile",    "exist",    "expand",   "expert",   "extend",   "fabric",
        "factor",   "fancy",    "feast",    "fence",    "ferry",    "fiber",    "field",    "figure",
        "final",    "finger",   "fjord",    "flame",    "flare",    "flask",    "fleet",    "flesh",
        "flint",    "flood",    "flour",    "flower",   "focus",    "follow",   "force",    "forge",
        "forest",   "fossil",   "found",    "frame",    "fresh",    "frost",    "frozen",   "future",
        "galaxy",   "garlic",   "gather",   "gentle",   "ghost",    "giant",    "ginger",   "glare",
        "glass",    "glide",    "glory",    "glove",    "gnome",    "goblin",   "golden",   "grace",
        "grand",    "grant",    "graph",    "grasp",    "gravel",   "green",    "grief",    "grill",
        "grove",    "growl",    "guard",    "guest",    "guide",    "guild",    "gusto",    "habit",
        "harbor",   "harsh",    "haven",    "heart",    "heavy",    "hedge",    "herald",   "heron",
        "hollow",   "honor",    "horse",    "hotel",    "hover",    "human",    "humor",    "hunter",
        "image",    "impact",   "inner",    "insight",  "invite",   "inward",   "ivory",    "jester",
        "jewel",    "joker",    "joust",    "judge",    "jumble",   "jungle",   "karma",    "kayak",
        "keeper",   "knife",    "knock",    "lantern",  "large",    "laser",    "launch",   "layer",
        "leader",   "ledger",   "lemon",    "level",    "lever",    "linen",    "lively",   "local",
        "lodge",    "logic",    "loyal",    "lunar",    "magic",    "maple",    "marble",   "master",
        "meadow",   "medal",    "mercy",    "merge",    "metal",    "minor",    "mirror",   "misty",
        "model",    "moment",   "money",    "moral",    "mortar",   "mount",    "mulch",    "music",
        "mystic",   "narrow",   "nature",   "nerve",    "nomad",    "north",    "notice",   "novel",
        "nymph",    "oasis",    "offer",    "olive",    "orbit",    "order",    "organ",    "orient",
        "ornate",   "outer",    "outrun",   "oxide",    "paint",    "panel",    "paper",    "patch",
        "pause",    "peace",    "peach",    "pearl",    "pedal",    "penny",    "pepper",   "perch",
        "permit",   "petal",    "phantom",  "piece",    "pilot",    "pinch",    "plain",    "plasma",
        "player",   "plume",    "plunge",   "pollen",   "ponder",   "portal",   "powder",   "power",
        "prism",    "probe",    "proof",    "proud",    "prowl",    "pulse",    "puzzle",   "quest",
        "quiet",    "quota",    "radar",    "rapid",    "reach",    "realm",    "rebel",    "refuse",
        "reign",    "relay",    "relic",    "remote",   "render",   "resist",   "revive",   "riddle",
        "ridge",    "risky",    "river",    "robust",   "rocket",   "rotate",   "route",    "royal",
        "rugged",   "rustic",   "sacred",   "sailor",   "salute",   "scale",    "scene",    "scroll",
        "seeker",   "select",   "serene",   "shadow",   "shield",   "shrine",   "signal",   "silent",
        "silver",   "simple",   "siren",    "sketch",   "skill",    "sliver",   "slope",    "smooth",
        "solar",    "solid",    "solve",    "source",   "spark",    "speed",    "sphere",   "spire",
        "splash",   "sprite",   "stable",   "stack",    "stage",    "stamp",    "stark",    "stealth",
        "steel",    "steep",    "stick",    "stone",    "storm",    "strand",   "stray",    "stream",
        "stride",   "strong",   "study",    "subtle",   "summit",   "swift",    "sword",    "symbol",
        "system",   "tackle",   "tangle",   "target",   "tender",   "theory",   "timber",   "titan",
        "torch",    "tower",    "trace",    "track",    "trail",    "train",    "travel",   "valor",
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

    // Raised after each successful copy so MainViewModel can show the toast.
    public event Action? ClipboardWritten;

    [RelayCommand]
    private void Generate() =>
        GeneratedPassword = UsePassphrase ? BuildPassphrase() : BuildPassword();

    [RelayCommand(CanExecute = nameof(HasPassword))]
    private void CopyToClipboard()
    {
        ClipboardGuard.SetAndScheduleWipe(GeneratedPassword);
        ClipboardWritten?.Invoke();
    }

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
