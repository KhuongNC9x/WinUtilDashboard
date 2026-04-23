using System;
using System.Linq;
using System.Windows;

namespace WinUtilDashboard.Infrastructure;

/// <summary>
/// Swaps the active theme ResourceDictionary without wiping out other merged
/// dictionaries (icons, global styles, etc).
/// </summary>
public static class ThemeManager
{
    private const string ThemeMarker = "/Themes/";

    public static void ApplyTheme(string themeUri)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("Application.Current is null");

        var mergedDicts = app.Resources.MergedDictionaries;

        // Remove ONLY existing theme dicts (not the whole collection).
        var existingThemes = mergedDicts
            .Where(d => d.Source?.OriginalString.Contains(ThemeMarker, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        foreach (var dict in existingThemes)
            mergedDicts.Remove(dict);

        mergedDicts.Add(new ResourceDictionary { Source = new Uri(themeUri, UriKind.Relative) });
    }
}
