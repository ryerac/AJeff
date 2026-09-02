using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal enum DeckBindingActionKind
{
    None = 0,
    VolumeAdjust = 1,
    ToggleMute = 2,
}

internal readonly record struct DeckBindingKey(
    string DeviceId,
    DeckControlType ControlType,
    int ControlIndex,
    DeckInputEventType TriggerEventType
);

internal sealed record StoredDeckBinding(
    string DeviceId,
    DeckControlType ControlType,
    int ControlIndex,
    DeckInputEventType TriggerEventType,
    DeckBindingActionKind ActionKind
);
