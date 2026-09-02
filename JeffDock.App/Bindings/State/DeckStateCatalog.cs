using JeffDock.Core.Audio;

namespace JeffDock.App.Bindings.State;

internal sealed class DeckStateCatalog : IDisposable
{
    private readonly IReadOnlyDictionary<string, IDeckStateSource> _sources;

    public DeckStateCatalog(WindowsVolumeController volumeController)
    {
        IDeckStateSource[] sources =
        [
            new OutputMuteStateSource(volumeController),
            new MicrophoneMuteStateSource(volumeController),
        ];

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
