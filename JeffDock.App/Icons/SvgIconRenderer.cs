using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace JeffDock.App.Icons;

internal static class SvgIconRenderer
{
    public static byte[] RenderPng(byte[] svgBytes, string foreground, string? background, int size = 240, double padding = 0.04)
    {
        var svgText = Encoding.UTF8.GetString(svgBytes)
            .Replace("currentColor", NormalizeColor(foreground), StringComparison.OrdinalIgnoreCase);

        using var svgStream = new MemoryStream(Encoding.UTF8.GetBytes(svgText));
        using var svg = new SKSvg();
        var picture = svg.Load(svgStream) ?? throw new InvalidDataException("The SVG could not be rendered.");
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidDataException("The SVG has no visible bounds.");
        }

        using var sourceBitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var sourceCanvas = new SKCanvas(sourceBitmap))
        {
            sourceCanvas.Clear(SKColors.Transparent);
            var sourceScale = Math.Min(size / bounds.Width, size / bounds.Height);
            var sourceX = ((size - (bounds.Width * sourceScale)) / 2) - (bounds.Left * sourceScale);
            var sourceY = ((size - (bounds.Height * sourceScale)) / 2) - (bounds.Top * sourceScale);
            sourceCanvas.Translate((float)sourceX, (float)sourceY);
            sourceCanvas.Scale((float)sourceScale);
            sourceCanvas.DrawPicture(picture);
            sourceCanvas.Flush();
        }

        var paintedBounds = FindPaintedBounds(sourceBitmap);
        using var sourceImage = SKImage.FromBitmap(sourceBitmap);
        using var outputBitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var outputCanvas = new SKCanvas(outputBitmap))
        {
            outputCanvas.Clear(background is null ? SKColors.Transparent : SKColor.Parse(NormalizeColor(background)));
            var available = size * (1 - (padding * 2));
            var outputScale = Math.Min(available / paintedBounds.Width, available / paintedBounds.Height);
            var outputWidth = paintedBounds.Width * outputScale;
            var outputHeight = paintedBounds.Height * outputScale;
            var destination = new SKRect(
                (float)((size - outputWidth) / 2),
                (float)((size - outputHeight) / 2),
                (float)((size + outputWidth) / 2),
                (float)((size + outputHeight) / 2));
            using var paint = new SKPaint { IsAntialias = true };
            outputCanvas.DrawImage(
                sourceImage,
                paintedBounds,
                destination,
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
                paint);
            outputCanvas.Flush();
        }

        using var image = SKImage.FromBitmap(outputBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKRect FindPaintedBounds(SKBitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top)
        {
            throw new InvalidDataException("The SVG has no visible pixels.");
        }

        return new SKRect(left, top, right + 1, bottom + 1);
    }

    public static BitmapSource ToBitmapSource(byte[] imageBytes, int decodeWidth = 96)
    {
        using var stream = new MemoryStream(imageBytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static string NormalizeColor(string color)
    {
        var value = color.Trim();
        if (!value.StartsWith('#'))
        {
            value = $"#{value}";
        }

        if (value.Length is not (7 or 9) || !value[1..].All(Uri.IsHexDigit))
        {
            throw new FormatException("Use a colour in #RRGGBB or #AARRGGBB format.");
        }

        return value;
    }
}
