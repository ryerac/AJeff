using NAudio.CoreAudioApi;

namespace JeffDock.Core.Audio;

public sealed class WindowsVolumeController : IDisposable
{
    public void NudgeVolume(int direction, float step = 0.02f)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var defaultRenderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        var current = defaultRenderDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
        var next = Math.Clamp(current + (step * Math.Sign(direction)), 0.0f, 1.0f);
        defaultRenderDevice.AudioEndpointVolume.MasterVolumeLevelScalar = next;
    }

    public void ToggleMute()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var defaultRenderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        var volume = defaultRenderDevice.AudioEndpointVolume;
        volume.Mute = !volume.Mute;
    }

    public void Dispose()
    {
    }
}
