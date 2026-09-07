using System.Windows;
using Wpf.Ui.Appearance;

namespace JeffDock.App.Themes;

internal static class AppThemeManager
{
    private const string ThemePathMarker = "/Themes/";

    public static void Apply(IAppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains(ThemePathMarker, StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new ResourceDictionary { Source = theme.ResourceDictionarySource };
        if (existing is null)
            dictionaries.Add(replacement);
        else
            dictionaries[dictionaries.IndexOf(existing)] = replacement;

        ApplicationAccentColorManager.Apply(theme.AccentColor, theme.BaseTheme);
    }
}
