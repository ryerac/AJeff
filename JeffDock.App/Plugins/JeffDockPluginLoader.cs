using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using JeffDock.PluginContracts;

namespace JeffDock.App.Plugins;

internal sealed class JeffDockPluginLoader
{
    public JeffDockPluginRegistry Registry { get; } = new();
    public List<string> Diagnostics { get; } = [];
    public List<LoadedPlugin> Plugins { get; } = [];

    public void LoadAll()
    {
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
            Plugins.Add(new LoadedPlugin(plugin.Id, plugin.DisplayName, plugin.Version));
            Diagnostics.Add($"Loaded {plugin.DisplayName} {plugin.Version}.");
        }
        catch (Exception exception)
        {
            Diagnostics.Add($"Skipped {manifestPath}: {exception.Message}");
        }
    }

    private sealed record PluginManifest(string Id, int ApiVersion, string EntryAssembly, string EntryType);
}

internal sealed record LoadedPlugin(string Id, string DisplayName, Version Version);
