namespace JeffDock.App.Bindings.State;

internal abstract class PollingDeckStateSource : IDeckStateSource
{
    private readonly object _sync = new();
    private readonly TimeSpan _interval;
    private Timer? _timer;
    private string _currentState = "unknown";
    private bool _polling;

    protected PollingDeckStateSource(TimeSpan? interval = null)
    {
        _interval = interval ?? TimeSpan.FromMilliseconds(500);
    }

    public abstract string Id { get; }

    public string CurrentState
    {
        get
        {
            lock (_sync)
            {
                return _currentState;
            }
        }
    }

    public event EventHandler<string>? StateChanged;

    public void Start()
    {
        lock (_sync)
        {
            _timer ??= new Timer(Poll, null, TimeSpan.Zero, _interval);
        }
    }

    protected abstract string ReadState();

    private void Poll(object? state)
    {
        lock (_sync)
        {
            if (_polling)
            {
                return;
            }

            _polling = true;
        }

        try
        {
            var nextState = ReadState();
            bool changed;
            lock (_sync)
            {
                changed = !string.Equals(_currentState, nextState, StringComparison.OrdinalIgnoreCase);
                _currentState = nextState;
            }

            if (changed)
            {
                StateChanged?.Invoke(this, nextState);
            }
        }
        catch
        {
            // Devices can disappear between polls. Keep the last known state and retry.
        }
        finally
        {
            lock (_sync)
            {
                _polling = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
