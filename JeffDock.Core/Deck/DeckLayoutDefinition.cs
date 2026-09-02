namespace JeffDock.Core.Deck;

public enum DeckControlType
{
    Button = 0,
    Encoder = 1,
}

public enum DeckControlVisualKind
{
    SquareButton = 0,
    RoundButton = 1,
    Knob = 2,
}

public sealed record DeckControlLayout(
    DeckControlType ControlType,
    int ControlIndex,
    DeckControlVisualKind VisualKind,
    double X,
    double Y,
    double Width,
    double Height,
    string Label,
    bool CanHaveIcon = false
);

public sealed record DeckLayoutDefinition(
    double Width,
    double Height,
    IReadOnlyList<DeckControlLayout> Controls,
    int ButtonImageRotationDegreesClockwise = 0,
    int ButtonImageOutputWidth = 60,
    int ButtonImageOutputHeight = 60
);
