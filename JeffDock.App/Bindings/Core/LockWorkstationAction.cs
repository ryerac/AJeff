using System.ComponentModel;
using System.Runtime.InteropServices;
using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings.Core;

internal sealed class LockWorkstationAction : IDeckAction
{
    public const string ActionId = "core.machine.lock";

    public string Id => ActionId;
    public string DisplayName => "Lock Computer";
    public DeckActionGroup Group => DeckActionGroups.Machine;

    public bool Supports(DeckInputEventType triggerEventType) =>
        triggerEventType is DeckInputEventType.ButtonPress or DeckInputEventType.EncoderPress;

    public void Execute(DeckActionContext context)
    {
        if (!LockWorkStation())
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not lock the workstation.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
