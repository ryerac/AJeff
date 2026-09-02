using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal enum LegacyDeckBindingActionKind
{
    None = 0,
    VolumeAdjust = 1,
    ToggleMute = 2,
}

internal readonly record struct DeckBindingKey(
    string DeviceId,
    string SceneId,
    DeckControlType ControlType,
    int ControlIndex,
    DeckInputEventType TriggerEventType
);

internal enum DeckIconMode
{
    Static = 0,
    Dynamic = 1,
}

internal readonly record struct DeckIconBindingKey(
    string DeviceId,
    string SceneId,
    int ControlIndex
);

internal sealed record DeckScene(string Id, string Name)
{
    public const string DefaultId = "default";

    public bool IsDefault => string.Equals(Id, DefaultId, StringComparison.OrdinalIgnoreCase);
}

internal sealed record StoredDeckBinding(
    DeckControlType ControlType,
    int ControlIndex,
    DeckInputEventType TriggerEventType,
    string ActionId,
    IReadOnlyDictionary<string, string>? Parameters = null
);

internal sealed record StoredDeckScene(
    string SceneId,
    string Name,
    IReadOnlyList<StoredDeckBinding> Bindings,
    IReadOnlyList<StoredDeckIconBinding>? Icons = null
);

internal sealed record StoredDeckIconBinding(
    int ControlIndex,
    DeckIconMode Mode
);

internal sealed record StoredDeckDevice(
    string DeviceId,
    string ActiveSceneId,
    IReadOnlyList<StoredDeckScene> Scenes
);

internal sealed record StoredDeckConfiguration(
    int Version,
    IReadOnlyList<StoredDeckDevice> Devices
);

internal sealed record LegacyStoredDeckBinding(
    string DeviceId,
    DeckControlType ControlType,
    int ControlIndex,
    DeckInputEventType TriggerEventType,
    string? ActionId = null,
    LegacyDeckBindingActionKind? ActionKind = null,
    string? SceneId = null
);

internal sealed record LegacyStoredDeckScene(
    string DeviceId,
    string SceneId,
    string Name,
    bool IsActive
);

internal sealed record LegacyStoredDeckConfiguration(
    int Version,
    IReadOnlyList<LegacyStoredDeckScene> Scenes,
    IReadOnlyList<LegacyStoredDeckBinding> Bindings
);
