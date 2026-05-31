/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Windows;

namespace IllyrianVault.Services;

public enum AppTheme { Red, Dark, Light }

public sealed class ThemeService
{
    // Pre-parsed at construction time so Apply() is a cheap reference swap,
    // not a XAML re-parse (which was ~40–80 ms per switch on slow machines).
    private readonly ResourceDictionary _red   = Load("Resources/Themes/RedTheme.xaml");
    private readonly ResourceDictionary _dark  = Load("Resources/Themes/DarkTheme.xaml");
    private readonly ResourceDictionary _light = Load("Resources/Themes/LightTheme.xaml");

    public AppTheme Current { get; private set; } = AppTheme.Red;

    public void Apply(AppTheme theme)
    {
        Current = theme;
        var next = theme switch
        {
            AppTheme.Dark  => _dark,
            AppTheme.Light => _light,
            _              => _red,
        };
        Swap(next, _red, _dark, _light);
    }

    private static void Swap(ResourceDictionary next, params ResourceDictionary[] toRemove)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        foreach (var d in toRemove)
            if (merged.Contains(d)) merged.Remove(d);
        if (!merged.Contains(next))
            merged.Add(next);
    }

    private static ResourceDictionary Load(string relPath) =>
        new() { Source = new Uri($"pack://application:,,,/{relPath}") };
}
