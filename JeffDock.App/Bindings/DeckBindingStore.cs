using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using JeffDock.App.Bindings.Core;
using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal sealed class DeckBindingStore
{
    private const int CurrentVersion = 3;

    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly Dictionary<DeckBindingKey, string> _bindings = new();
    private readonly Dictionary<DeckBindingKey, Dictionary<string, string>> _actionParameters = new();
    private readonly Dictionary<DeckIconBindingKey, DeckIconMode> _iconBindings = new();
    private readonly Dictionary<string, List<DeckScene>> _scenesByDevice = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _activeSceneByDevice = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public event Action<MonitoredDeckDevice>? ActiveSceneChanged;

    public DeckBindingStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JeffDock");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "bindings.json");
        Load();
    }

    public IReadOnlyList<DeckScene> GetScenes(MonitoredDeckDevice device)
    {
        lock (_sync)
        {
            return EnsureScenes(device.DeviceId).ToList();
        }
    }

    public DeckScene GetActiveScene(MonitoredDeckDevice device)
    {
        lock (_sync)
        {
            var scenes = EnsureScenes(device.DeviceId);
            var activeId = GetActiveSceneId(device.DeviceId);
            return scenes.First(scene => string.Equals(scene.Id, activeId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void SetActiveScene(MonitoredDeckDevice device, string sceneId)
    {
        lock (_sync)
        {
            var scenes = EnsureScenes(device.DeviceId);
            if (!scenes.Any(scene => string.Equals(scene.Id, sceneId, StringComparison.OrdinalIgnoreCase))
                || string.Equals(GetActiveSceneId(device.DeviceId), sceneId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _activeSceneByDevice[device.DeviceId] = sceneId;
            Save();
        }

        ActiveSceneChanged?.Invoke(device);
    }

    public void CycleScene(MonitoredDeckDevice device, int direction)
    {
        lock (_sync)
        {
            var scenes = EnsureScenes(device.DeviceId);
            if (scenes.Count < 2 || direction == 0)
            {
                return;
            }

            var activeId = GetActiveSceneId(device.DeviceId);
            var currentIndex = scenes.FindIndex(scene => string.Equals(scene.Id, activeId, StringComparison.OrdinalIgnoreCase));
            var nextIndex = (currentIndex + Math.Sign(direction) + scenes.Count) % scenes.Count;
            _activeSceneByDevice[device.DeviceId] = scenes[nextIndex].Id;
            Save();
        }

        ActiveSceneChanged?.Invoke(device);
    }

    public DeckScene CreateScene(MonitoredDeckDevice device, string name)
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Enter a scene name.", nameof(name));
        }

        DeckScene createdScene;
        lock (_sync)
        {
            var scenes = EnsureScenes(device.DeviceId);
            if (scenes.Any(scene => string.Equals(scene.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"A scene named '{trimmedName}' already exists.", nameof(name));
            }

            createdScene = new DeckScene(Guid.NewGuid().ToString("N"), trimmedName);
            scenes.Add(createdScene);
            _activeSceneByDevice[device.DeviceId] = createdScene.Id;
            Save();
        }

        ActiveSceneChanged?.Invoke(device);
        return createdScene;
    }

    public DeckScene RenameScene(MonitoredDeckDevice device, string sceneId, string name)
    {
        if (string.Equals(sceneId, DeckScene.DefaultId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Default scene cannot be renamed.");
        }

        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Enter a scene name.", nameof(name));
        }

        lock (_sync)
        {
            var scenes = EnsureScenes(device.DeviceId);
            var sceneIndex = scenes.FindIndex(scene => string.Equals(scene.Id, sceneId, StringComparison.OrdinalIgnoreCase));
            if (sceneIndex < 0)
            {
                throw new ArgumentException("The scene no longer exists.", nameof(sceneId));
            }

            if (scenes.Any(scene => !string.Equals(scene.Id, sceneId, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(scene.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"A scene named '{trimmedName}' already exists.", nameof(name));
            }

            var renamedScene = scenes[sceneIndex] with { Name = trimmedName };
            scenes[sceneIndex] = renamedScene;
            Save();
            return renamedScene;
        }
    }

    public bool DeleteScene(MonitoredDeckDevice device, string sceneId)
    {
        if (string.Equals(sceneId, DeckScene.DefaultId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool activeSceneDeleted;
        lock (_sync)
        {
            var scenes = EnsureScenes(device.DeviceId);
            activeSceneDeleted = string.Equals(GetActiveSceneId(device.DeviceId), sceneId, StringComparison.OrdinalIgnoreCase);
            var removed = scenes.RemoveAll(scene => string.Equals(scene.Id, sceneId, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
            {
                return false;
            }

            foreach (var key in _bindings.Keys
                         .Where(key => string.Equals(key.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase)
                                       && string.Equals(key.SceneId, sceneId, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                _bindings.Remove(key);
                _actionParameters.Remove(key);
            }

            foreach (var key in _iconBindings.Keys
                         .Where(key => string.Equals(key.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase)
                                       && string.Equals(key.SceneId, sceneId, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                _iconBindings.Remove(key);
            }

            if (activeSceneDeleted)
            {
                _activeSceneByDevice[device.DeviceId] = DeckScene.DefaultId;
            }

            Save();
        }

        if (activeSceneDeleted)
        {
            ActiveSceneChanged?.Invoke(device);
        }

        return true;
    }

    public string GetActionId(MonitoredDeckDevice device, DeckControlType controlType, int controlIndex, DeckInputEventType triggerEventType)
    {
        lock (_sync)
        {
            var key = new DeckBindingKey(device.DeviceId, GetActiveSceneId(device.DeviceId), controlType, controlIndex, triggerEventType);
            if (_bindings.TryGetValue(key, out var action))
            {
                return action;
            }

            return GetDefaultActionId(device.ProfileName, controlType, controlIndex, triggerEventType);
        }
    }

    public void SetAction(MonitoredDeckDevice device, DeckControlType controlType, int controlIndex, DeckInputEventType triggerEventType, string actionId)
    {
        lock (_sync)
        {
            var key = new DeckBindingKey(device.DeviceId, GetActiveSceneId(device.DeviceId), controlType, controlIndex, triggerEventType);
            if (!_bindings.TryGetValue(key, out var previousActionId)
                || !string.Equals(previousActionId, actionId, StringComparison.OrdinalIgnoreCase))
            {
                _actionParameters.Remove(key);
            }

            _bindings[key] = actionId;
            Save();
        }
    }

    public void ApplyControlPreset(
        MonitoredDeckDevice device,
        DeckControlType controlType,
        int controlIndex,
        IReadOnlyList<DeckControlBindingUpdate> updates,
        DeckIconMode? iconMode)
    {
        lock (_sync)
        {
            var sceneId = GetActiveSceneId(device.DeviceId);
            var supportedTriggers = controlType == DeckControlType.Button
                ? new[] { DeckInputEventType.ButtonPress }
                : new[] { DeckInputEventType.EncoderTurn, DeckInputEventType.EncoderPress };

            foreach (var trigger in supportedTriggers)
            {
                var key = new DeckBindingKey(device.DeviceId, sceneId, controlType, controlIndex, trigger);
                _bindings[key] = NoAction.ActionId;
                _actionParameters.Remove(key);
            }

            foreach (var update in updates)
            {
                var key = new DeckBindingKey(device.DeviceId, sceneId, controlType, controlIndex, update.TriggerEventType);
                _bindings[key] = update.ActionId;
                if (update.Parameters is { Count: > 0 })
                {
                    _actionParameters[key] = new Dictionary<string, string>(update.Parameters, StringComparer.OrdinalIgnoreCase);
                }
            }

            if (controlType == DeckControlType.Button && iconMode is { } mode)
            {
                var iconKey = new DeckIconBindingKey(device.DeviceId, sceneId, controlIndex);
                if (mode == DeckIconMode.Static)
                {
                    _iconBindings.Remove(iconKey);
                }
                else
                {
                    _iconBindings[iconKey] = mode;
                }
            }

            Save();
        }
    }

    public void ResetControl(MonitoredDeckDevice device, DeckControlType controlType, int controlIndex)
    {
        lock (_sync)
        {
            var sceneId = GetActiveSceneId(device.DeviceId);
            var triggers = controlType == DeckControlType.Button
                ? new[] { DeckInputEventType.ButtonPress }
                : new[] { DeckInputEventType.EncoderTurn, DeckInputEventType.EncoderPress };

            foreach (var trigger in triggers)
            {
                var key = new DeckBindingKey(device.DeviceId, sceneId, controlType, controlIndex, trigger);
                _bindings[key] = NoAction.ActionId;
                _actionParameters.Remove(key);
            }

            if (controlType == DeckControlType.Button)
            {
                _iconBindings.Remove(new DeckIconBindingKey(device.DeviceId, sceneId, controlIndex));
            }

            Save();
        }
    }

    public void MoveControl(MonitoredDeckDevice device, DeckControlType controlType, int sourceIndex, int targetIndex)
    {
        if (sourceIndex == targetIndex)
        {
            return;
        }

        lock (_sync)
        {
            var sceneId = GetActiveSceneId(device.DeviceId);
            var triggers = controlType == DeckControlType.Button
                ? new[] { DeckInputEventType.ButtonPress }
                : new[] { DeckInputEventType.EncoderTurn, DeckInputEventType.EncoderPress };

            foreach (var trigger in triggers)
            {
                var sourceKey = new DeckBindingKey(device.DeviceId, sceneId, controlType, sourceIndex, trigger);
                var targetKey = new DeckBindingKey(device.DeviceId, sceneId, controlType, targetIndex, trigger);
                _bindings[targetKey] = _bindings.TryGetValue(sourceKey, out var actionId)
                    ? actionId
                    : GetDefaultActionId(device.ProfileName, controlType, sourceIndex, trigger);

                if (_actionParameters.TryGetValue(sourceKey, out var parameters))
                {
                    _actionParameters[targetKey] = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    _actionParameters.Remove(targetKey);
                }

                _bindings[sourceKey] = NoAction.ActionId;
                _actionParameters.Remove(sourceKey);
            }

            if (controlType == DeckControlType.Button)
            {
                var sourceIconKey = new DeckIconBindingKey(device.DeviceId, sceneId, sourceIndex);
                var targetIconKey = new DeckIconBindingKey(device.DeviceId, sceneId, targetIndex);
                if (_iconBindings.TryGetValue(sourceIconKey, out var iconMode))
                {
                    _iconBindings[targetIconKey] = iconMode;
                }
                else
                {
                    _iconBindings.Remove(targetIconKey);
                }

                _iconBindings.Remove(sourceIconKey);
            }

            Save();
        }
    }

    public IReadOnlyDictionary<string, string> GetActionParameters(
        MonitoredDeckDevice device,
        DeckControlType controlType,
        int controlIndex,
        DeckInputEventType triggerEventType)
    {
        lock (_sync)
        {
            var key = new DeckBindingKey(device.DeviceId, GetActiveSceneId(device.DeviceId), controlType, controlIndex, triggerEventType);
            return _actionParameters.TryGetValue(key, out var parameters)
                ? new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetActionParameter(
        MonitoredDeckDevice device,
        DeckControlType controlType,
        int controlIndex,
        DeckInputEventType triggerEventType,
        string name,
        string? value)
    {
        lock (_sync)
        {
            var key = new DeckBindingKey(device.DeviceId, GetActiveSceneId(device.DeviceId), controlType, controlIndex, triggerEventType);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (_actionParameters.TryGetValue(key, out var existing))
                {
                    existing.Remove(name);
                    if (existing.Count == 0)
                    {
                        _actionParameters.Remove(key);
                    }
                }
            }
            else
            {
                if (!_actionParameters.TryGetValue(key, out var parameters))
                {
                    parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _actionParameters[key] = parameters;
                }

                parameters[name] = value;
            }

            Save();
        }
    }

    public DeckIconMode GetIconMode(MonitoredDeckDevice device, int controlIndex)
    {
        lock (_sync)
        {
            var key = new DeckIconBindingKey(device.DeviceId, GetActiveSceneId(device.DeviceId), controlIndex);
            return _iconBindings.GetValueOrDefault(key, DeckIconMode.Static);
        }
    }

    public void SetIconMode(MonitoredDeckDevice device, int controlIndex, DeckIconMode mode)
    {
        lock (_sync)
        {
            var key = new DeckIconBindingKey(device.DeviceId, GetActiveSceneId(device.DeviceId), controlIndex);
            if (mode == DeckIconMode.Static)
            {
                _iconBindings.Remove(key);
            }
            else
            {
                _iconBindings[key] = mode;
            }

            Save();
        }
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
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                LoadLegacyBindings(json);
                return;
            }

            if (document.RootElement.TryGetProperty("Devices", out _))
            {
                var configuration = JsonSerializer.Deserialize<StoredDeckConfiguration>(json, _jsonOptions);
                if (configuration is not null)
                {
                    LoadConfiguration(configuration);
                }

                return;
            }

            var legacyConfiguration = JsonSerializer.Deserialize<LegacyStoredDeckConfiguration>(json, _jsonOptions);
            if (legacyConfiguration is not null)
            {
                LoadLegacyConfiguration(legacyConfiguration);
            }
        }
        catch
        {
            _bindings.Clear();
            _actionParameters.Clear();
            _iconBindings.Clear();
            _scenesByDevice.Clear();
            _activeSceneByDevice.Clear();
        }
    }

    private void LoadLegacyBindings(string json)
    {
        var bindings = JsonSerializer.Deserialize<List<LegacyStoredDeckBinding>>(json, _jsonOptions) ?? [];
        foreach (var binding in bindings)
        {
            EnsureScenes(binding.DeviceId);
            AddBinding(binding, DeckScene.DefaultId);
        }
    }

    private void LoadLegacyConfiguration(LegacyStoredDeckConfiguration configuration)
    {
        foreach (var storedScene in configuration.Scenes)
        {
            if (string.IsNullOrWhiteSpace(storedScene.DeviceId)
                || string.IsNullOrWhiteSpace(storedScene.SceneId)
                || string.IsNullOrWhiteSpace(storedScene.Name))
            {
                continue;
            }

            var scenes = EnsureScenes(storedScene.DeviceId);
            if (string.Equals(storedScene.SceneId, DeckScene.DefaultId, StringComparison.OrdinalIgnoreCase))
            {
                if (storedScene.IsActive)
                {
                    _activeSceneByDevice[storedScene.DeviceId] = DeckScene.DefaultId;
                }

                continue;
            }

            if (scenes.Any(scene => string.Equals(scene.Id, storedScene.SceneId, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(scene.Name, storedScene.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            scenes.Add(new DeckScene(storedScene.SceneId, storedScene.Name));
            if (storedScene.IsActive)
            {
                _activeSceneByDevice[storedScene.DeviceId] = storedScene.SceneId;
            }
        }

        foreach (var binding in configuration.Bindings)
        {
            var sceneId = string.IsNullOrWhiteSpace(binding.SceneId) ? DeckScene.DefaultId : binding.SceneId;
            var scenes = EnsureScenes(binding.DeviceId);
            if (scenes.Any(scene => string.Equals(scene.Id, sceneId, StringComparison.OrdinalIgnoreCase)))
            {
                AddBinding(binding, sceneId);
            }
        }
    }

    private void LoadConfiguration(StoredDeckConfiguration configuration)
    {
        foreach (var storedDevice in configuration.Devices)
        {
            if (string.IsNullOrWhiteSpace(storedDevice.DeviceId))
            {
                continue;
            }

            var scenes = EnsureScenes(storedDevice.DeviceId);
            foreach (var storedScene in storedDevice.Scenes)
            {
                if (string.IsNullOrWhiteSpace(storedScene.SceneId) || string.IsNullOrWhiteSpace(storedScene.Name))
                {
                    continue;
                }

                if (!string.Equals(storedScene.SceneId, DeckScene.DefaultId, StringComparison.OrdinalIgnoreCase)
                    && !scenes.Any(scene => string.Equals(scene.Id, storedScene.SceneId, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(scene.Name, storedScene.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    scenes.Add(new DeckScene(storedScene.SceneId, storedScene.Name));
                }

                if (!scenes.Any(scene => string.Equals(scene.Id, storedScene.SceneId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (var binding in storedScene.Bindings)
                {
                    AddBinding(storedDevice.DeviceId, storedScene.SceneId, binding);
                }

                foreach (var icon in storedScene.Icons ?? [])
                {
                    if (icon.ControlIndex >= 0 && Enum.IsDefined(icon.Mode) && icon.Mode != DeckIconMode.Static)
                    {
                        _iconBindings[new DeckIconBindingKey(storedDevice.DeviceId, storedScene.SceneId, icon.ControlIndex)] = icon.Mode;
                    }
                }
            }

            if (scenes.Any(scene => string.Equals(scene.Id, storedDevice.ActiveSceneId, StringComparison.OrdinalIgnoreCase)))
            {
                _activeSceneByDevice[storedDevice.DeviceId] = storedDevice.ActiveSceneId;
            }
        }
    }

    private void AddBinding(LegacyStoredDeckBinding binding, string sceneId)
    {
        var key = new DeckBindingKey(binding.DeviceId, sceneId, binding.ControlType, binding.ControlIndex, binding.TriggerEventType);
        _bindings[key] = GetActionId(binding);
    }

    private void AddBinding(string deviceId, string sceneId, StoredDeckBinding binding)
    {
        var key = new DeckBindingKey(deviceId, sceneId, binding.ControlType, binding.ControlIndex, binding.TriggerEventType);
        _bindings[key] = binding.ActionId;
        if (binding.Parameters is { Count: > 0 })
        {
            _actionParameters[key] = new Dictionary<string, string>(binding.Parameters, StringComparer.OrdinalIgnoreCase);
        }
    }

    private List<DeckScene> EnsureScenes(string deviceId)
    {
        if (_scenesByDevice.TryGetValue(deviceId, out var scenes))
        {
            return scenes;
        }

        scenes = [new DeckScene(DeckScene.DefaultId, "Default")];
        _scenesByDevice[deviceId] = scenes;
        _activeSceneByDevice[deviceId] = DeckScene.DefaultId;
        return scenes;
    }

    private string GetActiveSceneId(string deviceId)
    {
        var scenes = EnsureScenes(deviceId);
        if (_activeSceneByDevice.TryGetValue(deviceId, out var activeId)
            && scenes.Any(scene => string.Equals(scene.Id, activeId, StringComparison.OrdinalIgnoreCase)))
        {
            return activeId;
        }

        _activeSceneByDevice[deviceId] = DeckScene.DefaultId;
        return DeckScene.DefaultId;
    }

    private void Save()
    {
        var devices = _scenesByDevice
            .OrderBy(device => device.Key, StringComparer.OrdinalIgnoreCase)
            .Select(device => new StoredDeckDevice(
                device.Key,
                GetActiveSceneId(device.Key),
                device.Value.Select(scene => new StoredDeckScene(
                        scene.Id,
                        scene.Name,
                        _bindings
                            .Where(entry => string.Equals(entry.Key.DeviceId, device.Key, StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(entry.Key.SceneId, scene.Id, StringComparison.OrdinalIgnoreCase))
                            .Select(entry => new StoredDeckBinding(
                                entry.Key.ControlType,
                                entry.Key.ControlIndex,
                                entry.Key.TriggerEventType,
                                entry.Value,
                                _actionParameters.TryGetValue(entry.Key, out var parameters)
                                    ? parameters
                                    : null))
                            .OrderBy(binding => binding.ControlType)
                            .ThenBy(binding => binding.ControlIndex)
                            .ThenBy(binding => binding.TriggerEventType)
                            .ToList(),
                        _iconBindings
                            .Where(entry => string.Equals(entry.Key.DeviceId, device.Key, StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(entry.Key.SceneId, scene.Id, StringComparison.OrdinalIgnoreCase))
                            .Select(entry => new StoredDeckIconBinding(entry.Key.ControlIndex, entry.Value))
                            .OrderBy(icon => icon.ControlIndex)
                            .ToList()))
                    .ToList()))
            .ToList();

        var configuration = new StoredDeckConfiguration(CurrentVersion, devices);
        var json = JsonSerializer.Serialize(configuration, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static string GetDefaultActionId(string profileName, DeckControlType controlType, int controlIndex, DeckInputEventType triggerEventType)
    {
        if (string.Equals(profileName, "AJAZZ AKP03E", StringComparison.OrdinalIgnoreCase))
        {
            if (controlType == DeckControlType.Encoder && controlIndex == 1 && triggerEventType == DeckInputEventType.EncoderTurn)
            {
                return VolumeAdjustAction.ActionId;
            }

            if (controlType == DeckControlType.Encoder && controlIndex == 1 && triggerEventType == DeckInputEventType.EncoderPress)
            {
                return ToggleMuteAction.ActionId;
            }
        }

        return NoAction.ActionId;
    }

    private static string GetActionId(LegacyStoredDeckBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.ActionId))
        {
            return binding.ActionId;
        }

        return binding.ActionKind switch
        {
            LegacyDeckBindingActionKind.VolumeAdjust => VolumeAdjustAction.ActionId,
            LegacyDeckBindingActionKind.ToggleMute => ToggleMuteAction.ActionId,
            _ => NoAction.ActionId,
        };
    }
}
