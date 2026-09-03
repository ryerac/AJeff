using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace JeffDock.App.Icons;

internal sealed class IconLibraryCatalog
{
    private const string ResourcePrefix = "assets/icons/packs/";
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".svg",
    };

    public IReadOnlyList<IconLibraryItem> Icons { get; }

    public IconLibraryCatalog()
    {
        Icons = LoadIcons(typeof(IconLibraryCatalog).Assembly);
    }

    public IconLibraryItem? FindIcon(string iconId)
    {
        return Icons.FirstOrDefault(icon => string.Equals(icon.Id, iconId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<IconLibraryItem> LoadIcons(Assembly assembly)
    {
        var resources = ReadPackResources(assembly);
        var manifests = resources
            .Where(resource => resource.Key.EndsWith("/pack.json", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                resource => GetPathParts(resource.Key)[0],
                resource => ReadManifest(resource.Value),
                StringComparer.OrdinalIgnoreCase);

        return resources
            .Where(resource => ImageExtensions.Contains(Path.GetExtension(resource.Key)))
            .Select(resource => CreateItem(resource.Key, resource.Value, manifests))
            .Where(item => item is not null)
            .Cast<IconLibraryItem>()
            .OrderBy(item => item.PackName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, byte[]> ReadPackResources(Assembly assembly)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase)))
        {
            using var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream is null)
            {
                continue;
            }

            using var reader = new ResourceReader(resourceStream);
            foreach (DictionaryEntry entry in reader)
            {
                if (entry.Key is not string key || !key.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bytes = entry.Value switch
                {
                    byte[] value => value,
                    Stream value => ReadAllBytes(value),
                    _ => null,
                };

                if (bytes is not null)
                {
                    result[key.Replace('\\', '/')] = bytes;
                }
            }
        }

        return result;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static IconLibraryItem? CreateItem(
        string resourcePath,
        byte[] imageBytes,
        IReadOnlyDictionary<string, IconPackManifest> manifests)
    {
        var parts = GetPathParts(resourcePath);
        if (parts.Length < 3)
        {
            return null;
        }

        var packDirectory = parts[0];
        var category = string.Join(" / ", parts.Skip(1).SkipLast(1).Select(ToDisplayName));
        var fileId = Path.GetFileNameWithoutExtension(parts[^1]);
        var manifest = manifests.GetValueOrDefault(packDirectory);
        var packId = manifest?.Id ?? packDirectory.ToLowerInvariant();
        var packName = manifest?.Name ?? ToDisplayName(packDirectory);

        try
        {
            var isVector = Path.GetExtension(resourcePath).Equals(".svg", StringComparison.OrdinalIgnoreCase);
            var previewBytes = isVector
                ? SvgIconRenderer.RenderPng(imageBytes, "#FFFFFF", null, size: 72)
                : imageBytes;
            var preview = SvgIconRenderer.ToBitmapSource(previewBytes);

            return new IconLibraryItem(
                $"{packId}/{string.Join('/', parts.Skip(1).SkipLast(1)).ToLowerInvariant()}/{fileId.ToLowerInvariant()}",
                packId,
                packName,
                category,
                ToDisplayName(fileId),
                imageBytes,
                preview,
                isVector);
        }
        catch
        {
            return null;
        }
    }

    private static string[] GetPathParts(string resourcePath)
    {
        return resourcePath[ResourcePrefix.Length..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static IconPackManifest ReadManifest(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<IconPackManifest>(bytes, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? new IconPackManifest();
        }
        catch (JsonException)
        {
            return new IconPackManifest();
        }
    }

    private static string ToDisplayName(string value)
    {
        var words = value.Replace('-', ' ').Replace('_', ' ');
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(words);
    }

    private sealed class IconPackManifest
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
    }
}
