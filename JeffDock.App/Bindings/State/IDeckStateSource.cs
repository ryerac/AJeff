namespace JeffDock.App.Bindings.State;

internal interface IDeckStateSource : IDisposable
{
    string Id { get; }

    string CurrentState { get; }

    byte[]? CurrentImageBytes => null;

    string GetCurrentState(string deviceId, string sceneId, int controlIndex, IReadOnlyDictionary<string, string> parameters) => CurrentState;
    byte[]? GetCurrentImageBytes(string deviceId, string sceneId, int controlIndex, IReadOnlyDictionary<string, string> parameters) => CurrentImageBytes;

    event EventHandler<string>? StateChanged;

    void Start();
}
