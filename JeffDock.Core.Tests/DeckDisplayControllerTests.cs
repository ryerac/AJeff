using JeffDock.Core.Akp03e;
using JeffDock.Core.Deck;

namespace JeffDock.Core.Tests;

public class DeckDisplayControllerTests
{
    private readonly Akp03eProfile _profile = new();
    private readonly List<byte[]> _packets = [];
    private DeckDisplayController Create(DeckDisplaySettings? settings = null) =>
        new(_profile, _packets.Add, settings ?? new(SleepEnabled: true, SleepMinutes: 1), 0);

    [Theory]
    [InlineData(DeckSleepBehaviour.Dim)]
    [InlineData(DeckSleepBehaviour.ScreenOff)]
    public void DimKeepsImagesLiveScreenOffDefersAndWakeRestoresBrightness(DeckSleepBehaviour behaviour)
    {
        var display = Create(new(37, true, 1, behaviour));
        display.Tick(59_999);
        Assert.Empty(_packets);
        display.Tick(60_000);
        var expectedSleep = behaviour == DeckSleepBehaviour.Dim
            ? new[] { _profile.BuildBrightnessPacket(1) } : _profile.BuildSleepPackets();
        AssertPackets(expectedSleep, _packets);

        _packets.Clear();
        byte[] image = [0xFF, 0xD8, 0x23, 0xFF, 0xD9];
        display.UpdateImages(new Dictionary<int, byte[]> { [0] = image });
        if (behaviour == DeckSleepBehaviour.Dim)
            AssertPackets(_profile.BuildClearButtonImages()
                .Concat(_profile.BuildButtonImageUpload(0, image))
                .Append(_profile.BuildBrightnessPacket(1)), _packets);
        else
            Assert.Empty(_packets);
        _packets.Clear();
        Assert.False(display.HandleInput(new(DeckInputEventType.ButtonPress, 0, 0), 61_000));
        AssertPackets(new[] { _profile.InitializePacket }
            .Concat(_profile.BuildClearButtonImages())
            .Concat(_profile.BuildButtonImageUpload(0, image))
            .Append(_profile.BuildBrightnessPacket(37)), _packets);
        Assert.True(display.HandleInput(new(DeckInputEventType.ButtonPress, 0, 0), 61_500));
    }

    [Theory]
    [InlineData(DeckInputEventType.ButtonPress)]
    [InlineData(DeckInputEventType.EncoderPress)]
    [InlineData(DeckInputEventType.EncoderTurn)]
    public void WakeConsumesGestureIncludingRepeatedReportsUntilQuiet(DeckInputEventType type)
    {
        var display = Create();
        display.Tick(60_000);
        var input = new DeckInputEvent(type, 0, 1);
        Assert.False(display.HandleInput(input, 60_100));
        Assert.False(display.HandleInput(input, 60_400));
        Assert.False(display.HandleInput(input, 60_800));
        // Other controls remain usable, but do not release the repeat guard.
        Assert.True(display.HandleInput(input with { ControlIndex = 1 }, 60_900));
        Assert.False(display.HandleInput(input with { Direction = -1 }, 61_000));
        Assert.True(display.HandleInput(input, 61_500));
    }

    [Fact]
    public void ActivityRestartsTimerButImageUpdatesDoNot()
    {
        var display = Create();
        Assert.True(display.HandleInput(new(DeckInputEventType.EncoderTurn, 0, 1), 30_000));
        display.Tick(60_000);
        Assert.Empty(_packets);
        display.UpdateImages(new Dictionary<int, byte[]>());
        _packets.Clear();
        display.Tick(90_000);
        Assert.Single(_packets);
    }

    [Fact]
    public void DisabledSleepNeverIdlesAndDisablingWhileAsleepRestoresDisplay()
    {
        var display = Create(new(SleepEnabled: false));
        display.Tick(100_000_000);
        Assert.Empty(_packets);
        display.Configure(new(42, true, 1), 100_000_000);
        display.Tick(100_060_000);
        _packets.Clear();
        display.Configure(new(29, false), 100_070_000);
        Assert.Equal(_profile.BuildBrightnessPacket(29), _packets.Last());
        Assert.True(display.HandleInput(new(DeckInputEventType.ButtonPress, 0, 0), 100_070_001));
    }

    [Fact]
    public void SuspendBlocksInputAndUploadsAndResumeUsesSavedBrightness()
    {
        var display = Create();
        display.Suspend();
        _packets.Clear();
        display.Configure(new(23, true, 1), 10);
        display.UpdateImages(new Dictionary<int, byte[]>());
        display.Tick(100_000);
        Assert.False(display.HandleInput(new(DeckInputEventType.ButtonPress, 0, 0), 100_000));
        Assert.Empty(_packets);
        display.Resume(100_000);
        Assert.Equal(_profile.BuildBrightnessPacket(23), _packets.Last());
        _packets.Clear();
        display.Tick(159_999);
        Assert.Empty(_packets);
        display.Tick(160_000);
        Assert.Single(_packets);
    }

    [Fact]
    public void FailedWakeNeverDispatchesActionAndCanRetry()
    {
        var fail = false;
        var display = new DeckDisplayController(_profile, packet =>
        {
            if (fail) throw new IOException("Device unavailable");
            _packets.Add(packet);
        }, new(SleepEnabled: true, SleepMinutes: 1), 0);
        display.Tick(60_000);
        fail = true;
        Assert.Throws<IOException>(() => display.HandleInput(new(DeckInputEventType.ButtonPress, 0, 0), 61_000));
        fail = false;
        Assert.False(display.HandleInput(new(DeckInputEventType.ButtonPress, 0, 0), 62_000));
    }

    [Fact]
    public void AwakeUploadsUseConfiguredBrightnessAndUnknownInputDoesNotWake()
    {
        var display = Create(new(19, true, 1));
        display.UpdateImages(new Dictionary<int, byte[]>());
        Assert.Equal(_profile.BuildBrightnessPacket(19), _packets.Last());
        display.Tick(60_000);
        _packets.Clear();
        Assert.False(display.HandleInput(default, 61_000));
        Assert.Empty(_packets);
    }

    private static void AssertPackets(IEnumerable<byte[]> expected, IEnumerable<byte[]> actual) =>
        Assert.Equal(expected.Select(Convert.ToHexString), actual.Select(Convert.ToHexString));
}
