using System.Collections.Concurrent;

namespace JeffDock.Core.Deck;

public sealed class DeckMonitorService : IDisposable
{
    private sealed class Session
    {
        public required string ConnectionKey { get; init; }
        public required HidDeckConnection Connection { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required Task ReadTask { get; set; }
        public Queue<string> LogLines { get; } = new();
        public object LogLock { get; } = new();
        public DeckDisplayController? Display { get; set; }
    }

    private readonly IReadOnlyList<IDeckProtocolProfile> _profiles;
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly object _displaySettingsLock = new();
    private readonly Dictionary<string, DeckDisplaySettings> _displaySettings = new(StringComparer.OrdinalIgnoreCase);
    private bool _displaysSuspended;
    private readonly int _maxLinesPerDevice;
    private CancellationTokenSource? _scanCts;
    private Task? _scanTask;

    public DeckMonitorService(IEnumerable<IDeckProtocolProfile> profiles, int maxLinesPerDevice = 100)
    {
        _profiles = profiles.ToList();
        _maxLinesPerDevice = Math.Max(10, maxLinesPerDevice);
    }

    public event Action? DevicesChanged;
    public event Action<MonitoredDeckDevice>? DeviceLogChanged;
    public event Action<MonitoredDeckDevice, DeckInputEvent>? InputEventReceived;

    public void Start()
    {
        if (_scanTask is not null)
        {
            return;
        }

        _scanCts = new CancellationTokenSource();
        _scanTask = Task.Run(() => ScanLoop(_scanCts.Token));
    }

