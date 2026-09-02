using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings.Core;

internal sealed class NoAction : IDeckAction
{
    public const string ActionId = "core.none";

    public string Id => ActionId;

    public string DisplayName => "None";

    public bool Supports(DeckInputEventType triggerEventType)
    {
        return triggerEventType is DeckInputEventType.EncoderTurn
            or DeckInputEventType.EncoderPress
            or DeckInputEventType.ButtonPress;
    }

    public void Execute(DeckActionContext context)
    {
    }
}
