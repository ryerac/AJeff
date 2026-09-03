using System.Runtime.InteropServices;
using JeffDock.Core.Deck;
using JeffDock.PluginContracts;

namespace JeffDock.Plugins.MouseMover;

public sealed class MouseMoverPlugin : IJeffDockPlugin
{
    public string Id => "jeffdock.mouse-mover";
    public string DisplayName => "Mouse Mover";
    public Version Version => new(1, 0, 0);

    public void Register(IJeffDockPluginRegistry registry)
    {
        var settings = registry.AddSettings(Id,
        [
            new PluginSettingDefinition(
                "intervalSeconds",
                "Movement interval",
                "How often the pointer moves while Mouse Mover is running.",
                PluginSettingType.Integer,
                "30",
                Minimum: 5,
                Maximum: 3600,
                Suffix: "seconds"),
        ]);
        var controller = new MouseMoverController(settings);
        registry.AddStateSource(controller);
        registry.AddAction(new ToggleMouseMoverAction(controller));
        registry.AddPresetJson(Presets);
    }

    private const string Presets = """
        {
          "version": 1,
          "sections": [
            {
              "id": "mouse-mover",
              "name": "Mouse Mover",
              "presets": [
                {
                  "id": "mouse-mover.toggle",
                  "name": "Toggle Mouse Mover",
                  "description": "Start or stop periodic mouse movement.",
                  "controlTypes": [ "Button", "Encoder" ],
                  "bindings": [ { "trigger": "Press", "actionId": "jeffdock.mouse-mover.toggle" } ],
                  "iconMode": "Dynamic"
                }
              ]
            }
          ]
        }
        """;
}

internal sealed class ToggleMouseMoverAction(MouseMoverController controller) : IPluginDeckAction
{
    private static readonly TimeSpan PressCooldown = TimeSpan.FromSeconds(1);
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _lastPressByControl = new(StringComparer.OrdinalIgnoreCase);

    public string Id => "jeffdock.mouse-mover.toggle";
    public string DisplayName => "Toggle Mouse Mover";
    public PluginActionGroup Group { get; } = new("mouse-mover", "Mouse Mover");
    public PluginActionVisual Visual { get; } = new(
        MouseMoverController.StateSourceId,
        [new("stopped", "Stopped", "elgato/general/mouse"), new("running", "Running", "elgato/general/mouse--filled")]);
    public bool Supports(DeckInputEventType triggerEventType) =>
        triggerEventType is DeckInputEventType.ButtonPress or DeckInputEventType.EncoderPress;
    public void Execute(PluginActionContext context)
    {
        var input = context.InputEvent;
        var control = $"{context.Device.DeviceId}|{input.Type}|{input.ControlIndex}";
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (_lastPressByControl.TryGetValue(control, out var lastPress)
                && now - lastPress < PressCooldown)
            {
                return;
            }

            _lastPressByControl[control] = now;
        }

        controller.Toggle();
    }
}

internal sealed class MouseMoverController : IPluginDeckStateSource
{
    public const string StateSourceId = "jeffdock.mouse-mover.state";
    private readonly object _sync = new();
    private Timer? _timer;
    private readonly IPluginSettings _settings;

    public MouseMoverController(IPluginSettings settings)
    {
        _settings = settings;
        _settings.Changed += OnSettingChanged;
    }

    public string Id => StateSourceId;
    public string CurrentState { get; private set; } = "stopped";
    public event EventHandler<string>? StateChanged;
    public void Start() { }

    public void Toggle()
    {
        string state;
        lock (_sync)
        {
            if (_timer is null)
            {
                _timer = CreateTimer();
                state = CurrentState = "running";
            }
            else
            {
                _timer.Dispose();
                _timer = null;
                state = CurrentState = "stopped";
            }
        }
        StateChanged?.Invoke(this, state);
    }

    private static void MoveMouse(object? state)
    {
        if (!GetCursorPos(out var original))
        {
            return;
        }

        SetCursorPos(original.X + 6, original.Y);
        Thread.Sleep(150);
        SetCursorPos(original.X, original.Y);
    }

    private Timer CreateTimer() => new(
        MoveMouse,
        null,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(int.Parse(_settings.GetValue("intervalSeconds"), System.Globalization.CultureInfo.InvariantCulture)));

    private void OnSettingChanged(object? sender, PluginSettingChangedEventArgs e)
    {
        if (!string.Equals(e.Key, "intervalSeconds", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_sync)
        {
            if (_timer is not null)
            {
                _timer.Dispose();
                _timer = CreateTimer();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
            CurrentState = "stopped";
        }
        _settings.Changed -= OnSettingChanged;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        public int X { get; init; }
        public int Y { get; init; }
    }
}
