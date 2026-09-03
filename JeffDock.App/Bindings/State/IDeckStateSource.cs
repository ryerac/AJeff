namespace JeffDock.App.Bindings.State;

internal interface IDeckStateSource : IDisposable
{
    string Id { get; }

    string CurrentState { get; }

    byte[]? CurrentImageBytes => null;

    event EventHandler<string>? StateChanged;

    void Start();
}
