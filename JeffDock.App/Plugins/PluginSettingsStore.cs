using System.Globalization;
using System.IO;
using System.Text.Json;
using JeffDock.PluginContracts;

namespace JeffDock.App.Plugins;

internal sealed class PluginSettingsStore : IPluginSettings
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly Dictionary<string, string> _values;

    public PluginSettingsStore(string pluginId, IReadOnlyList<PluginSettingDefinition> definitions)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || pluginId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Plugin ID cannot be used as a settings filename.", nameof(pluginId));
        }

        Definitions = definitions;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JeffDock", "PluginSettings");
        _path = Path.Combine(directory, $"{pluginId}.json");
        _values = Load(_path);

        foreach (var definition in Definitions)
        {
            if (!_values.TryGetValue(definition.Key, out var value) || !IsValid(definition, value))
            {
                _values[definition.Key] = definition.DefaultValue;
            }
        }
    }

    public IReadOnlyList<PluginSettingDefinition> Definitions { get; }
    public string FilePath => _path;
    public event EventHandler<PluginSettingChangedEventArgs>? Changed;

    public string GetValue(string key)
    {
        lock (_sync)
        {
            var definition = Definitions.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Unknown plugin setting '{key}'.");
            return _values.GetValueOrDefault(definition.Key, definition.DefaultValue);
        }
    }

    public void SetValue(string key, string value)
    {
        PluginSettingDefinition definition;
        lock (_sync)
        {
            definition = Definitions.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Unknown plugin setting '{key}'.");
            if (!IsValid(definition, value))
            {
                throw new ArgumentException($"Invalid value for {definition.DisplayName}.", nameof(value));
            }

            _values[definition.Key] = value;
            Save();
        }
        Changed?.Invoke(this, new PluginSettingChangedEventArgs(definition.Key, value));
    }

    public string GetJson()
    {
        lock (_sync)
        {
            return JsonSerializer.Serialize(new SettingsDocument(1, _values), JsonOptions);
        }
    }

    public void ApplyJson(string json)
    {
        var document = JsonSerializer.Deserialize<SettingsDocument>(json, JsonOptions)
            ?? throw new JsonException("The settings document is empty.");
        foreach (var definition in Definitions)
        {
            var value = document.Values.GetValueOrDefault(definition.Key, definition.DefaultValue);
            if (!IsValid(definition, value))
            {
                throw new JsonException($"Invalid value for '{definition.Key}'.");
            }
        }
        foreach (var definition in Definitions)
        {
            SetValue(definition.Key, document.Values.GetValueOrDefault(definition.Key, definition.DefaultValue));
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new SettingsDocument(1, _values), JsonOptions));
        File.Move(temporaryPath, _path, true);
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(path), JsonOptions)?.Values
                    ?? new(StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool IsValid(PluginSettingDefinition definition, string value)
    {
        return definition.Type switch
        {
            PluginSettingType.Boolean => bool.TryParse(value, out _),
            PluginSettingType.Integer => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                && (!definition.Minimum.HasValue || number >= definition.Minimum)
                && (!definition.Maximum.HasValue || number <= definition.Maximum),
            PluginSettingType.Decimal => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                && (!definition.Minimum.HasValue || number >= definition.Minimum)
                && (!definition.Maximum.HasValue || number <= definition.Maximum),
            PluginSettingType.Choice => definition.Choices?.Any(choice => string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase)) == true,
            PluginSettingType.Color => System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$"),
            _ => true,
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private sealed record SettingsDocument(int Version, Dictionary<string, string> Values);
}
