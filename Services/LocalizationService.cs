using System.Windows;

namespace IllyrianVault.Services;

public enum AppLanguage { En, Sq }

public sealed class LocalizationService
{
    private static readonly Uri EnUri = new("pack://application:,,,/Resources/Localization/Strings.en.xaml");
    private static readonly Uri SqUri = new("pack://application:,,,/Resources/Localization/Strings.sq.xaml");

    public AppLanguage Current { get; private set; } = AppLanguage.En;

    public void Apply(AppLanguage language)
    {
        Current = language;
        Swap(language == AppLanguage.Sq ? SqUri : EnUri, EnUri, SqUri);
    }

    /// <summary>Indexer: localization["UnlockButton"] → "Unlock Vault" or "Hap Kasafortën"</summary>
    public string this[string key] =>
        Application.Current.TryFindResource(key) is string s ? s : $"[{key}]";

    private static void Swap(Uri next, params Uri[] toRemove)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        var old = merged.FirstOrDefault(d => toRemove.Contains(d.Source));
        if (old is not null) merged.Remove(old);
        merged.Add(new ResourceDictionary { Source = next });
    }
}
