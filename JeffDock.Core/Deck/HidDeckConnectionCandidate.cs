using HidSharp;

namespace JeffDock.Core.Deck;

internal sealed record HidDeckConnectionCandidate(
    IDeckProtocolProfile Profile,
    HidDevice Device,
    string DeviceId,
    string DevicePath,
    string? SerialNumber,
    int InputReportLength,
    int OutputReportLength
);
