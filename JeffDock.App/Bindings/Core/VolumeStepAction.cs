using JeffDock.Core.Audio;
using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings.Core;

internal sealed class VolumeStepAction(
    WindowsVolumeController volumeController,
    string id,
    string displayName,
    int direction) : IDeckAction
{
    public const string UpActionId = "core.audio.volume-up";
    public const string DownActionId = "core.audio.volume-down";

    public string Id => id;

    public string DisplayName => displayName;

    public DeckActionGroup Group => DeckActionGroups.Audio;

    public bool Supports(DeckInputEventType triggerEventType)
    {
        return triggerEventType is DeckInputEventType.ButtonPress or DeckInputEventType.EncoderPress;
    }

    public void Execute(DeckActionContext context)
    {
        volumeController.NudgeVolume(direction);
    }
}
