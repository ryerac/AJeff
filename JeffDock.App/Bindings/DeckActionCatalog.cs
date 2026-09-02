using JeffDock.App.Bindings.Core;
using JeffDock.App.Bindings.Scenes;
using JeffDock.Core.Audio;
using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal sealed class DeckActionCatalog : IDisposable
{
    private readonly WindowsVolumeController _volumeController = new();
    private readonly IReadOnlyList<IDeckAction> _actions;
    private readonly IReadOnlyDictionary<string, IDeckAction> _actionsById;

    public DeckActionCatalog(DeckBindingStore bindingStore)
    {
        _actions =
        [
            new NoAction(),
            new VolumeAdjustAction(_volumeController),
            new ToggleMuteAction(_volumeController),
            new SceneCycleAction(bindingStore, SceneCycleAction.NextActionId, "Next Scene", 1),
            new SceneCycleAction(bindingStore, SceneCycleAction.PreviousActionId, "Previous Scene", -1),
        ];

        _actionsById = _actions.ToDictionary(action => action.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IDeckAction> GetActionsFor(DeckInputEventType triggerEventType)
    {
        return _actions.Where(action => action.Supports(triggerEventType)).ToList();
    }

    public IDeckAction GetAction(string actionId)
    {
        return _actionsById.GetValueOrDefault(actionId) ?? _actionsById[NoAction.ActionId];
    }

    public void Dispose()
    {
        _volumeController.Dispose();
    }
}
