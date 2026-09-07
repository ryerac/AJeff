using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace JeffDock.App.Themes;

internal static class AppThemeCatalog
{
    public static IAppTheme Default { get; } = new AppTheme(
        "aurora",
        "AJeff Studio",
        new Uri("/AJeff;component/Themes/Aurora.xaml", UriKind.Relative),
        ApplicationTheme.Light,
        Color.FromRgb(210, 47, 58));

    public static IReadOnlyList<IAppTheme> All { get; } = [Default];

    private sealed record AppTheme(
        string Id,
        string DisplayName,
        Uri ResourceDictionarySource,
        ApplicationTheme BaseTheme,
        Color AccentColor) : IAppTheme;
}
