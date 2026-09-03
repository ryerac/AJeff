using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings.Scenes;

internal sealed class HomeSceneAction(DeckBindingStore bindingStore) : IDeckAction
{
    public const string ActionId = "scene.home";
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _lastPressByDevice = new(StringComparer.OrdinalIgnoreCase);

    public string Id => ActionId;
    public string DisplayName => "Home Scene";
    public DeckActionGroup Group => DeckActionGroups.Scenes;
    public bool Supports(DeckInputEventType triggerEventType) =>
        triggerEventType is DeckInputEventType.ButtonPress or DeckInputEventType.EncoderPress;

    public void Execute(DeckActionContext context)
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (_lastPressByDevice.TryGetValue(context.Device.DeviceId, out var last)
                && now - last < TimeSpan.FromSeconds(1))
            {
                return;
            }
            _lastPressByDevice[context.Device.DeviceId] = now;
        }

        bindingStore.SetActiveScene(context.Device, DeckScene.DefaultId);
    }
}
