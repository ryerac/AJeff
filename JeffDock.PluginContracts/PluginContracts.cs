using JeffDock.Core.Deck;

namespace JeffDock.PluginContracts;

public interface IJeffDockPlugin
{
    string Id { get; }
    string DisplayName { get; }
    Version Version { get; }
    void Register(IJeffDockPluginRegistry registry);
}

public interface IJeffDockPluginRegistry
{
    IPluginNotifications Notifications { get; }
    void AddAction(IPluginDeckAction action);
    void AddStateSource(IPluginDeckStateSource stateSource);
    void AddPresetJson(string json);
    IPluginSettings AddSettings(string pluginId, IReadOnlyList<PluginSettingDefinition> definitions);
}

public interface IPluginNotifications
{
    void ShowAlert(string title, string message);
}

public enum PluginSettingType
{
    String,
    Integer,
    Decimal,
    Boolean,
    Choice,
    Color,
}

public sealed record PluginSettingChoice(string Value, string DisplayName);

public sealed record PluginSettingDefinition(
    string Key,
    string DisplayName,
    string? Description,
    PluginSettingType Type,
    string DefaultValue,
    decimal? Minimum = null,
    decimal? Maximum = null,
    string? Suffix = null,
    IReadOnlyList<PluginSettingChoice>? Choices = null);

public sealed class PluginSettingChangedEventArgs(string key, string value) : EventArgs
{
    public string Key { get; } = key;
    public string Value { get; } = value;
}

public interface IPluginSettings
{
    IReadOnlyList<PluginSettingDefinition> Definitions { get; }
    string GetValue(string key);
    void SetValue(string key, string value);
    event EventHandler<PluginSettingChangedEventArgs>? Changed;
}

public interface IPluginDeckAction
{
    string Id { get; }
    string DisplayName { get; }
    PluginActionGroup Group { get; }
    PluginActionVisual? Visual => null;
    IReadOnlyList<PluginSettingDefinition> Parameters => [];
    bool Supports(DeckInputEventType triggerEventType);
    void Execute(PluginActionContext context);
}

public readonly record struct PluginActionContext(
    MonitoredDeckDevice Device,
    string SceneId,
    DeckInputEvent InputEvent,
    IReadOnlyDictionary<string, string> Parameters);

public readonly record struct PluginVisualContext(
    string DeviceId,
    string SceneId,
    int ControlIndex,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record PluginActionGroup(string Id, string DisplayName);
public sealed record PluginActionVisual(
    string StateSourceId,
    IReadOnlyList<PluginActionVisualState> States,
    bool IsImageManaged = false);
public sealed record PluginActionVisualState(string Id, string DisplayName, string? DefaultIconId = null);

public interface IPluginDeckStateSource : IDisposable
{
    string Id { get; }
    string CurrentState { get; }
    byte[]? CurrentImageBytes => null;
    string GetCurrentState(PluginVisualContext context) => CurrentState;
    byte[]? GetCurrentImageBytes(PluginVisualContext context) => CurrentImageBytes;
    event EventHandler<string>? StateChanged;
    void Start();
}
