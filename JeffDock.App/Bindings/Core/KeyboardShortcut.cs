using System.Windows.Input;

namespace JeffDock.App.Bindings.Core;

[Flags]
internal enum KeyboardShortcutModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8,
}

internal sealed record KeyboardShortcut(KeyboardShortcutModifiers Modifiers, Key Key)
{
    public string Serialize()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyboardShortcutModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyboardShortcutModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyboardShortcutModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyboardShortcutModifiers.Windows)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }

    public override string ToString() => Serialize();

    public static KeyboardShortcut? FromKey(Key key, ModifierKeys modifiers)
    {
        if (IsModifierKey(key) || KeyInterop.VirtualKeyFromKey(key) == 0)
        {
            return null;
        }

        var shortcutModifiers = KeyboardShortcutModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) shortcutModifiers |= KeyboardShortcutModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) shortcutModifiers |= KeyboardShortcutModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Alt)) shortcutModifiers |= KeyboardShortcutModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Windows)) shortcutModifiers |= KeyboardShortcutModifiers.Windows;
        return new KeyboardShortcut(shortcutModifiers, key);
    }

    public static bool TryParse(string? value, out KeyboardShortcut? shortcut)
    {
        shortcut = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key) || IsModifierKey(key))
        {
            return false;
        }

        var modifiers = KeyboardShortcutModifiers.None;
        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => KeyboardShortcutModifiers.Control,
                "shift" => KeyboardShortcutModifiers.Shift,
                "alt" => KeyboardShortcutModifiers.Alt,
                "win" or "windows" => KeyboardShortcutModifiers.Windows,
                _ => (KeyboardShortcutModifiers)(-1),
            };
        }

        if ((int)modifiers < 0 || KeyInterop.VirtualKeyFromKey(key) == 0)
        {
            return false;
        }

        shortcut = new KeyboardShortcut(modifiers, key);
        return true;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
    }
}
