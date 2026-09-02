using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JeffDock.App.Bindings;

internal sealed class DeckIconStore
{
    private const int IconSize = 60;

    private readonly string _rootDirectory;

    public DeckIconStore()
    {
        _rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JeffDock");
    }

    public string SaveIcon(string deviceId, string sceneId, int buttonIndex, string sourcePath)
    {
        var iconDirectory = GetIconDirectory(deviceId, sceneId);
        Directory.CreateDirectory(iconDirectory);

        var destinationPath = Path.Combine(iconDirectory, $"{buttonIndex}.jpg");
        var temporaryPath = Path.Combine(iconDirectory, $".{buttonIndex}.{Guid.NewGuid():N}.tmp");

        try
        {
            var source = LoadBitmap(sourcePath);
            var rendered = RenderSquareIcon(source);
            var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(rendered));

            using (var stream = File.Create(temporaryPath))
            {
                encoder.Save(stream);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return destinationPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void DeleteSceneIcons(string deviceId, string sceneId)
    {
        var sceneDirectory = Path.GetDirectoryName(GetIconDirectory(deviceId, sceneId));
        if (sceneDirectory is not null && Directory.Exists(sceneDirectory))
        {
            Directory.Delete(sceneDirectory, recursive: true);
        }
    }

    public bool DeleteIcon(string deviceId, string sceneId, int buttonIndex)
    {
        var path = GetIconPath(deviceId, sceneId, buttonIndex);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public string? FindIconPath(string deviceId, string sceneId, int buttonIndex)
    {
        var path = GetIconPath(deviceId, sceneId, buttonIndex);
        return File.Exists(path) ? path : null;
    }

    public IReadOnlyDictionary<int, byte[]> LoadButtonImages(
        string deviceId,
        string sceneId,
        IEnumerable<int> buttonIndexes,
        int outputWidth,
        int outputHeight,
        int rotationDegreesClockwise)
    {
        var blankIcon = CreateBlankIcon(outputWidth, outputHeight);
        return buttonIndexes.ToDictionary(
            buttonIndex => buttonIndex,
            buttonIndex =>
            {
                var path = GetIconPath(deviceId, sceneId, buttonIndex);
                return File.Exists(path)
                    ? LoadDeviceJpeg(path, outputWidth, outputHeight, rotationDegreesClockwise)
                    : blankIcon.ToArray();
            });
    }

    private string GetIconDirectory(string deviceId, string sceneId)
    {
        return Path.Combine(
            _rootDirectory,
            BuildDeviceDirectoryName(deviceId),
            BuildSafeSceneDirectoryName(sceneId),
            "Icons");
    }

    private string GetIconPath(string deviceId, string sceneId, int buttonIndex)
    {
        return Path.Combine(GetIconDirectory(deviceId, sceneId), $"{buttonIndex}.jpg");
    }

    private static BitmapSource LoadBitmap(string sourcePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(sourcePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] LoadDeviceJpeg(
        string sourcePath,
        int outputWidth,
        int outputHeight,
        int rotationDegreesClockwise)
    {
        BitmapSource source = RenderDeviceImage(LoadBitmap(sourcePath), outputWidth, outputHeight);
        if (rotationDegreesClockwise % 360 != 0)
        {
            source = new TransformedBitmap(source, new RotateTransform(rotationDegreesClockwise));
            source.Freeze();
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource RenderDeviceImage(BitmapSource source, int outputWidth, int outputHeight)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, outputWidth, outputHeight));
            drawing.DrawImage(source, new Rect(0, 0, outputWidth, outputHeight));
        }

        var rendered = new RenderTargetBitmap(outputWidth, outputHeight, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static BitmapSource RenderSquareIcon(BitmapSource source)
    {
        var scale = Math.Max(IconSize / (double)source.PixelWidth, IconSize / (double)source.PixelHeight);
        var width = source.PixelWidth * scale;
        var height = source.PixelHeight * scale;
        var x = (IconSize - width) / 2;
        var y = (IconSize - height) / 2;

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, IconSize, IconSize));
            drawing.DrawImage(source, new Rect(x, y, width, height));
        }

        var rendered = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static byte[] CreateBlankIcon(int width, int height)
    {
        var pixels = new byte[width * height * 3];
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgr24,
            null,
            pixels,
            width * 3);
        bitmap.Freeze();

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string BuildDeviceDirectoryName(string deviceId)
    {
        var readable = new string(deviceId
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_')
            .ToArray())
            .Trim('.');

        if (readable.Length == 0)
        {
            readable = "device";
        }

        readable = readable[..Math.Min(readable.Length, 48)];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId)))[..12].ToLowerInvariant();
        return $"{readable}-{hash}";
    }

    private static string BuildSafeSceneDirectoryName(string sceneId)
    {
        if (sceneId.Length is > 0 and <= 64
            && sceneId is not "." and not ".."
            && sceneId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
        {
            return sceneId;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sceneId)))[..16].ToLowerInvariant();
        return $"scene-{hash}";
    }
}
