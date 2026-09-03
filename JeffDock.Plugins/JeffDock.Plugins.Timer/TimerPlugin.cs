using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using JeffDock.Core.Deck;
using JeffDock.PluginContracts;

namespace JeffDock.Plugins.Timer;

public sealed class TimerPlugin : IJeffDockPlugin
{
    public string Id => "jeffdock.timer";
    public string DisplayName => "Timer";
    public Version Version => new(1, 0, 0);

    public void Register(IJeffDockPluginRegistry registry)
    {
        var settings = registry.AddSettings(Id,
        [
            new PluginSettingDefinition(
                "durationSeconds",
                "Default duration",
                "The countdown duration used whenever the timer is started.",
                PluginSettingType.Integer,
                "60",
                Minimum: 5,
                Maximum: 86400,
                Suffix: "seconds"),
        ]);
        var controller = new TimerController(settings, registry.Notifications);
        registry.AddStateSource(controller);
        registry.AddAction(new StartTimerAction(controller));
        registry.AddPresetJson(Presets);
    }

    private const string Presets = """
        {
          "version": 1,
          "sections": [
            {
              "id": "timer",
              "name": "Timer",
              "presets": [
                {
                  "id": "timer.countdown",
                  "name": "Countdown Timer",
                  "description": "Start the countdown, or stop and reset it while running.",
                  "controlTypes": [ "Button" ],
                  "bindings": [ { "trigger": "Press", "actionId": "jeffdock.timer.start" } ],
                  "iconMode": "Dynamic",
                  "iconId": "elgato/general/timer"
                }
              ]
            }
          ]
        }
        """;
}

internal sealed class StartTimerAction(TimerController controller) : IPluginDeckAction
{
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _lastPressByControl = new(StringComparer.OrdinalIgnoreCase);

    public string Id => "jeffdock.timer.start";
    public string DisplayName => "Start / Stop Countdown";
    public PluginActionGroup Group { get; } = new("timer", "Timer");
    public PluginActionVisual Visual { get; } = new(
        TimerController.StateSourceId,
        [new("ready", "Ready", "elgato/general/timer"), new("running", "Running"), new("finished", "Finished")],
        IsImageManaged: true);
    public bool Supports(DeckInputEventType triggerEventType) => triggerEventType == DeckInputEventType.ButtonPress;

    public void Execute(PluginActionContext context)
    {
        var input = context.InputEvent;
        var key = $"{context.Device.DeviceId}|{input.ControlIndex}";
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (_lastPressByControl.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(1)) return;
            _lastPressByControl[key] = now;
        }
        controller.ToggleCountdown();
    }
}

internal sealed class TimerController : IPluginDeckStateSource
{
    public const string StateSourceId = "jeffdock.timer.state";
    private readonly object _sync = new();
    private readonly IPluginSettings _settings;
    private readonly IPluginNotifications _notifications;
    private System.Threading.Timer? _timer;
    private int _remainingSeconds;
    private byte[] _imageBytes;

    public TimerController(IPluginSettings settings, IPluginNotifications notifications)
    {
        _settings = settings;
        _notifications = notifications;
        _remainingSeconds = ReadDuration();
        _imageBytes = RenderTime(_remainingSeconds, finished: false);
        _settings.Changed += OnSettingChanged;
    }

    public string Id => StateSourceId;
    public string CurrentState { get; private set; } = "ready";
    public byte[]? CurrentImageBytes { get { lock (_sync) return _imageBytes.ToArray(); } }
    public event EventHandler<string>? StateChanged;
    public void Start() { }

    public void ToggleCountdown()
    {
        string state;
        lock (_sync)
        {
            if (_timer is not null)
            {
                _timer.Dispose();
                _timer = null;
                _remainingSeconds = ReadDuration();
                CurrentState = state = "ready";
                _imageBytes = RenderTime(_remainingSeconds, finished: false);
            }
            else
            {
                _remainingSeconds = ReadDuration();
                CurrentState = state = "running";
                _imageBytes = RenderTime(_remainingSeconds, finished: false);
                _timer = new System.Threading.Timer(Tick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
        }
        StateChanged?.Invoke(this, state);
    }

    private void Tick(object? state)
    {
        var finished = false;
        lock (_sync)
        {
            if (_timer is null) return;
            _remainingSeconds = Math.Max(0, _remainingSeconds - 1);
            finished = _remainingSeconds == 0;
            CurrentState = finished ? "finished" : "running";
            _imageBytes = RenderTime(_remainingSeconds, finished);
            if (finished)
            {
                _timer.Dispose();
                _timer = null;
            }
        }

        StateChanged?.Invoke(this, CurrentState);
        if (finished) _notifications.ShowAlert("Timer finished", "Your countdown has finished.");
    }

    private void OnSettingChanged(object? sender, PluginSettingChangedEventArgs e)
    {
        if (!string.Equals(e.Key, "durationSeconds", StringComparison.OrdinalIgnoreCase)) return;
        lock (_sync)
        {
            if (_timer is not null) return;
            _remainingSeconds = ReadDuration();
            CurrentState = "ready";
            _imageBytes = RenderTime(_remainingSeconds, finished: false);
        }
        StateChanged?.Invoke(this, "ready");
    }

    private int ReadDuration() => int.Parse(_settings.GetValue("durationSeconds"), CultureInfo.InvariantCulture);

    private static byte[] RenderTime(int seconds, bool finished)
    {
        using var bitmap = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(finished ? Color.FromArgb(170, 28, 38) : Color.FromArgb(20, 24, 33));

        var minutes = seconds / 60;
        var remainder = seconds % 60;
        using var minuteFont = new Font("Segoe UI", 35, FontStyle.Bold, GraphicsUnit.Pixel);
        using var secondFont = new Font("Segoe UI", 30, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelFont = new Font("Segoe UI", 16, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        using var mutedBrush = new SolidBrush(Color.FromArgb(190, 210, 218, 230));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString($"{minutes}m", minuteFont, brush, new RectangleF(0, 13, 128, 45), format);
        graphics.DrawString($"{remainder}s", secondFont, brush, new RectangleF(0, 52, 128, 42), format);
        graphics.DrawString(finished ? "DONE" : "TIMER", labelFont, mutedBrush, new RectangleF(0, 94, 128, 25), format);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingChanged;
        lock (_sync) { _timer?.Dispose(); _timer = null; }
    }
}
