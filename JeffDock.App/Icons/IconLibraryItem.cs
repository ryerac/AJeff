using System.Windows.Media.Imaging;

namespace JeffDock.App.Icons;

public sealed record IconLibraryItem(
    string Id,
    string PackId,
    string PackName,
    string Category,
    string DisplayName,
    byte[] ImageBytes,
    BitmapSource Preview);
