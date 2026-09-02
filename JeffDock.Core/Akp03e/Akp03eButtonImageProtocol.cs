namespace JeffDock.Core.Akp03e;

public static class Akp03eButtonImageProtocol
{
    public const int PacketLength = 1025;
    public const int ImageWidth = 64;
    public const int ImageHeight = 64;

    private const int ImageChunkLength = PacketLength - 1;

    public static IReadOnlyList<byte[]> BuildUpload(int controlIndex, ReadOnlySpan<byte> jpegData)
    {
        if (controlIndex is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(controlIndex), "AKP03E display button index must be between 0 and 5.");
        }

        if (jpegData.Length is 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(jpegData), "AKP03E JPEG data must contain between 1 and 65535 bytes.");
        }

        if (jpegData.Length < 2 || jpegData[0] != 0xFF || jpegData[1] != 0xD8)
        {
            throw new ArgumentException("AKP03E button images must be JPEG data.", nameof(jpegData));
        }

        var packets = new List<byte[]> { BuildAnnouncePacket(controlIndex, jpegData.Length) };
        for (var offset = 0; offset < jpegData.Length; offset += ImageChunkLength)
        {
            var length = Math.Min(ImageChunkLength, jpegData.Length - offset);
            var packet = new byte[PacketLength];
            jpegData.Slice(offset, length).CopyTo(packet.AsSpan(1));
            packets.Add(packet);
        }

        packets.Add(BuildCommandPacket("STP"u8));
        return packets;
    }

    public static byte[] BuildBrightnessPacket(int percentage)
    {
        if (percentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), "Brightness must be between 0 and 100.");
        }

        Span<byte> command = stackalloc byte[] { (byte)'L', (byte)'I', (byte)'G', 0, 0, (byte)percentage };
        return BuildCommandPacket(command);
    }

    private static byte[] BuildAnnouncePacket(int controlIndex, int imageLength)
    {
        Span<byte> command = stackalloc byte[]
        {
            (byte)'B', (byte)'A', (byte)'T', 0, 0,
            (byte)(imageLength >> 8),
            (byte)imageLength,
            (byte)(controlIndex + 1),
        };

        return BuildCommandPacket(command);
    }

    private static byte[] BuildCommandPacket(ReadOnlySpan<byte> command)
    {
        var packet = new byte[PacketLength];
        "CRT"u8.CopyTo(packet.AsSpan(1));
        command.CopyTo(packet.AsSpan(6));
        return packet;
    }
}
