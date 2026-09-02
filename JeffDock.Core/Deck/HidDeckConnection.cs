using HidSharp;

namespace JeffDock.Core.Deck;

internal sealed class HidDeckConnection : IDisposable
{
    public HidDeckConnection(HidDeckConnectionCandidate candidate, HidStream stream)
    {
        Candidate = candidate;
        Stream = stream;
    }

    public HidDeckConnectionCandidate Candidate { get; }
    public IDeckProtocolProfile Profile => Candidate.Profile;
    public string DeviceId => Candidate.DeviceId;
    public string DevicePath => Candidate.DevicePath;
    public string? SerialNumber => Candidate.SerialNumber;
    public int InputReportLength => Candidate.InputReportLength;
    public HidStream Stream { get; }

    public void Dispose()
    {
        Stream.Dispose();
    }
}
