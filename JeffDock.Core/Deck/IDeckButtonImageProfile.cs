namespace JeffDock.Core.Deck;

public interface IDeckButtonImageProfile
{
    int ButtonImageWidth { get; }

    int ButtonImageHeight { get; }

    int PreferredOutputPacketLength { get; }

    IReadOnlyList<byte[]> BuildButtonImageUpload(int controlIndex, ReadOnlySpan<byte> jpegData);

    IReadOnlyList<byte[]> BuildClearButtonImages();

    IReadOnlyList<byte[]> BuildSleepPackets();

    byte[] BuildBrightnessPacket(int percentage);
}
