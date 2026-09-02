using JeffDock.Core.Audio;

namespace JeffDock.App.Bindings.State;

internal sealed class OutputMuteStateSource(WindowsVolumeController volumeController) : PollingDeckStateSource
{
    public const string SourceId = "core.audio.output-mute";

    public override string Id => SourceId;

    protected override string ReadState() => volumeController.GetOutputMute() ? "muted" : "unmuted";
}

internal sealed class MicrophoneMuteStateSource(WindowsVolumeController volumeController) : PollingDeckStateSource
{
    public const string SourceId = "core.audio.microphone-mute";

    public override string Id => SourceId;

    protected override string ReadState() => volumeController.GetMicrophoneMute() ? "muted" : "unmuted";
}