    public IReadOnlyList<MonitoredDeckDevice> GetConnectedDevices()
    {
        return _sessions.Values
            .Select(BuildMonitoredDevice)
            .OrderBy(d => d.ProfileName)
            .ThenBy(d => d.SerialNumber ?? d.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetLogLines(string deviceId)
    {
        var session = _sessions.Values.FirstOrDefault(s => string.Equals(s.Connection.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            return [];
        }

        lock (session.LogLock)
        {
            return session.LogLines.ToList();
        }
    }

    public void ConfigureDisplay(string deviceId, DeckDisplaySettings settings)
    {
        settings.Validate();
        lock (_displaySettingsLock)
        {
            _displaySettings[deviceId] = settings;
            foreach (var session in _sessions.Values.Where(session =>
                         string.Equals(session.Connection.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)))
                RunDisplayOperation(session, display => display.Configure(settings, Environment.TickCount64));
        }
    }

    public bool TrySetButtonImages(string deviceId, IReadOnlyDictionary<int, byte[]> jpegImages)
    {
        var session = GetDisplaySessions().FirstOrDefault(candidate =>
            string.Equals(candidate.Connection.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        return session is not null && RunDisplayOperation(session, display => display.UpdateImages(jpegImages));
    }

    private IEnumerable<Session> GetDisplaySessions() => _sessions.Values
        .Where(session => session.Display is not null)
        .OrderByDescending(session => session.Connection.OutputReportLength)
        .ThenBy(session => session.ConnectionKey, StringComparer.OrdinalIgnoreCase)
        .DistinctBy(session => session.Connection.DeviceId, StringComparer.OrdinalIgnoreCase);

    public void SleepAllDevices()
    {
        lock (_displaySettingsLock)
        {
            _displaysSuspended = true;
            foreach (var session in _sessions.Values)
                RunDisplayOperation(session, display => display.Suspend());
        }
    }

    public void WakeAllDevices()
    {
        lock (_displaySettingsLock)
        {
            _displaysSuspended = false;
            foreach (var session in _sessions.Values)
                RunDisplayOperation(session, display => display.Resume(Environment.TickCount64));
        }
    }

    private bool RunDisplayOperation(Session session, Action<DeckDisplayController> operation)
    {
        if (session.Display is not { } display) return false;
        try
        {
            operation(display);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or ArgumentException or TimeoutException)
        {
            AddLogLine(session, $"display update failed: {exception.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _scanCts?.Cancel();

        try
        {
            _scanTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore race conditions during shutdown.
        }

        foreach (var key in _sessions.Keys.ToList())
        {
            RemoveSession(key, waitForTask: false);
        }

        _scanCts?.Dispose();
        _scanTask = null;
        _scanCts = null;
    }

    private async Task ScanLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                ScanOnce();
                foreach (var session in GetDisplaySessions())
                    RunDisplayOperation(session, display => display.Tick(Environment.TickCount64));
            }
            catch
            {
                // Keep scanning even if one pass hits an unexpected HID error.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private void ScanOnce()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in HidDeckDiscovery.FindCandidates(_profiles))
        {
            seen.Add(candidate.DevicePath);

            if (_sessions.ContainsKey(candidate.DevicePath))
            {
                continue;
            }

            TryAddSession(candidate);
        }

        foreach (var existing in _sessions.Keys)
        {
            if (!seen.Contains(existing))
            {
                RemoveSession(existing, waitForTask: false);
            }
        }
    }

    private void TryAddSession(HidDeckConnectionCandidate candidate)
    {
        if (!HidDeckDiscovery.TryOpen(candidate, out var connection))
        {
            return;
        }

        var cts = new CancellationTokenSource();

        var session = new Session
        {
            ConnectionKey = candidate.DevicePath,
            Connection = connection,
            Cts = cts,
            ReadTask = Task.CompletedTask,
        };

        lock (_displaySettingsLock)
        {
            if (connection.Profile is IDeckButtonImageProfile imageProfile
                && connection.OutputReportLength >= imageProfile.PreferredOutputPacketLength)
            {
                var settings = _displaySettings.GetValueOrDefault(connection.DeviceId) ?? new DeckDisplaySettings();
                session.Display = new DeckDisplayController(connection.Profile,
                    packet => connection.Stream.Write(packet), settings, Environment.TickCount64);
                RunDisplayOperation(session, display =>
                {
                    if (_displaysSuspended) display.Suspend();
                    else display.Configure(settings, Environment.TickCount64);
                });
            }
            if (!_sessions.TryAdd(session.ConnectionKey, session))
            {
                cts.Cancel();
                connection.Dispose();
                cts.Dispose();
                return;
            }
        }

        session.ReadTask = Task.Run(() => ReadLoop(session));
        AddLogLine(session, "connected");
        DevicesChanged?.Invoke();
    }

    private void ReadLoop(Session session)
    {
        var buffer = new byte[session.Connection.InputReportLength];

        while (!session.Cts.IsCancellationRequested)
        {
            try
            {
                var bytesRead = session.Connection.Stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    continue;
                }

                var evt = session.Connection.Profile.Parse(buffer.AsSpan(0, bytesRead));
                var line = BuildLogLine(evt, buffer.AsSpan(0, bytesRead));
                AddLogLine(session, line);

                if (evt.Type != DeckInputEventType.Unknown)
                {
                    try
                    {
                        // Some devices expose separate input and output HID interfaces.
                        // Route all interfaces to the same controller used for artwork
                        // and idle timing, so one interface cannot sleep an active deck.
                        var display = GetDisplaySessions().FirstOrDefault(candidate =>
                            string.Equals(candidate.Connection.DeviceId, session.Connection.DeviceId, StringComparison.OrdinalIgnoreCase))?.Display;
                        if (display is null || display.HandleInput(evt, Environment.TickCount64))
                            InputEventReceived?.Invoke(BuildMonitoredDevice(session), evt);
                    }
                    catch (Exception exception)
                    {
                        AddLogLine(session, $"input handler failed: {exception.Message}");
                    }
                }
            }
            catch (TimeoutException)
            {
                // Continue loop to check cancellation.
            }
            catch (IOException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }

        RemoveSession(session.ConnectionKey, waitForTask: false);
    }

    private static string BuildLogLine(DeckInputEvent evt, ReadOnlySpan<byte> packet)
    {
        var headLength = Math.Min(packet.Length, 16);
        var head = BitConverter.ToString(packet[..headLength].ToArray());
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        if (evt.Type == DeckInputEventType.Unknown)
        {
            return $"{timestamp} len={packet.Length} raw={head} evt=Unknown";
        }

        return $"{timestamp} len={packet.Length} raw={head} evt={evt.Type} idx={evt.ControlIndex} dir={evt.Direction}";
    }

    private void AddLogLine(Session session, string line)
    {
        lock (session.LogLock)
        {
            session.LogLines.Enqueue(line);
            while (session.LogLines.Count > _maxLinesPerDevice)
            {
                session.LogLines.Dequeue();
            }
        }

        DeviceLogChanged?.Invoke(BuildMonitoredDevice(session));
    }

    private void RemoveSession(string deviceId, bool waitForTask)
    {
        if (!_sessions.TryRemove(deviceId, out var session))
        {
            return;
        }

        try
        {
            session.Cts.Cancel();
            session.Connection.Dispose();

            if (waitForTask)
            {
                session.ReadTask.Wait(TimeSpan.FromMilliseconds(500));
            }

            session.Cts.Dispose();
        }
        catch
        {
            // Best-effort cleanup.
        }

        DevicesChanged?.Invoke();
    }

    private static MonitoredDeckDevice BuildMonitoredDevice(Session session)
    {
        return new MonitoredDeckDevice(
            session.Connection.DeviceId,
            session.Connection.Profile.Name,
            session.Connection.DevicePath,
            session.Connection.SerialNumber,
            session.Connection.Profile.Layout
        );
    }
}
