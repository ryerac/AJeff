using JeffDock.Core.Deck;

namespace JeffDock.Core.Akp03e;

public sealed class Akp03eProfile : IDeckProtocolProfile, IDeckButtonImageProfile
{
    // AJAZZ AKP03E appears as Mirabox v2.
    public string Name => "AJAZZ AKP03E";
    public int VendorId => 0x0300;
    public int ProductId => 0x3002;
    public int PreferredInputPacketLength => 512;
    public int PreferredOutputPacketLength => Akp03eButtonImageProtocol.PacketLength;
    public int ButtonImageWidth => Akp03eButtonImageProtocol.ImageWidth;
    public int ButtonImageHeight => Akp03eButtonImageProtocol.ImageHeight;
    public DeckLayoutDefinition Layout { get; } = BuildLayout();

    public byte[] InitializePacket { get; } = BuildInitializePacket();

    public DeckInputEvent Parse(ReadOnlySpan<byte> packet)
    {
        // Some HID stacks expose report-id-prefixed input while others don't.
        var evt = ParseWithOffsets(packet, dataLengthOffset: 0, actionOffset: 9);
        if (evt.Type != DeckInputEventType.Unknown)
        {
            return evt;
        }

        return ParseWithOffsets(packet, dataLengthOffset: 1, actionOffset: 10);
    }

    public IReadOnlyList<byte[]> BuildButtonImageUpload(int controlIndex, ReadOnlySpan<byte> jpegData)
    {
        return Akp03eButtonImageProtocol.BuildUpload(controlIndex, jpegData);
    }

    public IReadOnlyList<byte[]> BuildClearButtonImages()
    {
        return Akp03eButtonImageProtocol.BuildClearButtonImages();
    }

    public IReadOnlyList<byte[]> BuildSleepPackets()
    {
        return Akp03eButtonImageProtocol.BuildSleepPackets();
    }

    public byte[] BuildBrightnessPacket(int percentage)
    {
        return Akp03eButtonImageProtocol.BuildBrightnessPacket(percentage);
    }

    private static DeckInputEvent ParseWithOffsets(ReadOnlySpan<byte> packet, int dataLengthOffset, int actionOffset)
    {
        if (packet.Length <= actionOffset)
        {
            return default;
        }

        if (packet[dataLengthOffset] == 0)
        {
            return default;
        }

        var code = packet[actionOffset];

        return code switch
        {
            0x91 => new DeckInputEvent(DeckInputEventType.EncoderTurn, 0, +1),
            0x90 => new DeckInputEvent(DeckInputEventType.EncoderTurn, 0, -1),
            0x51 => new DeckInputEvent(DeckInputEventType.EncoderTurn, 1, +1),
            0x50 => new DeckInputEvent(DeckInputEventType.EncoderTurn, 1, -1),
            0x61 => new DeckInputEvent(DeckInputEventType.EncoderTurn, 2, +1),
            0x60 => new DeckInputEvent(DeckInputEventType.EncoderTurn, 2, -1),
            0x33 => new DeckInputEvent(DeckInputEventType.EncoderPress, 0, 0),
            0x35 => new DeckInputEvent(DeckInputEventType.EncoderPress, 1, 0),
            0x34 => new DeckInputEvent(DeckInputEventType.EncoderPress, 2, 0),
            >= 0x01 and <= 0x06 => new DeckInputEvent(DeckInputEventType.ButtonPress, code - 1, 0),
            0x25 => new DeckInputEvent(DeckInputEventType.ButtonPress, 6, 0),
            0x30 => new DeckInputEvent(DeckInputEventType.ButtonPress, 7, 0),
            0x31 => new DeckInputEvent(DeckInputEventType.ButtonPress, 8, 0),
            _ => default,
        };
    }

    private static byte[] BuildInitializePacket()
    {
        // [0x00, 'C','R','T',0,0,'D','I','S'] padded to 1025 bytes for AKP03E v2.
        var packet = new byte[1025];
        packet[0] = 0x00;
        packet[1] = 0x43;
        packet[2] = 0x52;
        packet[3] = 0x54;
        packet[4] = 0x00;
        packet[5] = 0x00;
        packet[6] = 0x44;
        packet[7] = 0x49;
        packet[8] = 0x53;
        return packet;
    }

    private static DeckLayoutDefinition BuildLayout()
    {
        return new DeckLayoutDefinition(
            Width: 460,
            Height: 220,
            Controls:
            [
                new DeckControlLayout(DeckControlType.Button, 0, DeckControlVisualKind.SquareButton, 8, 8, 72, 72, "0", CanHaveIcon: true),
                new DeckControlLayout(DeckControlType.Button, 1, DeckControlVisualKind.SquareButton, 88, 8, 72, 72, "1", CanHaveIcon: true),
                new DeckControlLayout(DeckControlType.Button, 2, DeckControlVisualKind.SquareButton, 168, 8, 72, 72, "2", CanHaveIcon: true),
                new DeckControlLayout(DeckControlType.Button, 3, DeckControlVisualKind.SquareButton, 8, 88, 72, 72, "3", CanHaveIcon: true),
                new DeckControlLayout(DeckControlType.Button, 4, DeckControlVisualKind.SquareButton, 88, 88, 72, 72, "4", CanHaveIcon: true),
                new DeckControlLayout(DeckControlType.Button, 5, DeckControlVisualKind.SquareButton, 168, 88, 72, 72, "5", CanHaveIcon: true),
                new DeckControlLayout(DeckControlType.Button, 6, DeckControlVisualKind.RoundButton, 10, 176, 56, 32, "6"),
                new DeckControlLayout(DeckControlType.Button, 7, DeckControlVisualKind.RoundButton, 92, 176, 56, 32, "7"),
                new DeckControlLayout(DeckControlType.Button, 8, DeckControlVisualKind.RoundButton, 174, 176, 56, 32, "8"),
                new DeckControlLayout(DeckControlType.Encoder, 1, DeckControlVisualKind.Knob, 310, 8, 110, 110, "1"),
                new DeckControlLayout(DeckControlType.Encoder, 0, DeckControlVisualKind.Knob, 300, 136, 70, 70, "0"),
                new DeckControlLayout(DeckControlType.Encoder, 2, DeckControlVisualKind.Knob, 380, 136, 70, 70, "2"),
            ],
            ButtonImageRotationDegreesClockwise: 90,
            ButtonImageOutputWidth: Akp03eButtonImageProtocol.ImageWidth,
            ButtonImageOutputHeight: Akp03eButtonImageProtocol.ImageHeight
        );
    }
}
