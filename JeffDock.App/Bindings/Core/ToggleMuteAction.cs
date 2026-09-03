using JeffDock.Core.Audio;
using JeffDock.Core.Deck;
using JeffDock.App.Bindings.State;

namespace JeffDock.App.Bindings.Core;

internal sealed class ToggleMuteAction(WindowsVolumeController volumeController) : IDeckAction
{
    public const string ActionId = "core.audio.toggle-mute";

    private readonly Dictionary<string, DateTime> _lastPressByKey = new(StringComparer.OrdinalIgnoreCase);

    public string Id => ActionId;

    public string DisplayName => "Toggle Mute";

    public DeckActionGroup Group => DeckActionGroups.Audio;

    public DeckActionVisualDefinition Visual { get; } = new(
        OutputMuteStateSource.SourceId,
        [
            new("muted", "Muted", "core/audio/toggle-mute_muted"),
            new("unmuted", "Not Muted", "core/audio/toggle-mute_unmuted"),
        ]);

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

        volumeController.ToggleMute();
    }

    private bool ShouldDebounce(DeckActionContext context, int milliseconds)
    {
        var evt = context.InputEvent;
        var key = $"{context.Device.DeviceId}|{evt.Type}|{evt.ControlIndex}";
        var now = DateTime.UtcNow;
        if (_lastPressByKey.TryGetValue(key, out var last) && (now - last).TotalMilliseconds < milliseconds)
        {
            return true;
        }

        _lastPressByKey[key] = now;
        return false;
    }
}
