using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings.Core;

internal sealed class KeyboardShortcutAction : IDeckAction
{
    public const string ActionId = "core.keyboard.shortcut";
    public const string ShortcutParameter = "shortcut";

    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _lastPressByKey = new(StringComparer.OrdinalIgnoreCase);

    public string Id => ActionId;

    public string DisplayName => "Keyboard Shortcut";

    public DeckActionGroup Group => DeckActionGroups.Keyboard;

    public bool Supports(DeckInputEventType triggerEventType)
    {
        return triggerEventType is DeckInputEventType.ButtonPress or DeckInputEventType.EncoderPress;
    }

    public void Execute(DeckActionContext context)
    {
        if (!context.Parameters.TryGetValue(ShortcutParameter, out var value)
            || !KeyboardShortcut.TryParse(value, out var shortcut)
            || shortcut is null
            || ShouldDebounce(context, 150))
        {
            return;
        }

        WindowsKeyboardSender.Send(shortcut);
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
