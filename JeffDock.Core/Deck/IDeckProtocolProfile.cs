namespace JeffDock.Core.Deck;

public interface IDeckProtocolProfile
{
    string Name { get; }
    int VendorId { get; }
    int ProductId { get; }
    int PreferredInputPacketLength { get; }
    DeckLayoutDefinition Layout { get; }

    // Optional open-time init packet; null when not required.
    byte[]? InitializePacket { get; }

    DeckInputEvent Parse(ReadOnlySpan<byte> packet);
}
