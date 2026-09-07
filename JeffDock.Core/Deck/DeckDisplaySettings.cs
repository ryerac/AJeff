namespace JeffDock.Core.Deck;

public enum DeckSleepBehaviour
{
    Dim = 0,
    ScreenOff = 1,
}

public sealed record DeckDisplaySettings(
    int Brightness = 80,
    bool SleepEnabled = false,
    int SleepMinutes = 15,
    DeckSleepBehaviour SleepBehaviour = DeckSleepBehaviour.Dim)
{
    public void Validate()
    {
        if (Brightness is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(Brightness), "Brightness must be between 1 and 100.");
        if (SleepMinutes is < 1 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(SleepMinutes), "Sleep timeout must be between 1 and 1440 minutes.");
        if (!Enum.IsDefined(SleepBehaviour))
            throw new ArgumentOutOfRangeException(nameof(SleepBehaviour));
    }
}
