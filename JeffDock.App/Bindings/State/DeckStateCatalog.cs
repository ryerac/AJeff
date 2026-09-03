using JeffDock.Core.Audio;

namespace JeffDock.App.Bindings.State;

internal sealed class DeckStateCatalog : IDisposable
{
    private readonly IReadOnlyDictionary<string, IDeckStateSource> _sources;

    public DeckStateCatalog(WindowsVolumeController volumeController, IEnumerable<IDeckStateSource>? pluginSources = null)
    {
        var sources = new List<IDeckStateSource>
        {
            new OutputMuteStateSource(volumeController),
            new MicrophoneMuteStateSource(volumeController),
        };
        if (pluginSources is not null)
        {
            sources.AddRange(pluginSources);
        }

        _sources = sources.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            source.StateChanged += OnStateChanged;
            source.Start();
        }
    }

    public event Action<string, string>? StateChanged;

    public string GetCurrentState(string sourceId)
    {
        return _sources.GetValueOrDefault(sourceId)?.CurrentState ?? "unknown";
    }

    public byte[]? GetCurrentImageBytes(string sourceId)
    {
        return _sources.GetValueOrDefault(sourceId)?.CurrentImageBytes;
    }

    public string GetCurrentState(string sourceId, string deviceId, string sceneId, int controlIndex, IReadOnlyDictionary<string, string> parameters)
    {
        return _sources.GetValueOrDefault(sourceId)?.GetCurrentState(deviceId, sceneId, controlIndex, parameters) ?? "unknown";
    }

    public byte[]? GetCurrentImageBytes(string sourceId, string deviceId, string sceneId, int controlIndex, IReadOnlyDictionary<string, string> parameters)
    {
        return _sources.GetValueOrDefault(sourceId)?.GetCurrentImageBytes(deviceId, sceneId, controlIndex, parameters);
    }

    private void OnStateChanged(object? sender, string state)
    {
        if (sender is IDeckStateSource source)
        {
            StateChanged?.Invoke(source.Id, state);
        }
    }

    public void Dispose()
    {
        foreach (var source in _sources.Values)
        {
            source.StateChanged -= OnStateChanged;
            source.Dispose();
        }
    }
}
