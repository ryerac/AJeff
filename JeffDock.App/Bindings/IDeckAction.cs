using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal interface IDeckAction
{
    string Id { get; }

    string DisplayName { get; }

    DeckActionGroup Group { get; }

    DeckActionVisualDefinition? Visual => null;

    bool Supports(DeckInputEventType triggerEventType);

    void Execute(DeckActionContext context);
}

internal readonly record struct DeckActionContext(
    MonitoredDeckDevice Device,
    DeckInputEvent InputEvent
);

internal sealed record DeckActionVisualDefinition(
    string StateSourceId,
    IReadOnlyList<DeckActionVisualState> States);

internal sealed record DeckActionVisualState(
    string Id,
    string DisplayName,
    string? DefaultIconId = null);
