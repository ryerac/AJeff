using System.IO;
using System.Text.Json;
using JeffDock.Core.Deck;

namespace JeffDock.App.Settings;

internal sealed class DisplaySettingsStore
{
    private readonly string _path;
    private readonly Dictionary<string, DeckDisplaySettings> _devices = new(StringComparer.OrdinalIgnoreCase);
    public event Action<string, DeckDisplaySettings>? Changed;
    public IReadOnlyDictionary<string, DeckDisplaySettings> Devices => _devices;

    public DisplaySettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JeffDock", "display-settings.json");
        try
        {
            if (!File.Exists(_path)) return;
            var document = JsonSerializer.Deserialize<Dictionary<string, DeckDisplaySettings?>>(File.ReadAllText(_path));
            if (document is null) return;
            foreach (var (id, settings) in document)
            {
                if (settings is null) continue;
                try { settings.Validate(); _devices[id] = settings; }
                catch (ArgumentException) { /* Ignore invalid settings for this device. */ }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Use defaults if settings cannot be read, matching application settings.
        }
    }

    public DeckDisplaySettings Get(string deviceId) => _devices.GetValueOrDefault(deviceId) ?? new();

    public void Save(string deviceId, DeckDisplaySettings settings)
    {
        settings.Validate();
        var updated = new Dictionary<string, DeckDisplaySettings>(_devices, StringComparer.OrdinalIgnoreCase)
        {
            [deviceId] = settings,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, overwrite: true);
        _devices[deviceId] = settings;
        Changed?.Invoke(deviceId, settings);
    }
}
