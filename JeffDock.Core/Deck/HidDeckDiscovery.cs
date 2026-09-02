using HidSharp;
using System.Diagnostics.CodeAnalysis;

namespace JeffDock.Core.Deck;

internal static class HidDeckDiscovery
{
    public static IReadOnlyList<HidDeckConnectionCandidate> FindCandidates(IEnumerable<IDeckProtocolProfile> profiles)
    {
        var candidates = new List<HidDeckConnectionCandidate>();

        foreach (var profile in profiles)
        {
            var devices = DeviceList.Local
                .GetHidDevices(profile.VendorId, profile.ProductId)
                .OrderByDescending(d => d.GetMaxInputReportLength());

            foreach (var device in devices)
            {
                var devicePath = device.DevicePath;
                var serialNumber = TryGetSerialNumber(device);
                var deviceId = BuildStableDeviceId(profile, serialNumber, devicePath);
                var inputReportLength = Math.Max(device.GetMaxInputReportLength(), profile.PreferredInputPacketLength);

                candidates.Add(new HidDeckConnectionCandidate(
                    profile,
                    device,
                    deviceId,
                    devicePath,
                    serialNumber,
                    inputReportLength
                ));
            }
        }

        return candidates;
    }

    public static bool TryOpen(HidDeckConnectionCandidate candidate, [NotNullWhen(true)] out HidDeckConnection? connection)
    {
        connection = null;

        if (!candidate.Device.TryOpen(out var stream))
        {
            return false;
        }

        stream.ReadTimeout = 500;

        if (candidate.Profile.InitializePacket is not null)
        {
            try
            {
                stream.Write(candidate.Profile.InitializePacket);
            }
            catch
            {
                // Keep going; some firmware/interface variants reject init writes.
            }
        }

        connection = new HidDeckConnection(candidate, stream);
        return true;
    }

    private static string BuildStableDeviceId(IDeckProtocolProfile profile, string? serialNumber, string devicePath)
    {
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return $"{profile.VendorId:X4}:{profile.ProductId:X4}:{serialNumber.Trim()}";
        }

        return $"{profile.VendorId:X4}:{profile.ProductId:X4}:{devicePath}";
    }

    private static string? TryGetSerialNumber(HidDevice device)
    {
        try
        {
            var serialNumber = device.GetSerialNumber();
            return string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber;
        }
        catch
        {
            return null;
        }
    }
}
