using JeffDock.Core.Audio;
using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings.Core;

internal sealed class ToggleMicrophoneMuteAction(WindowsVolumeController volumeController) : IDeckAction
{
    public const string ActionId = "core.audio.toggle-microphone-mute";

    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _lastPressByKey = new(StringComparer.OrdinalIgnoreCase);

    public string Id => ActionId;

    public string DisplayName => "Toggle Microphone Mute";

    public DeckActionGroup Group => DeckActionGroups.Audio;

    public bool Supports(DeckInputEventType triggerEventType)
    {
        return triggerEventType is DeckInputEventType.EncoderPress or DeckInputEventType.ButtonPress;
    }

    public void Execute(DeckActionContext context)
    {
        if (ShouldDebounce(context, 250))
        {
            return;
        }

        volumeController.ToggleMicrophoneMute();
    }

    private bool ShouldDebounce(DeckActionContext context, int milliseconds)
    {
        var evt = context.InputEvent;
        var key = $"{context.Device.DeviceId}|{evt.Type}|{evt.ControlIndex}";

        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (_lastPressByKey.TryGetValue(key, out var last) && (now - last).TotalMilliseconds < milliseconds)
            {
                return true;
            }

            _lastPressByKey[key] = now;
            return false;
        }
    }
}
