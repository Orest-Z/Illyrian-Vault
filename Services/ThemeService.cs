using System.Windows;

namespace IllyriaVault.Services;

public enum AppTheme { Red, Dark, Light }

public sealed class ThemeService
{
    private static readonly Uri RedUri   = new("pack://application:,,,/Resources/Themes/RedTheme.xaml");
    private static readonly Uri DarkUri  = new("pack://application:,,,/Resources/Themes/DarkTheme.xaml");
    private static readonly Uri LightUri = new("pack://application:,,,/Resources/Themes/LightTheme.xaml");

    public AppTheme Current { get; private set; } = AppTheme.Red;

    public void Apply(AppTheme theme)
    {
        Current = theme;
        var target = theme switch
        {
            AppTheme.Dark  => DarkUri,
            AppTheme.Light => LightUri,
            _              => RedUri,
        };
        Swap(target, RedUri, DarkUri, LightUri);
    }

    private static void Swap(Uri next, params Uri[] toRemove)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        var old = merged.FirstOrDefault(d => toRemove.Contains(d.Source));
        if (old is not null) merged.Remove(old);
        merged.Add(new ResourceDictionary { Source = next });
    }
}
