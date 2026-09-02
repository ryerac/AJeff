namespace JeffDock.Core.Deck;

public enum DeckInputEventType
{
    Unknown = 0,
    EncoderTurn = 1,
    EncoderPress = 2,
    ButtonPress = 3,
}

public readonly record struct DeckInputEvent(
    DeckInputEventType Type,
    int ControlIndex,
    int Direction
);
