namespace JeffDock.Core.Deck;

public sealed record MonitoredDeckDevice(
    string DeviceId,
    string ProfileName,
    string DevicePath,
    string? SerialNumber,
    DeckLayoutDefinition Layout,
    bool IsSimulated = false
)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(SerialNumber)
        ? $"{ProfileName} [{SerialNumber}]"
        : $"{ProfileName} [{DevicePath}]";
}
