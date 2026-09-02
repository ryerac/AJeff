using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal interface IDeckAction
{
    string Id { get; }

    string DisplayName { get; }

    DeckActionGroup Group { get; }

    bool Supports(DeckInputEventType triggerEventType);

    void Execute(DeckActionContext context);
}

internal readonly record struct DeckActionContext(
    MonitoredDeckDevice Device,
    DeckInputEvent InputEvent
);
