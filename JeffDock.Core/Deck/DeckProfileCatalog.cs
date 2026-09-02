using JeffDock.Core.Akp03e;

namespace JeffDock.Core.Deck;

public static class DeckProfileCatalog
{
    public static IReadOnlyList<IDeckProtocolProfile> SupportedProfiles { get; } =
    [
        new Akp03eProfile(),
        // Add future deck profiles here.
    ];
}
