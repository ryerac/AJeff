using JeffDock.Core.Akp03e;
using JeffDock.Core.Deck;

namespace JeffDock.Core.Tests;

public class Akp03eProfileTests
{
    private readonly Akp03eProfile _profile = new();

    [Theory]
    [InlineData(0x91, DeckInputEventType.EncoderTurn, 0, 1)]
    [InlineData(0x90, DeckInputEventType.EncoderTurn, 0, -1)]
    [InlineData(0x51, DeckInputEventType.EncoderTurn, 1, 1)]
    [InlineData(0x50, DeckInputEventType.EncoderTurn, 1, -1)]
    [InlineData(0x61, DeckInputEventType.EncoderTurn, 2, 1)]
    [InlineData(0x60, DeckInputEventType.EncoderTurn, 2, -1)]
    [InlineData(0x33, DeckInputEventType.EncoderPress, 0, 0)]
    [InlineData(0x35, DeckInputEventType.EncoderPress, 1, 0)]
    [InlineData(0x34, DeckInputEventType.EncoderPress, 2, 0)]
    [InlineData(0x01, DeckInputEventType.ButtonPress, 0, 0)]
    [InlineData(0x02, DeckInputEventType.ButtonPress, 1, 0)]
    [InlineData(0x03, DeckInputEventType.ButtonPress, 2, 0)]
    [InlineData(0x04, DeckInputEventType.ButtonPress, 3, 0)]
    [InlineData(0x05, DeckInputEventType.ButtonPress, 4, 0)]
    [InlineData(0x06, DeckInputEventType.ButtonPress, 5, 0)]
    [InlineData(0x25, DeckInputEventType.ButtonPress, 6, 0)]
    [InlineData(0x30, DeckInputEventType.ButtonPress, 7, 0)]
    [InlineData(0x31, DeckInputEventType.ButtonPress, 8, 0)]
    public void Parse_KnownCodes_ReturnsExpectedEvent(byte actionCode, DeckInputEventType expectedType, int expectedIndex, int expectedDirection)
    {
        var packet = BuildPacket(actionCode, shifted: false);

        var evt = _profile.Parse(packet);

        Assert.Equal(expectedType, evt.Type);
        Assert.Equal(expectedIndex, evt.ControlIndex);
        Assert.Equal(expectedDirection, evt.Direction);
    }

    [Theory]
    [InlineData(0x91)]
    [InlineData(0x35)]
    [InlineData(0x25)]
    public void Parse_ShiftedPacketLayout_ReturnsExpectedEvent(byte actionCode)
    {
        var evt = _profile.Parse(BuildPacket(actionCode, shifted: true));

        Assert.NotEqual(DeckInputEventType.Unknown, evt.Type);
    }

    [Fact]
    public void Parse_NoData_ReturnsUnknown()
    {
        var evt = _profile.Parse(new byte[16]);

        Assert.Equal(DeckInputEventType.Unknown, evt.Type);
    }

    [Fact]
    public void Layout_DescribesExpectedPhysicalControls()
    {
        Assert.Equal(12, _profile.Layout.Controls.Count);
        Assert.Equal(90, _profile.Layout.ButtonImageRotationDegreesClockwise);
        Assert.Equal(64, _profile.Layout.ButtonImageOutputWidth);
        Assert.Equal(64, _profile.Layout.ButtonImageOutputHeight);
        Assert.Equal(9, _profile.Layout.Controls.Count(control => control.ControlType == DeckControlType.Button));
        Assert.Equal(3, _profile.Layout.Controls.Count(control => control.ControlType == DeckControlType.Encoder));
        Assert.Equal(
            [0, 1, 2, 3, 4, 5],
            _profile.Layout.Controls
                .Where(control => control.CanHaveIcon)
                .Select(control => control.ControlIndex)
                .Order()
                .ToArray());
        Assert.Contains(_profile.Layout.Controls, control => control.ControlType == DeckControlType.Button && control.ControlIndex == 0 && control.VisualKind == DeckControlVisualKind.SquareButton);
        Assert.Contains(_profile.Layout.Controls, control => control.ControlType == DeckControlType.Button && control.ControlIndex == 6 && control.VisualKind == DeckControlVisualKind.RoundButton);
        Assert.Contains(_profile.Layout.Controls, control => control.ControlType == DeckControlType.Encoder && control.ControlIndex == 1 && control.VisualKind == DeckControlVisualKind.Knob);
    }

    private static byte[] BuildPacket(byte actionCode, bool shifted)
    {
        var packet = new byte[16];
        packet[shifted ? 1 : 0] = 1;
        packet[shifted ? 10 : 9] = actionCode;
        return packet;
    }
}

public class Akp03eButtonImageProtocolTests
{
    [Fact]
    public void BuildUpload_UsesBatChunksAndStpPackets()
    {
        var jpeg = new byte[1100];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;

        var packets = Akp03eButtonImageProtocol.BuildUpload(5, jpeg);

        Assert.Equal(4, packets.Count);
        Assert.All(packets, packet => Assert.Equal(1025, packet.Length));
        Assert.Equal("CRT"u8.ToArray(), packets[0][1..4]);
        Assert.Equal("BAT"u8.ToArray(), packets[0][6..9]);
        Assert.Equal(0x04, packets[0][11]);
        Assert.Equal(0x4C, packets[0][12]);
        Assert.Equal(6, packets[0][13]);
        Assert.Equal(jpeg[..1024], packets[1][1..1025]);
        Assert.Equal(jpeg[1024..], packets[2][1..77]);
        Assert.Equal("STP"u8.ToArray(), packets[3][6..9]);
    }

    [Fact]
    public void BuildUpload_RejectsNonDisplayButton()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Akp03eButtonImageProtocol.BuildUpload(6, [0xFF, 0xD8]));
    }

    [Fact]
    public void BuildBrightnessPacket_UsesLigCommand()
    {
        var packet = Akp03eButtonImageProtocol.BuildBrightnessPacket(75);

        Assert.Equal(1025, packet.Length);
        Assert.Equal("LIG"u8.ToArray(), packet[6..9]);
        Assert.Equal(75, packet[11]);
    }

    [Fact]
    public void BuildClearButtonImages_UsesCleAndStpCommands()
    {
        var packets = Akp03eButtonImageProtocol.BuildClearButtonImages();

        Assert.Equal(2, packets.Count);
        Assert.All(packets, packet => Assert.Equal(1025, packet.Length));
        Assert.Equal("CLE"u8.ToArray(), packets[0][6..9]);
        Assert.Equal(0, packets[0][9]);
        Assert.Equal(0, packets[0][10]);
        Assert.Equal(0, packets[0][11]);
        Assert.Equal(0xFF, packets[0][12]);
        Assert.Equal("STP"u8.ToArray(), packets[1][6..9]);
    }

    [Fact]
    public void BuildSleepPackets_UsesShutdownAndHanCommands()
    {
        var packets = Akp03eButtonImageProtocol.BuildSleepPackets();

        Assert.Equal(2, packets.Count);
        Assert.All(packets, packet => Assert.Equal(1025, packet.Length));
        Assert.Equal("CLE"u8.ToArray(), packets[0][6..9]);
        Assert.Equal("DC"u8.ToArray(), packets[0][11..13]);
        Assert.Equal("HAN"u8.ToArray(), packets[1][6..9]);
    }
}
