using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace JeffDock.App.Themes;

internal interface IAppTheme
{
    string Id { get; }
    string DisplayName { get; }
    Uri ResourceDictionarySource { get; }
    ApplicationTheme BaseTheme { get; }
    Color AccentColor { get; }
}
