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
        public object OutputLock { get; } = new();
    }

    private readonly IReadOnlyList<IDeckProtocolProfile> _profiles;
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
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

    public bool TrySetButtonImages(string deviceId, IReadOnlyDictionary<int, byte[]> jpegImages, int brightness = 80)
    {
        var session = _sessions.Values
            .Where(candidate => string.Equals(candidate.Connection.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.Connection.Profile is IDeckButtonImageProfile imageProfile
                                && candidate.Connection.OutputReportLength >= imageProfile.PreferredOutputPacketLength)
            .OrderByDescending(candidate => candidate.Connection.OutputReportLength)
            .FirstOrDefault();

        if (session?.Connection.Profile is not IDeckButtonImageProfile profile)
        {
            return false;
        }

        try
        {
            lock (session.OutputLock)
            {
                foreach (var (controlIndex, jpegData) in jpegImages.OrderBy(image => image.Key))
                {
                    foreach (var packet in profile.BuildButtonImageUpload(controlIndex, jpegData))
                    {
                        session.Connection.Stream.Write(packet);
                    }
                }

                session.Connection.Stream.Write(profile.BuildBrightnessPacket(brightness));
            }

            AddLogLine(session, $"updated {jpegImages.Count} button icon(s)");
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or ArgumentException or TimeoutException)
        {
            AddLogLine(session, $"button icon update failed: {exception.Message}");
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

        if (!_sessions.TryAdd(session.ConnectionKey, session))
        {
            cts.Cancel();
            connection.Dispose();
            cts.Dispose();
            return;
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
                    InputEventReceived?.Invoke(BuildMonitoredDevice(session), evt);
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
