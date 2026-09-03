using JeffDock.PluginContracts;

namespace JeffDock.App.Plugins;

internal sealed class JeffDockPluginRegistry : IJeffDockPluginRegistry
{
    public IPluginNotifications Notifications { get; } = new WpfPluginNotifications();
    public List<IPluginDeckAction> Actions { get; } = [];
    public List<IPluginDeckStateSource> StateSources { get; } = [];
    public List<string> PresetJson { get; } = [];
    public Dictionary<string, PluginSettingsStore> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddAction(IPluginDeckAction action) => Actions.Add(action);
    public void AddStateSource(IPluginDeckStateSource stateSource) => StateSources.Add(stateSource);
    public void AddPresetJson(string json) => PresetJson.Add(json);

    public IPluginSettings AddSettings(string pluginId, IReadOnlyList<PluginSettingDefinition> definitions)
    {
        if (Settings.ContainsKey(pluginId))
        {
            throw new InvalidOperationException($"Settings are already registered for '{pluginId}'.");
        }

        var settings = new PluginSettingsStore(pluginId, definitions);
        Settings.Add(pluginId, settings);
        return settings;
    }
}

internal sealed class WpfPluginNotifications : IPluginNotifications
{
    public void ShowAlert(string title, string message)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current.MainWindow,
                message,
                title,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information));
    }
}
