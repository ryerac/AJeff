using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace JeffDock.App.Settings;

internal sealed class ApplicationSettingsStore
{
    private const string StartupValueName = "AJeff";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _path;

    public ApplicationSettingsStore()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JeffDock",
            "application-settings.json");

        StartMinimized = Load().StartMinimized;
    }

    public bool StartWithWindows => IsStartupRegistered();
    public bool StartMinimized { get; private set; }

    public void Save(bool startWithWindows, bool startMinimized)
    {
        UpdateStartupRegistration(startWithWindows, startMinimized);
        StartMinimized = startMinimized;

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(
            new SettingsDocument(1, startMinimized),
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, _path, true);
    }

    private SettingsDocument Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(_path)) ?? new(1, false)
                : new(1, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(1, false);
        }
    }

    private static bool IsStartupRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(StartupValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    private static void UpdateStartupRegistration(bool enabled, bool startMinimized)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows startup settings could not be opened.");

        if (!enabled)
        {
            key.DeleteValue(StartupValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current AJeff executable path could not be determined.");
        var command = $"\"{executablePath}\"";
        if (startMinimized)
        {
            command += " --minimized";
        }
        key.SetValue(StartupValueName, command, RegistryValueKind.String);
    }

    private sealed record SettingsDocument(int Version, bool StartMinimized);
}
