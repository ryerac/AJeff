using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace JeffDock.App.Bindings.Core;

internal static class WindowsKeyboardSender
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventExtendedKey = 0x0001;

    public static void Send(KeyboardShortcut shortcut)
    {
        var keys = new List<Key>();
        if (shortcut.Modifiers.HasFlag(KeyboardShortcutModifiers.Control)) keys.Add(Key.LeftCtrl);
        if (shortcut.Modifiers.HasFlag(KeyboardShortcutModifiers.Shift)) keys.Add(Key.LeftShift);
        if (shortcut.Modifiers.HasFlag(KeyboardShortcutModifiers.Alt)) keys.Add(Key.LeftAlt);
        if (shortcut.Modifiers.HasFlag(KeyboardShortcutModifiers.Windows)) keys.Add(Key.LWin);
        keys.Add(shortcut.Key);

        var inputs = keys
            .Select(key => CreateInput(key, keyUp: false))
            .Concat(keys.AsEnumerable().Reverse().Select(key => CreateInput(key, keyUp: true)))
            .ToArray();

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != (uint)inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not send the complete keyboard shortcut.");
        }
    }

    private static Input CreateInput(Key key, bool keyUp)
    {
        var virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
        var flags = keyUp ? KeyEventKeyUp : 0;
        if (IsExtendedKey(key))
        {
            flags |= KeyEventExtendedKey;
        }

        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = flags,
                },
            },
        };
    }

    private static bool IsExtendedKey(Key key)
    {
        return key is Key.Insert or Key.Delete or Key.Home or Key.End
            or Key.PageUp or Key.PageDown
            or Key.Left or Key.Right or Key.Up or Key.Down
            or Key.NumLock or Key.Divide or Key.RightAlt or Key.RightCtrl
            or Key.LWin or Key.RWin or Key.Apps;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}
