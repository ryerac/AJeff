using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows.Media;
using JeffDock.App.Icons;
using JeffDock.PluginContracts;
using JeffDock.Plugins.Game;
using JeffDock.Plugins.MouseMover;
using JeffDock.Plugins.PiHole;
using JeffDock.Plugins.Timer;

namespace JeffDock.App.Plugins;

internal sealed class JeffDockPluginLoader
{
    public JeffDockPluginRegistry Registry { get; } = new();
    public List<string> Diagnostics { get; } = [];
    public List<LoadedPlugin> Plugins { get; } = [];

    public void LoadAll()
    {
        LoadBuiltIn(new MouseMoverPlugin());
        LoadBuiltIn(new TimerPlugin());
        LoadBuiltIn(new GamePlugin());
        LoadBuiltIn(new PiHolePlugin(), "JeffDock.Plugins.PiHole.icon.svg");

        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Plugins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JeffDock", "Plugins"),
        };

        foreach (var manifestPath in roots.Where(Directory.Exists).SelectMany(root => Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories)))
        {
            Load(manifestPath);
        }
    }

    private void LoadBuiltIn(IJeffDockPlugin plugin, string? iconResourceName = null)
    {
        try
        {
            plugin.Register(Registry);
            var icon = iconResourceName is null
                ? null
                : LoadIcon(plugin.GetType().Assembly, iconResourceName);
            Plugins.Add(new LoadedPlugin(plugin.Id, plugin.DisplayName, plugin.Version, icon));
            Diagnostics.Add($"Loaded built-in {plugin.DisplayName} {plugin.Version}.");
        }
        catch (Exception exception)
        {
            Diagnostics.Add($"Could not load built-in {plugin.DisplayName}: {exception.Message}");
        }
    }

    private void Load(string manifestPath)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidDataException("Empty plugin manifest.");
            if (manifest.ApiVersion != 1)
            {
                throw new InvalidDataException($"Unsupported API version {manifest.ApiVersion}.");
            }
            if (Plugins.Any(plugin => string.Equals(plugin.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"Plugin ID '{manifest.Id}' is already loaded.");
            }

            var directory = Path.GetDirectoryName(manifestPath)!;
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(directory, manifest.EntryAssembly)));
            var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
            var plugin = (IJeffDockPlugin)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Could not create plugin entry point."));
            if (!string.Equals(plugin.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Manifest and plugin IDs do not match.");
            }

            plugin.Register(Registry);
            Plugins.Add(new LoadedPlugin(plugin.Id, plugin.DisplayName, plugin.Version, LoadIcon(directory, manifest.Icon)));
            Diagnostics.Add($"Loaded {plugin.DisplayName} {plugin.Version}.");
        }
        catch (Exception exception)
        {
            Diagnostics.Add($"Skipped {manifestPath}: {exception.Message}");
        }
    }

    private ImageSource? LoadIcon(Assembly assembly, string resourceName)
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Embedded plugin icon '{resourceName}' was not found.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var imageBytes = SvgIconRenderer.RenderPng(memory.ToArray(), "#FFFFFF", null, size: 96);
            return SvgIconRenderer.ToBitmapSource(imageBytes, decodeWidth: 96);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException)
        {
            Diagnostics.Add($"Could not load embedded plugin icon '{resourceName}': {exception.Message}");
            return null;
        }
    }

    private ImageSource? LoadIcon(string pluginDirectory, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        try
        {
            var root = Path.GetFullPath(pluginDirectory) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(pluginDirectory, relativePath));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Plugin icon must be inside the plugin directory.");
            var bytes = File.ReadAllBytes(path);
            var imageBytes = string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase)
                ? SvgIconRenderer.RenderPng(bytes, "#FFFFFF", null, size: 96)
                : bytes;
            return SvgIconRenderer.ToBitmapSource(imageBytes, decodeWidth: 96);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            Diagnostics.Add($"Could not load plugin icon '{relativePath}': {exception.Message}");
            return null;
        }
    }

    private sealed record PluginManifest(string Id, int ApiVersion, string EntryAssembly, string EntryType, string? Icon = null);
}

internal sealed record LoadedPlugin(string Id, string DisplayName, Version Version, ImageSource? Icon);
