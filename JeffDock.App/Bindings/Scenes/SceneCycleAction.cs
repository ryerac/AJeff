using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings.Scenes;

internal sealed class SceneCycleAction(
    DeckBindingStore bindingStore,
    string id,
    string displayName,
    int direction) : IDeckAction
{
    public const string NextActionId = "scene.next";
    public const string PreviousActionId = "scene.previous";

    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _lastPressByDevice = new(StringComparer.OrdinalIgnoreCase);

    public string Id => id;

    public string DisplayName => displayName;

    public bool Supports(DeckInputEventType triggerEventType)
    {
        return triggerEventType == DeckInputEventType.ButtonPress;
    }

    public void Execute(DeckActionContext context)
    {
        if (ShouldDebounce(context.Device.DeviceId, 250))
        {
            return;
        }

        bindingStore.CycleScene(context.Device, direction);
    }

    private bool ShouldDebounce(string deviceId, int milliseconds)
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (_lastPressByDevice.TryGetValue(deviceId, out var last) && (now - last).TotalMilliseconds < milliseconds)
            {
                return true;
            }

            _lastPressByDevice[deviceId] = now;
            return false;
        }
    }
}
