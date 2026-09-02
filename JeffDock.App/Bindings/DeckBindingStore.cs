using System.Text.Json;
using System.IO;
using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal sealed class DeckBindingStore
{
    private readonly string _filePath;
    private readonly Dictionary<DeckBindingKey, DeckBindingActionKind> _bindings = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public DeckBindingStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JeffDock");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "bindings.json");
        Load();
    }

    public DeckBindingActionKind GetAction(MonitoredDeckDevice device, DeckControlType controlType, int controlIndex, DeckInputEventType triggerEventType)
    {
        var key = new DeckBindingKey(device.DeviceId, controlType, controlIndex, triggerEventType);
        if (_bindings.TryGetValue(key, out var action))
        {
            return action;
        }

        return GetDefaultAction(device.ProfileName, controlType, controlIndex, triggerEventType);
    }

    public void SetAction(MonitoredDeckDevice device, DeckControlType controlType, int controlIndex, DeckInputEventType triggerEventType, DeckBindingActionKind actionKind)
    {
        var key = new DeckBindingKey(device.DeviceId, controlType, controlIndex, triggerEventType);
        _bindings[key] = actionKind;
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var bindings = JsonSerializer.Deserialize<List<StoredDeckBinding>>(json, _jsonOptions) ?? [];
            _bindings.Clear();

            foreach (var binding in bindings)
            {
                var key = new DeckBindingKey(binding.DeviceId, binding.ControlType, binding.ControlIndex, binding.TriggerEventType);
                _bindings[key] = binding.ActionKind;
            }
        }
        catch
        {
            _bindings.Clear();
        }
    }

    private void Save()
    {
        var bindings = _bindings
            .Select(entry => new StoredDeckBinding(
                entry.Key.DeviceId,
                entry.Key.ControlType,
                entry.Key.ControlIndex,
                entry.Key.TriggerEventType,
                entry.Value
            ))
            .OrderBy(binding => binding.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(binding => binding.ControlType)
            .ThenBy(binding => binding.ControlIndex)
            .ThenBy(binding => binding.TriggerEventType)
            .ToList();

        var json = JsonSerializer.Serialize(bindings, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static DeckBindingActionKind GetDefaultAction(string profileName, DeckControlType controlType, int controlIndex, DeckInputEventType triggerEventType)
    {
        if (string.Equals(profileName, "AJAZZ AKP03E", StringComparison.OrdinalIgnoreCase))
        {
            if (controlType == DeckControlType.Encoder && controlIndex == 1 && triggerEventType == DeckInputEventType.EncoderTurn)
            {
                return DeckBindingActionKind.VolumeAdjust;
            }

            if (controlType == DeckControlType.Encoder && controlIndex == 1 && triggerEventType == DeckInputEventType.EncoderPress)
            {
                return DeckBindingActionKind.ToggleMute;
            }
        }

        return DeckBindingActionKind.None;
    }
}
