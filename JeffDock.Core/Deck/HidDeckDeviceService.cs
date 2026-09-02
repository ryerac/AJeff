namespace JeffDock.Core.Deck;

public sealed class HidDeckDeviceService : IDisposable
{
    private readonly IReadOnlyList<IDeckProtocolProfile> _profiles;
    private HidDeckConnection? _connection;
    private DateTime _lastUnknownLogUtc = DateTime.MinValue;

    public HidDeckDeviceService(IEnumerable<IDeckProtocolProfile> profiles)
    {
        _profiles = profiles.ToList();
    }

    public bool IsConnected => _connection is not null;
    public string ActiveDeviceName => _connection?.Profile.Name ?? "None";

    public bool TryConnect()
    {
        if (_connection is not null)
        {
            return true;
        }

        foreach (var candidate in HidDeckDiscovery.FindCandidates(_profiles))
        {
            if (!HidDeckDiscovery.TryOpen(candidate, out var connection))
            {
                continue;
            }

            _connection = connection;
            Console.WriteLine($"Opened HID interface path={connection.DevicePath} inputLen={connection.InputReportLength} profile={connection.Profile.Name} deviceId={connection.DeviceId}");
            return true;
        }

        return false;
    }

    public DeckInputEvent ReadEvent(CancellationToken cancellationToken)
    {
        var connection = _connection ?? throw new InvalidOperationException("Device is not connected.");

        var buffer = new byte[connection.InputReportLength];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var bytesRead = connection.Stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    continue;
                }

                var evt = connection.Profile.Parse(buffer.AsSpan(0, bytesRead));
                if (evt.Type != DeckInputEventType.Unknown)
                {
                    return evt;
                }

                MaybeLogUnknownPacket(buffer, bytesRead, connection.Profile);
            }
            catch (TimeoutException)
            {
                // Read timeout is expected; continue so cancellation can be observed.
            }
            catch (IOException)
            {
                Disconnect();
                return default;
            }
            catch (ObjectDisposedException)
            {
                Disconnect();
                return default;
            }
        }

        return default;
    }

    public void Disconnect()
    {
        _connection?.Dispose();
        _connection = null;
    }

    public void Dispose()
    {
        Disconnect();
    }

    private void MaybeLogUnknownPacket(byte[] buffer, int bytesRead, IDeckProtocolProfile profile)
    {
        if ((DateTime.UtcNow - _lastUnknownLogUtc).TotalMilliseconds < 500)
        {
            return;
        }

        var headLength = Math.Min(bytesRead, 16);
        var head = BitConverter.ToString(buffer, 0, headLength);
        Console.WriteLine($"Unmapped HID packet profile={profile.Name} len={bytesRead} head={head}");
        _lastUnknownLogUtc = DateTime.UtcNow;
    }
}
