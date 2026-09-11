namespace JeffDock.Core.Deck;

/// <summary>Serializes display output and filters waking input for one HID session.</summary>
public sealed class DeckDisplayController
{
    // The current protocol has press events but no release events. Treat consecutive
    // reports from the waking control as one gesture until it has been quiet this long.
    public const int WakeQuietMilliseconds = 500;
    private readonly object _sync = new();
    private readonly IDeckProtocolProfile _deviceProfile;
    private readonly IDeckButtonImageProfile _imageProfile;
    private readonly Action<byte[]> _write;
    private DeckDisplaySettings _settings;
    private IReadOnlyDictionary<int, byte[]> _images = new Dictionary<int, byte[]>();
    private long _lastActivity;
    private bool _sleeping;
    private bool _suspended;
    private (DeckInputEventType Type, int Index)? _wakingControl;
    private long _lastWakeInput;

    public DeckDisplayController(IDeckProtocolProfile profile, Action<byte[]> write,
        DeckDisplaySettings settings, long now)
    {
        settings.Validate();
        _deviceProfile = profile;
        _imageProfile = profile as IDeckButtonImageProfile
            ?? throw new ArgumentException("The profile must support display output.", nameof(profile));
        _write = write;
        _settings = settings;
        _lastActivity = now;
    }

    public void Configure(DeckDisplaySettings settings, long now)
    {
        settings.Validate();
        lock (_sync)
        {
            _settings = settings;
            _lastActivity = now;
            if (_suspended) return;
            if (_sleeping) RestoreDisplay();
            else _write(_imageProfile.BuildBrightnessPacket(settings.Brightness));
        }
    }

    public void UpdateImages(IReadOnlyDictionary<int, byte[]> images)
    {
        lock (_sync)
        {
            _images = images.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
            // Dimmed displays stay live; screen-off and suspended displays cache artwork for wake.
            if (!_suspended && (!_sleeping || _settings.SleepBehaviour == DeckSleepBehaviour.Dim))
                WriteImages(_sleeping ? 1 : _settings.Brightness);
        }
    }

    public void Tick(long now)
    {
        lock (_sync)
        {
            if (_suspended || _sleeping || !_settings.SleepEnabled
                || now - _lastActivity < _settings.SleepMinutes * 60_000L) return;
            if (_settings.SleepBehaviour == DeckSleepBehaviour.Dim)
                _write(_imageProfile.BuildBrightnessPacket(1));
            else
                foreach (var packet in _imageProfile.BuildSleepPackets()) _write(packet);
            _sleeping = true;
        }
    }

    /// <returns>True when the event may execute a bound action.</returns>
    public bool HandleInput(DeckInputEvent input, long now)
    {
        lock (_sync)
        {
            if (input.Type == DeckInputEventType.Unknown || _suspended) return false;
            _lastActivity = now;
            var control = (input.Type, input.ControlIndex);
            if (_sleeping)
            {
                _wakingControl = control;
                _lastWakeInput = now;
                RestoreDisplay();
                return false;
            }
            if (_wakingControl == control && now - _lastWakeInput < WakeQuietMilliseconds)
            {
                _lastWakeInput = now;
                return false;
            }
            if (now - _lastWakeInput >= WakeQuietMilliseconds) _wakingControl = null;
            return true;
        }
    }

    public void Suspend()
    {
        lock (_sync)
        {
            _suspended = true;
            foreach (var packet in _imageProfile.BuildSleepPackets()) _write(packet);
        }
    }

    public void Resume(long now)
    {
        lock (_sync)
        {
            _lastActivity = now;
            // Keep input blocked if restoring the display fails.
            RestoreDisplay();
            _suspended = false;
        }
    }

    private void RestoreDisplay()
    {
        if (_deviceProfile.InitializePacket is { } initialize) _write(initialize);
        WriteImages(_settings.Brightness);
        _sleeping = false;
    }

    private void WriteImages(int brightness)
    {
        foreach (var packet in _imageProfile.BuildClearButtonImages()) _write(packet);
        foreach (var (index, image) in _images.OrderBy(pair => pair.Key))
            foreach (var packet in _imageProfile.BuildButtonImageUpload(index, image)) _write(packet);
        _write(_imageProfile.BuildBrightnessPacket(brightness));
    }
}
