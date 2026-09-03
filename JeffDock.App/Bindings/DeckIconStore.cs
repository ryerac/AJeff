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
        return SaveIcon(GetIconPath(deviceId, sceneId, buttonIndex), LoadBitmap(sourcePath));
    }

    public string SaveIcon(string deviceId, string sceneId, int buttonIndex, byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes, writable: false);
        return SaveIcon(GetIconPath(deviceId, sceneId, buttonIndex), LoadBitmap(stream));
    }

    public string SaveStateIcon(string deviceId, string sceneId, int buttonIndex, string stateId, string sourcePath)
    {
        return SaveIcon(GetStateIconPath(deviceId, sceneId, buttonIndex, stateId), LoadBitmap(sourcePath));
    }

    public string SaveStateIcon(string deviceId, string sceneId, int buttonIndex, string stateId, byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes, writable: false);
        return SaveIcon(GetStateIconPath(deviceId, sceneId, buttonIndex, stateId), LoadBitmap(stream));
    }

    private static string SaveIcon(string destinationPath, BitmapSource source)
    {
        var iconDirectory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(iconDirectory);

        var temporaryPath = Path.Combine(iconDirectory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
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

    public void DeleteAllControlIcons(string deviceId, string sceneId, int buttonIndex)
    {
        var staticPath = GetIconPath(deviceId, sceneId, buttonIndex);
        if (File.Exists(staticPath))
        {
            File.Delete(staticPath);
        }

        var stateDirectory = Path.Combine(GetIconDirectory(deviceId, sceneId), buttonIndex.ToString());
        if (Directory.Exists(stateDirectory))
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    public string? FindIconPath(string deviceId, string sceneId, int buttonIndex)
    {
        var path = GetIconPath(deviceId, sceneId, buttonIndex);
        return File.Exists(path) ? path : null;
    }

    public string? FindStateIconPath(string deviceId, string sceneId, int buttonIndex, string stateId)
    {
        var path = GetStateIconPath(deviceId, sceneId, buttonIndex, stateId);
        return File.Exists(path) ? path : null;
    }

    public bool DeleteStateIcon(string deviceId, string sceneId, int buttonIndex, string stateId)
    {
        var path = GetStateIconPath(deviceId, sceneId, buttonIndex, stateId);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public IReadOnlyDictionary<int, byte[]> LoadButtonImages(
        IReadOnlyDictionary<int, byte[]?> sourceImages,
        int outputWidth,
        int outputHeight,
        int rotationDegreesClockwise)
    {
        var blankIcon = CreateBlankIcon(outputWidth, outputHeight);
        return sourceImages.ToDictionary(
            entry => entry.Key,
            entry => entry.Value is { } bytes
                ? LoadDeviceJpeg(bytes, outputWidth, outputHeight, rotationDegreesClockwise)
                : blankIcon.ToArray());
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

    private string GetStateIconPath(string deviceId, string sceneId, int buttonIndex, string stateId)
    {
        return Path.Combine(
            GetIconDirectory(deviceId, sceneId),
            buttonIndex.ToString(),
            $"{BuildSafeStateFileName(stateId)}.jpg");
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

    private static BitmapSource LoadBitmap(Stream sourceStream)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = sourceStream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] LoadDeviceJpeg(
        byte[] imageBytes,
        int outputWidth,
        int outputHeight,
        int rotationDegreesClockwise)
    {
        using var sourceStream = new MemoryStream(imageBytes, writable: false);
        BitmapSource source = RenderDeviceImage(LoadBitmap(sourceStream), outputWidth, outputHeight);
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

    private static string BuildSafeStateFileName(string stateId)
    {
        if (stateId.Length is > 0 and <= 64
            && stateId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
        {
            return stateId;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stateId)))[..16].ToLowerInvariant();
        return $"state-{hash}";
    }
}
