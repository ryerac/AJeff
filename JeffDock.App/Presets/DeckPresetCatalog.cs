using System.IO;
using System.Reflection;
using System.Text.Json;

namespace JeffDock.App.Presets;

internal sealed class DeckPresetCatalog
{
    private const string BundledResourceSuffix = ".Assets.Presets.core.json";
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<DeckPresetSection> Sections { get; }

    public DeckPresetCatalog(IEnumerable<string>? pluginPresetJson = null)
    {
        var configurations = new List<DeckPresetConfiguration>();
        var assembly = typeof(DeckPresetCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(BundledResourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                configurations.Add(ReadConfiguration(stream));
            }
        }

        if (pluginPresetJson is not null)
        {
            foreach (var json in pluginPresetJson)
            {
                try
                {
                    configurations.Add(JsonSerializer.Deserialize<DeckPresetConfiguration>(json, _jsonOptions)
                        ?? new DeckPresetConfiguration(1, []));
                }
                catch (JsonException)
                {
                    // One malformed plugin preset must not prevent other plugins from loading.
                }
            }
        }

        var userPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JeffDock",
            "presets.json");
        if (File.Exists(userPath))
        {
            try
            {
                using var stream = File.OpenRead(userPath);
                configurations.Add(ReadConfiguration(stream));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // A malformed optional user file must not prevent JeffDock from starting.
            }
        }

        Sections = Merge(configurations);
    }

    private DeckPresetConfiguration ReadConfiguration(Stream stream) =>
        JsonSerializer.Deserialize<DeckPresetConfiguration>(stream, _jsonOptions)
        ?? new DeckPresetConfiguration(1, []);

    private static IReadOnlyList<DeckPresetSection> Merge(IEnumerable<DeckPresetConfiguration> configurations)
    {
        var sections = new List<DeckPresetSection>();
        foreach (var configuration in configurations.Where(item => item.Version == 1))
        {
            foreach (var incoming in configuration.Sections)
            {
                var sectionIndex = sections.FindIndex(section =>
                    string.Equals(section.Id, incoming.Id, StringComparison.OrdinalIgnoreCase));
                if (sectionIndex < 0)
                {
                    sections.Add(incoming);
                    continue;
                }

                var existing = sections[sectionIndex];
                var presets = existing.Presets.ToList();
                foreach (var preset in incoming.Presets)
                {
                    var presetIndex = presets.FindIndex(item =>
                        string.Equals(item.Id, preset.Id, StringComparison.OrdinalIgnoreCase));
                    if (presetIndex < 0)
                    {
                        presets.Add(preset);
                    }
                    else
                    {
                        presets[presetIndex] = preset;
                    }
                }

                sections[sectionIndex] = incoming with { Presets = presets };
            }
        }

        return sections;
    }

    private sealed record DeckPresetConfiguration(int Version, IReadOnlyList<DeckPresetSection> Sections);
}
