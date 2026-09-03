using JeffDock.App.Bindings;
using JeffDock.App.Bindings.State;
using JeffDock.PluginContracts;
using JeffDock.Core.Deck;

namespace JeffDock.App.Plugins;

internal sealed class PluginActionAdapter(IPluginDeckAction action) : IDeckAction
{
    public string Id => action.Id;
    public string DisplayName => action.DisplayName;
    public DeckActionGroup Group { get; } = new(action.Group.Id, action.Group.DisplayName);
    public DeckActionVisualDefinition? Visual { get; } = action.Visual is null ? null : new(
        action.Visual.StateSourceId,
        action.Visual.States.Select(state => new DeckActionVisualState(state.Id, state.DisplayName, state.DefaultIconId)).ToList(),
        action.Visual.IsImageManaged);
    public bool Supports(DeckInputEventType triggerEventType) => action.Supports(triggerEventType);
    public void Execute(DeckActionContext context) => action.Execute(new(
        context.Device, context.InputEvent, context.Parameters));
}

internal sealed class PluginStateSourceAdapter : IDeckStateSource
{
    private readonly IPluginDeckStateSource _source;

    public PluginStateSourceAdapter(IPluginDeckStateSource source)
    {
        _source = source;
        _source.StateChanged += OnPluginStateChanged;
    }

    public string Id => _source.Id;
    public string CurrentState => _source.CurrentState;
    public byte[]? CurrentImageBytes => _source.CurrentImageBytes;
    public event EventHandler<string>? StateChanged;
    public void Start() => _source.Start();

    private void OnPluginStateChanged(object? sender, string state) => StateChanged?.Invoke(this, state);

    public void Dispose()
    {
        _source.StateChanged -= OnPluginStateChanged;
        _source.Dispose();
    }
}
