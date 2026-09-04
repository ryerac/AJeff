using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using JeffDock.Core.Deck;
using JeffDock.PluginContracts;

namespace JeffDock.Plugins.System;

public sealed class SystemPlugin : IJeffDockPlugin
{
    public string Id => "jeffdock.system";
    public string DisplayName => "System";
    public Version Version => new(1, 0, 0);

    public void Register(IJeffDockPluginRegistry registry)
    {
        registry.AddAction(new LockWorkstationAction());
        registry.AddAction(new SleepAction());
        registry.AddAction(new RunApplicationAction());
        registry.AddAction(new RunCommandAction());
        registry.AddPresetJson(Presets);
    }

    private const string Presets = """
        {
          "version": 1,
          "sections": [
            {
              "id": "system",
              "name": "System",
              "pluginId": "jeffdock.system",
              "presets": [
                {
                  "id": "machine.lock",
                  "name": "Lock Computer",
                  "description": "Lock the current Windows session.",
                  "controlTypes": [ "Button", "Encoder" ],
                  "bindings": [ { "trigger": "Press", "actionId": "jeffdock.system.lock" } ],
                  "iconMode": "Static",
                  "iconId": "elgato/general/lock",
                  "iconForeground": "#FFFFFF",
                  "iconBackground": "#354052"
                },
                {
                  "id": "system.sleep",
                  "name": "Sleep Computer",
                  "description": "Put the computer to sleep immediately.",
                  "controlTypes": [ "Button", "Encoder" ],
                  "bindings": [ { "trigger": "Press", "actionId": "jeffdock.system.sleep" } ],
                  "iconMode": "Static",
                  "iconId": "elgato/general/power",
                  "iconForeground": "#FFFFFF",
                  "iconBackground": "#354052"
                },
                {
                  "id": "system.run-app",
                  "name": "Run App",
                  "description": "Launch an application, file, folder, or URL.",
                  "controlTypes": [ "Button", "Encoder" ],
                  "bindings": [ { "trigger": "Press", "actionId": "jeffdock.system.run-app" } ],
                  "iconMode": "Static",
                  "iconId": "elgato/general/apps",
                  "iconForeground": "#FFFFFF",
                  "iconBackground": "#2563A6"
                },
                {
                  "id": "system.run-command",
                  "name": "Run Command",
                  "description": "Run an advanced Windows command through cmd.exe.",
                  "controlTypes": [ "Button", "Encoder" ],
                  "bindings": [ { "trigger": "Press", "actionId": "jeffdock.system.run-command" } ],
                  "iconMode": "Static",
                  "iconId": "elgato/general/terminal",
                  "iconForeground": "#FFFFFF",
                  "iconBackground": "#5B3978"
                }
              ]
            }
          ]
        }
        """;
}

internal abstract class SystemActionBase : IPluginDeckAction
{
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _lastPressByControl = new(StringComparer.OrdinalIgnoreCase);

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public PluginActionGroup Group { get; } = new("system", "System");
    public virtual IReadOnlyList<PluginSettingDefinition> Parameters => [];

    public bool Supports(DeckInputEventType triggerEventType) =>
        triggerEventType is DeckInputEventType.ButtonPress or DeckInputEventType.EncoderPress;

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

        ExecuteCore(context);
    }

    protected abstract void ExecuteCore(PluginActionContext context);

    protected static string ReadParameter(PluginActionContext context, string key) =>
        context.Parameters.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
}

internal sealed class LockWorkstationAction : SystemActionBase
{
    public override string Id => "jeffdock.system.lock";
    public override string DisplayName => "Lock Computer";

    protected override void ExecuteCore(PluginActionContext context)
    {
        if (!LockWorkStation())
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not lock the workstation.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}

internal sealed class SleepAction : SystemActionBase
{
    public override string Id => "jeffdock.system.sleep";
    public override string DisplayName => "Sleep Computer";

    protected override void ExecuteCore(PluginActionContext context)
    {
        if (!SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not put the computer to sleep.");
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}

internal sealed class RunApplicationAction : SystemActionBase
{
    public override string Id => "jeffdock.system.run-app";
    public override string DisplayName => "Run App";
    public override IReadOnlyList<PluginSettingDefinition> Parameters { get; } =
    [
        new("target", "App, file, folder, or URL", "Enter an executable path, file, folder, or URL.", PluginSettingType.String, ""),
        new("arguments", "Arguments", "Optional command-line arguments passed to an application.", PluginSettingType.String, ""),
        new("workingDirectory", "Working directory", "Optional folder used as the application's working directory.", PluginSettingType.String, ""),
    ];

    protected override void ExecuteCore(PluginActionContext context)
    {
        var target = ReadParameter(context, "target");
        if (target.Length == 0) throw new InvalidOperationException("Run App requires an app, file, folder, or URL.");

        var startInfo = new ProcessStartInfo
        {
            FileName = target,
            Arguments = ReadParameter(context, "arguments"),
            UseShellExecute = true,
        };
        var workingDirectory = ReadParameter(context, "workingDirectory");
        if (workingDirectory.Length > 0) startInfo.WorkingDirectory = workingDirectory;

        Process.Start(startInfo);
    }
}

internal sealed class RunCommandAction : SystemActionBase
{
    public override string Id => "jeffdock.system.run-command";
    public override string DisplayName => "Run Command";
    public override IReadOnlyList<PluginSettingDefinition> Parameters { get; } =
    [
        new("command", "Command", "Runs through cmd.exe. Only use commands you trust, and do not include secrets.", PluginSettingType.String, ""),
        new("workingDirectory", "Working directory", "Optional folder in which to run the command.", PluginSettingType.String, ""),
        new("showWindow", "Show command window", "Show the cmd.exe window while the command runs.", PluginSettingType.Boolean, "false"),
    ];

    protected override void ExecuteCore(PluginActionContext context)
    {
        var command = ReadParameter(context, "command");
        if (command.Length == 0) throw new InvalidOperationException("Run Command requires a command.");

        var showWindow = bool.TryParse(ReadParameter(context, "showWindow"), out var show) && show;
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = $"/d /c {command}",
            UseShellExecute = false,
            CreateNoWindow = !showWindow,
            WindowStyle = showWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
        };

        var workingDirectory = ReadParameter(context, "workingDirectory");
        if (workingDirectory.Length > 0) startInfo.WorkingDirectory = workingDirectory;

        Process.Start(startInfo);
    }
}
