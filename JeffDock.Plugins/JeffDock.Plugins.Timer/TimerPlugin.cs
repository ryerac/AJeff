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
    public IReadOnlyList<PluginSettingDefinition> Parameters { get; } =
    [
        new PluginSettingDefinition(
            "durationSeconds", "Duration override",
            "Optional duration for this button. Disable the override to use the plugin default.",
            PluginSettingType.Integer, "60", Minimum: 5, Maximum: 86400, Suffix: "seconds"),
    ];
    public bool Supports(DeckInputEventType triggerEventType) => triggerEventType == DeckInputEventType.ButtonPress;

    public void Execute(PluginActionContext context)
    {
        var input = context.InputEvent;
        var key = $"{context.Device.DeviceId}|{context.SceneId}|{input.ControlIndex}";
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (_lastPressByControl.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(1)) return;
            _lastPressByControl[key] = now;
        }
        controller.ToggleCountdown(new TimerInstanceKey(context.Device.DeviceId, context.SceneId, input.ControlIndex), context.Parameters);
    }
}

internal sealed class TimerController : IPluginDeckStateSource
{
    public const string StateSourceId = "jeffdock.timer.state";
    private readonly object _sync = new();
    private readonly IPluginSettings _settings;
    private readonly IPluginNotifications _notifications;
    private readonly Dictionary<TimerInstanceKey, TimerInstance> _instances = [];

    public TimerController(IPluginSettings settings, IPluginNotifications notifications)
    {
        _settings = settings;
        _notifications = notifications;
        _settings.Changed += OnSettingChanged;
    }

    public string Id => StateSourceId;
    public string CurrentState => "ready";
    public string GetCurrentState(PluginVisualContext context)
    {
        lock (_sync) return _instances.GetValueOrDefault(ToKey(context))?.State ?? "ready";
    }
    public byte[]? GetCurrentImageBytes(PluginVisualContext context)
    {
        lock (_sync)
        {
            var key = ToKey(context);
            var duration = ReadDuration(context.Parameters);
            if (_instances.TryGetValue(key, out var instance))
            {
                if (instance.Timer is null && instance.State == "ready" && instance.RemainingSeconds != duration)
                {
                    instance = new TimerInstance(duration, "ready", RenderTime(duration, false), null, context.Parameters.ContainsKey("durationSeconds"));
                    _instances[key] = instance;
                }
                return instance.ImageBytes.ToArray();
            }
            return RenderTime(duration, finished: false);
        }
    }
    public event EventHandler<string>? StateChanged;
    public void Start() { }

    public void ToggleCountdown(TimerInstanceKey key, IReadOnlyDictionary<string, string> parameters)
    {
        string state;
        lock (_sync)
        {
            if (_instances.TryGetValue(key, out var existing) && existing.Timer is not null)
            {
                existing.Timer.Dispose();
                var duration = ReadDuration(parameters);
                _instances[key] = new TimerInstance(duration, "ready", RenderTime(duration, false), null, parameters.ContainsKey("durationSeconds"));
                state = "ready";
            }
            else
            {
                var duration = ReadDuration(parameters);
                var timer = new System.Threading.Timer(_ => Tick(key), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                _instances[key] = new TimerInstance(duration, "running", RenderTime(duration, false), timer, parameters.ContainsKey("durationSeconds"));
                state = "running";
            }
        }
        StateChanged?.Invoke(this, state);
    }

    private void Tick(TimerInstanceKey key)
    {
        var finished = false;
        var nextState = "running";
        lock (_sync)
        {
            if (!_instances.TryGetValue(key, out var instance) || instance.Timer is null) return;
            var remaining = Math.Max(0, instance.RemainingSeconds - 1);
            finished = remaining == 0;
            nextState = finished ? "finished" : "running";
            if (finished)
            {
                instance.Timer.Dispose();
            }
            _instances[key] = new TimerInstance(remaining, nextState, RenderTime(remaining, finished), finished ? null : instance.Timer, instance.UsesOverride);
        }

        StateChanged?.Invoke(this, nextState);
        if (finished) _notifications.ShowAlert("Timer finished", "Your countdown has finished.");
    }

    private void OnSettingChanged(object? sender, PluginSettingChangedEventArgs e)
    {
        if (!string.Equals(e.Key, "durationSeconds", StringComparison.OrdinalIgnoreCase)) return;
        lock (_sync)
        {
            foreach (var (key, instance) in _instances.Where(item => item.Value.Timer is null && !item.Value.UsesOverride).ToList())
            {
                var duration = ReadDuration();
                _instances[key] = new TimerInstance(duration, "ready", RenderTime(duration, false), null, false);
            }
        }
        StateChanged?.Invoke(this, "ready");
    }

    private int ReadDuration() => int.Parse(_settings.GetValue("durationSeconds"), CultureInfo.InvariantCulture);
    private int ReadDuration(IReadOnlyDictionary<string, string> parameters) =>
        parameters.TryGetValue("durationSeconds", out var value)
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : ReadDuration();
    private static TimerInstanceKey ToKey(PluginVisualContext context) => new(context.DeviceId, context.SceneId, context.ControlIndex);

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
        lock (_sync)
        {
            foreach (var instance in _instances.Values) instance.Timer?.Dispose();
            _instances.Clear();
        }
    }

    private sealed record TimerInstance(int RemainingSeconds, string State, byte[] ImageBytes, System.Threading.Timer? Timer, bool UsesOverride);
}

internal readonly record struct TimerInstanceKey(string DeviceId, string SceneId, int ControlIndex);
