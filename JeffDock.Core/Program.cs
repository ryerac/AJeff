using JeffDock.Core.Audio;
using JeffDock.Core.Deck;

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
	e.Cancel = true;
	cts.Cancel();
};

var profiles = DeckProfileCatalog.SupportedProfiles;

using var deviceService = new HidDeckDeviceService(profiles);
var volume = new WindowsVolumeController();
var lastTopKnobPressUtc = DateTime.MinValue;

Console.WriteLine("JeffDock.Core started.");
Console.WriteLine($"Looking for supported decks: {string.Join(", ", profiles.Select(p => p.Name))}");
Console.WriteLine("Press Ctrl+C to quit.");

while (!cts.IsCancellationRequested)
{
	if (!deviceService.IsConnected)
	{
		if (deviceService.TryConnect())
		{
			Console.WriteLine($"Device connected: {deviceService.ActiveDeviceName}");
		}
		else
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
			}
			catch (TaskCanceledException)
			{
				break;
			}
			continue;
		}
	}

	var evt = deviceService.ReadEvent(cts.Token);
	if (evt.Type == DeckInputEventType.Unknown)
	{
		continue;
	}

	Console.WriteLine($"Event: {evt.Type} idx={evt.ControlIndex} dir={evt.Direction}");

	// AKP03E index 1 is the large top knob.
	if (evt.ControlIndex == 1 && evt.Type == DeckInputEventType.EncoderTurn)
	{
		volume.NudgeVolume(evt.Direction);
	}

	if (evt.ControlIndex == 1 && evt.Type == DeckInputEventType.EncoderPress)
	{
		var now = DateTime.UtcNow;
		if ((now - lastTopKnobPressUtc).TotalMilliseconds > 250)
		{
			volume.ToggleMute();
			lastTopKnobPressUtc = now;
		}
	}
}

deviceService.Disconnect();
Console.WriteLine("Stopped.");
