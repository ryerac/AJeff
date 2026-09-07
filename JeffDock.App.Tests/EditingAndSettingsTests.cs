using System.IO;
using JeffDock.App.Bindings;
using JeffDock.App.Settings;
using JeffDock.Core.Akp03e;
using JeffDock.Core.Deck;

namespace JeffDock.App.Tests;

public sealed class EditingAndSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AJeffTests", Guid.NewGuid().ToString("N"));
    private static MonitoredDeckDevice Device(string id = "test") => new(id, "AJAZZ AKP03E", "test-path", null, new Akp03eProfile().Layout);

    [Fact]
    public void CopyCapturesBothDialBindingsAndParametersAcrossScenesWithoutSharing()
    {
        var store = new DeckBindingStore(_directory);
        var device = Device();
        store.SetAction(device, DeckControlType.Encoder, 0, DeckInputEventType.EncoderTurn, "turn-action");
        store.SetActionParameter(device, DeckControlType.Encoder, 0, DeckInputEventType.EncoderTurn, "step", "3");
        store.SetAction(device, DeckControlType.Encoder, 0, DeckInputEventType.EncoderPress, "press-action");
        store.SetActionParameter(device, DeckControlType.Encoder, 0, DeckInputEventType.EncoderPress, "target", "original");
        var snapshot = store.CaptureControl(device, DeckControlType.Encoder, 0);
        store.SetActionParameter(device, DeckControlType.Encoder, 0, DeckInputEventType.EncoderPress, "target", "source-edited");
        store.CreateScene(device, "Destination");
        store.ApplyControlPreset(device, DeckControlType.Encoder, 1, snapshot, null);
        Assert.Equal("turn-action", store.GetActionId(device, DeckControlType.Encoder, 1, DeckInputEventType.EncoderTurn));
        Assert.Equal("3", store.GetActionParameters(device, DeckControlType.Encoder, 1, DeckInputEventType.EncoderTurn)["step"]);
        Assert.Equal("original", store.GetActionParameters(device, DeckControlType.Encoder, 1, DeckInputEventType.EncoderPress)["target"]);
        store.SetActionParameter(device, DeckControlType.Encoder, 1, DeckInputEventType.EncoderPress, "target", "paste-edited");
        Assert.Equal("original", snapshot.Single(binding => binding.TriggerEventType == DeckInputEventType.EncoderPress).Parameters!["target"]);
        store.SetActiveScene(device, DeckScene.DefaultId);
        Assert.Equal("source-edited", store.GetActionParameters(device, DeckControlType.Encoder, 0, DeckInputEventType.EncoderPress)["target"]);
        var reloaded = new DeckBindingStore(_directory);
        Assert.Equal("turn-action", reloaded.GetActionId(device, DeckControlType.Encoder, 0, DeckInputEventType.EncoderTurn));
    }

    [Fact]
    public void PasteReplacesOldParametersAndPreservesDynamicModeAcrossDevices()
    {
        var store = new DeckBindingStore(_directory);
        var source = Device();
        var target = Device("other");
        store.SetAction(source, DeckControlType.Button, 0, DeckInputEventType.ButtonPress, "stateful-action");
        store.SetIconMode(source, 0, DeckIconMode.Dynamic);
        store.SetActionParameter(target, DeckControlType.Button, 1, DeckInputEventType.ButtonPress, "obsolete", "remove-me");
        store.ApplyControlPreset(target, DeckControlType.Button, 1,
            store.CaptureControl(source, DeckControlType.Button, 0), store.GetIconMode(source, 0));
        var reloaded = new DeckBindingStore(_directory);
        Assert.Equal("stateful-action", reloaded.GetActionId(target, DeckControlType.Button, 1, DeckInputEventType.ButtonPress));
        Assert.Empty(reloaded.GetActionParameters(target, DeckControlType.Button, 1, DeckInputEventType.ButtonPress));
        Assert.Equal(DeckIconMode.Dynamic, reloaded.GetIconMode(target, 1));
    }

    [Fact]
    public void IconSnapshotKeepsAllStateFilesAndStaticArtworkIndependentAndRemovesOldStates()
    {
        var icons = new DeckIconStore(_directory);
        var original = new ControlIconSnapshot([1, 2, 3], new Dictionary<string, byte[]>
        {
            ["muted.jpg"] = [4, 5], ["state-abcdef.jpg"] = [6, 7],
        });
        icons.ReplaceControlIcons("source", "default", 0, original);
        var copied = icons.CaptureControlIcons("source", "default", 0);
        icons.ReplaceControlIcons("target", "other-scene", 2,
            new(null, new Dictionary<string, byte[]> { ["obsolete.jpg"] = [0] }));
        icons.ReplaceControlIcons("target", "other-scene", 2, copied);
        icons.ReplaceControlIcons("source", "default", 0, new(null, new Dictionary<string, byte[]>()));
        var pasted = icons.CaptureControlIcons("target", "other-scene", 2);
        Assert.Equal(original.StaticImage, pasted.StaticImage);
        Assert.Equal(2, pasted.StateImages.Count);
        Assert.Equal(original.StateImages["muted.jpg"], pasted.StateImages["muted.jpg"]);
        Assert.Equal(original.StateImages["state-abcdef.jpg"], pasted.StateImages["state-abcdef.jpg"]);
        Assert.Empty(icons.CaptureControlIcons("source", "default", 0).StateImages);
    }

    [Fact]
    public void PasteRejectsDifferentControlTypesAndArtworkOnScreenlessButtons()
    {
        var layout = Device().Layout;
        var snapshot = new ControlConfigurationSnapshot(DeckControlType.Button, [], DeckIconMode.Dynamic,
            new(null, new Dictionary<string, byte[]>()));
        Assert.True(snapshot.CanPasteTo(layout.Controls.First(control => control.CanHaveIcon)));
        Assert.False(snapshot.CanPasteTo(layout.Controls.First(control => control.ControlType == DeckControlType.Encoder)));
        Assert.False(snapshot.CanPasteTo(layout.Controls.First(control => control.ControlType == DeckControlType.Button && !control.CanHaveIcon)));
    }

    [Fact]
    public void DisplaySettingsRoundTripPerDeviceAndRejectInvalidValuesWithoutOverwriting()
    {
        var path = Path.Combine(_directory, "display-settings.json");
        var store = new DisplaySettingsStore(path);
        Assert.False(store.Get("new-device").SleepEnabled);
        store.Save("one", new(31, true, 7, DeckSleepBehaviour.ScreenOff));
        store.Save("two", new(68, false, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save("one", new(SleepMinutes: 0)));
        var reloaded = new DisplaySettingsStore(path);
        Assert.Equal(new(31, true, 7, DeckSleepBehaviour.ScreenOff), reloaded.Get("one"));
        Assert.Equal(new(68, false, 20), reloaded.Get("two"));
    }

    [Fact]
    public void InvalidDeviceSettingsDoNotDiscardValidDevices()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "display-settings.json");
        File.WriteAllText(path, """{"invalid":{"Brightness":999},"valid":{"Brightness":42}}""");
        var store = new DisplaySettingsStore(path);
        Assert.Equal(80, store.Get("invalid").Brightness);
        Assert.Equal(42, store.Get("valid").Brightness);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
