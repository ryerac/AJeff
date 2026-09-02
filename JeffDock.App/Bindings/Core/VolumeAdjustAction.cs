using JeffDock.Core.Audio;
using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings.Core;

internal sealed class VolumeAdjustAction(WindowsVolumeController volumeController) : IDeckAction
{
    public const string ActionId = "core.audio.volume-adjust";

    public string Id => ActionId;

    public string DisplayName => "Adjust Volume";

    public bool Supports(DeckInputEventType triggerEventType)
    {
        return triggerEventType == DeckInputEventType.EncoderTurn;
    }

    public void Execute(DeckActionContext context)
    {
        volumeController.NudgeVolume(context.InputEvent.Direction);
    }
}
