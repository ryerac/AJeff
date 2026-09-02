using JeffDock.App.Bindings.Core;
using JeffDock.App.Bindings.Scenes;
using JeffDock.App.Bindings.State;
using JeffDock.Core.Audio;
using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal sealed class DeckActionCatalog : IDisposable
{
    private readonly WindowsVolumeController _volumeController = new();
    private readonly IReadOnlyList<IDeckAction> _actions;
    private readonly IReadOnlyDictionary<string, IDeckAction> _actionsById;

    public DeckStateCatalog StateCatalog { get; }

    public DeckActionCatalog(DeckBindingStore bindingStore)
    {
        StateCatalog = new DeckStateCatalog(_volumeController);
        _actions =
        [
            new NoAction(),
            new VolumeAdjustAction(_volumeController),
            new ToggleMuteAction(_volumeController),
            new ToggleMicrophoneMuteAction(_volumeController),
            new VolumeStepAction(_volumeController, VolumeStepAction.UpActionId, "Volume Up", 1),
            new VolumeStepAction(_volumeController, VolumeStepAction.DownActionId, "Volume Down", -1),
            new SceneCycleAction(bindingStore, SceneCycleAction.NextActionId, "Next Scene", 1),
            new SceneCycleAction(bindingStore, SceneCycleAction.PreviousActionId, "Previous Scene", -1),
        ];

        _actionsById = _actions.ToDictionary(action => action.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IDeckAction> GetActionsFor(DeckInputEventType triggerEventType)
    {
        return _actions.Where(action => action.Supports(triggerEventType)).ToList();
    }

    public IReadOnlyList<DeckActionGroup> GetGroupsFor(DeckInputEventType triggerEventType)
    {
        return _actions
            .Where(action => action.Supports(triggerEventType))
            .Select(action => action.Group)
            .DistinctBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<IDeckAction> GetActionsFor(DeckInputEventType triggerEventType, string groupId)
    {
        return _actions
            .Where(action => action.Supports(triggerEventType)
                             && string.Equals(action.Group.Id, groupId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IDeckAction GetAction(string actionId)
    {
        return _actionsById.GetValueOrDefault(actionId) ?? _actionsById[NoAction.ActionId];
    }

    public void Dispose()
    {
        StateCatalog.Dispose();
        _volumeController.Dispose();
    }
}
