using System.Windows.Input;
using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings.Core;

internal sealed class MediaKeyAction(string id, string displayName, Key key) : IDeckAction
{
    public const string PlayPauseActionId = "core.media.play-pause";
    public const string NextTrackActionId = "core.media.next-track";
    public const string PreviousTrackActionId = "core.media.previous-track";

    private const int PressCooldownMilliseconds = 1000;
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _lastPressByControl = new(StringComparer.OrdinalIgnoreCase);

    public string Id => id;
    public string DisplayName => displayName;
    public DeckActionGroup Group => DeckActionGroups.Media;

    public bool Supports(DeckInputEventType triggerEventType) =>
        triggerEventType is DeckInputEventType.ButtonPress or DeckInputEventType.EncoderPress;

    public void Execute(DeckActionContext context)
    {
        if (ShouldDebounce(context))
        {
            return;
        }

        WindowsKeyboardSender.Send(new KeyboardShortcut(KeyboardShortcutModifiers.None, key));
    }

    private bool ShouldDebounce(DeckActionContext context)
    {
        var evt = context.InputEvent;
        var control = $"{context.Device.DeviceId}|{evt.Type}|{evt.ControlIndex}";
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (_lastPressByControl.TryGetValue(control, out var last)
                && (now - last).TotalMilliseconds < PressCooldownMilliseconds)
            {
                return true;
            }

            _lastPressByControl[control] = now;
            return false;
        }
    }
}
