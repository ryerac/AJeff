using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal sealed class DeckActionExecutor(DeckActionCatalog actionCatalog)
{
    public void Execute(MonitoredDeckDevice device, DeckInputEvent evt, DeckBindingStore bindingStore)
    {
        var controlType = GetControlType(evt.Type);
        if (controlType is null)
        {
            return;
        }

        var actionId = bindingStore.GetActionId(device, controlType.Value, evt.ControlIndex, evt.Type);
        var action = actionCatalog.GetAction(actionId);
        if (action.Supports(evt.Type))
        {
            var parameters = bindingStore.GetActionParameters(device, controlType.Value, evt.ControlIndex, evt.Type);
            action.Execute(new DeckActionContext(device, evt, parameters));
        }
    }

    private static DeckControlType? GetControlType(DeckInputEventType eventType)
    {
        return eventType switch
        {
            DeckInputEventType.ButtonPress => DeckControlType.Button,
            DeckInputEventType.EncoderPress => DeckControlType.Encoder,
            DeckInputEventType.EncoderTurn => DeckControlType.Encoder,
            _ => null,
        };
    }
}
