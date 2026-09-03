using System.Windows.Media.Imaging;

namespace JeffDock.App.Icons;

public sealed record IconLibraryItem(
    string Id,
    string PackId,
    string PackName,
    string Category,
    string DisplayName,
    byte[] SourceBytes,
    BitmapSource Preview,
    bool IsVector = false)
{
    public byte[] ImageBytes => GetRenderedBytes("#FFFFFF", "#000000");

    public byte[] GetRenderedBytes(string foreground, string? background)
    {
        return IsVector
            ? SvgIconRenderer.RenderPng(SourceBytes, foreground, background)
            : SourceBytes;
    }
}
