/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Windows;

namespace IllyrianVault.Services;

public enum AppLanguage { En, Sq }

public sealed class LocalizationService
{
    // Pre-parsed once so Apply() is a reference swap, not a re-parse.
    private readonly ResourceDictionary _en = Load("Resources/Localization/Strings.en.xaml");
    private readonly ResourceDictionary _sq = Load("Resources/Localization/Strings.sq.xaml");

    public AppLanguage Current { get; private set; } = AppLanguage.En;

    public void Apply(AppLanguage language)
    {
        Current = language;
        var next = language == AppLanguage.Sq ? _sq : _en;
        Swap(next, _en, _sq);
    }

    /// <summary>Indexer: localization["UnlockButton"] → "Unlock Vault" or "Hap Kasafortën"</summary>
    public string this[string key] =>
        Application.Current.TryFindResource(key) is string s ? s : $"[{key}]";

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
