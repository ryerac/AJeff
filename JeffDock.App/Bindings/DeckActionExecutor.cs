using JeffDock.Core.Audio;
using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal sealed class DeckActionExecutor : IDisposable
{
    private readonly WindowsVolumeController _volumeController = new();
    private readonly Dictionary<string, DateTime> _lastPressByKey = new(StringComparer.OrdinalIgnoreCase);

    public void Execute(MonitoredDeckDevice device, DeckInputEvent evt, DeckBindingStore bindingStore)
    {
        var controlType = GetControlType(evt.Type);
        if (controlType is null)
        {
            return;
        }

        var action = bindingStore.GetAction(device, controlType.Value, evt.ControlIndex, evt.Type);
        switch (action)
        {
            case DeckBindingActionKind.None:
                return;
            case DeckBindingActionKind.VolumeAdjust when evt.Type == DeckInputEventType.EncoderTurn:
                _volumeController.NudgeVolume(evt.Direction);
                return;
            case DeckBindingActionKind.ToggleMute when evt.Type is DeckInputEventType.EncoderPress or DeckInputEventType.ButtonPress:
                if (ShouldDebounce(device.DeviceId, controlType.Value, evt.ControlIndex, evt.Type, 250))
                {
                    return;
                }

                _volumeController.ToggleMute();
                return;
        }
    }

    public void Dispose()
    {
        _volumeController.Dispose();
    }

    private bool ShouldDebounce(string deviceId, DeckControlType controlType, int controlIndex, DeckInputEventType triggerEventType, int milliseconds)
    {
        var key = $"{deviceId}|{controlType}|{controlIndex}|{triggerEventType}";
        var now = DateTime.UtcNow;
        if (_lastPressByKey.TryGetValue(key, out var last) && (now - last).TotalMilliseconds < milliseconds)
        {
            return true;
        }

        _lastPressByKey[key] = now;
        return false;
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
