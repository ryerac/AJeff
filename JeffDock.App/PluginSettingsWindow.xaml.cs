using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using JeffDock.App.Plugins;
using JeffDock.App.Settings;
using JeffDock.Core.Deck;
using JeffDock.PluginContracts;

namespace JeffDock.App;

public partial class PluginSettingsWindow : Window
{
    private readonly JeffDockPluginLoader _loader;
    private readonly ApplicationSettingsStore _applicationSettings = new();
    private readonly DisplaySettingsStore _displaySettings;
    private readonly Action<string, DeckDisplaySettings>? _previewDisplay;
    private string? _previewDeviceId;
    private bool _loadingDisplaySettings;
    private readonly Dictionary<string, Control> _editors = new(StringComparer.OrdinalIgnoreCase);
    private LoadedPlugin? _selectedPlugin;
    private PluginSettingsStore? _settings;

    internal PluginSettingsWindow(JeffDockPluginLoader loader, DisplaySettingsStore displaySettings,
        IReadOnlyList<MonitoredDeckDevice> devices, string? selectedDeviceId,
        Action<string, DeckDisplaySettings>? previewDisplay = null)
    {
        _displaySettings = displaySettings;
        _previewDisplay = previewDisplay;
        InitializeComponent();
        _loader = loader;
        var displayDevices = devices.Where(device => device.Layout.Controls.Any(control => control.CanHaveIcon)).ToList();
        DisplayDeviceComboBox.ItemsSource = displayDevices;
        DisplayDeviceComboBox.SelectedItem = displayDevices.FirstOrDefault(device => device.DeviceId == selectedDeviceId)
            ?? displayDevices.FirstOrDefault();
        DisplayControlsPanel.IsEnabled = displayDevices.Count > 0;
        NoDisplayDeviceText.Visibility = displayDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StartWithWindowsCheckBox.IsChecked = _applicationSettings.StartWithWindows;
        StartMinimizedCheckBox.IsChecked = _applicationSettings.StartMinimized;
        UpdateStartMinimizedAvailability();
        PluginList.ItemsSource = loader.Plugins;
        DiagnosticsText.Text = string.Join(Environment.NewLine, loader.Diagnostics);
        PluginList.SelectedIndex = loader.Plugins.Count > 0 ? 0 : -1;
        if (loader.Plugins.Count == 0)
        {
            PluginTitle.Text = "No plugins loaded";
            SaveButton.IsEnabled = EditJsonButton.IsEnabled = false;
        }
    }

    private void DisplayDeviceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RestoreBrightnessPreview();
        if (DisplayDeviceComboBox.SelectedItem is not MonitoredDeckDevice device) return;
        var settings = _displaySettings.Get(device.DeviceId);
        _loadingDisplaySettings = true;
        try { BrightnessSlider.Value = settings.Brightness; }
        finally { _loadingDisplaySettings = false; }
        DisplaySleepCheckBox.IsChecked = settings.SleepEnabled;
        SleepMinutesTextBox.Text = settings.SleepMinutes.ToString(CultureInfo.InvariantCulture);
        SleepBehaviourComboBox.SelectedIndex = (int)settings.SleepBehaviour;
        DisplaySleepOptionsPanel.IsEnabled = settings.SleepEnabled;
    }

    private void BrightnessSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingDisplaySettings || _previewDisplay is null
            || DisplayDeviceComboBox?.SelectedItem is not MonitoredDeckDevice device) return;
        _previewDeviceId = device.DeviceId;
        // Only preview brightness; unsaved sleep settings stay in the editor.
        _previewDisplay(device.DeviceId, _displaySettings.Get(device.DeviceId) with { Brightness = (int)e.NewValue });
    }

    private void RestoreBrightnessPreview()
    {
        if (_previewDeviceId is not { } deviceId) return;
        _previewDeviceId = null;
        _previewDisplay?.Invoke(deviceId, _displaySettings.Get(deviceId));
    }

    protected override void OnClosed(EventArgs e)
    {
        RestoreBrightnessPreview();
        base.OnClosed(e);
    }

    private void DisplaySleepCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (DisplaySleepOptionsPanel is not null)
            DisplaySleepOptionsPanel.IsEnabled = DisplaySleepCheckBox.IsChecked == true;
    }

    private void SaveDisplaySettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DisplayDeviceComboBox.SelectedItem is not MonitoredDeckDevice device) return;
        try
        {
            var enabled = DisplaySleepCheckBox.IsChecked == true;
            var minutes = _displaySettings.Get(device.DeviceId).SleepMinutes;
            if (enabled && (!int.TryParse(SleepMinutesTextBox.Text, out minutes) || minutes is < 1 or > 1440))
                throw new ArgumentException("Enter a sleep timeout between 1 and 1440 minutes.");
            _displaySettings.Save(device.DeviceId, new DeckDisplaySettings((int)BrightnessSlider.Value,
                enabled, minutes, (DeckSleepBehaviour)SleepBehaviourComboBox.SelectedIndex));
            _previewDeviceId = null;
            MessageBox.Show(this, "Display settings saved.", "AJeff", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Could not save display settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StartWithWindowsCheckBox_OnChanged(object sender, RoutedEventArgs e) =>
        UpdateStartMinimizedAvailability();

    private void UpdateStartMinimizedAvailability()
    {
        if (StartMinimizedCheckBox is not null)
        {
            StartMinimizedCheckBox.IsEnabled = StartWithWindowsCheckBox?.IsChecked == true;
        }
    }

    private void SaveApplicationSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _applicationSettings.Save(
                StartWithWindowsCheckBox.IsChecked == true,
                StartMinimizedCheckBox.IsChecked == true);
            MessageBox.Show(this, "Application settings saved.", "AJeff", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidOperationException
                                          or System.Security.SecurityException)
        {
            MessageBox.Show(this, exception.Message, "Could not save application settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PluginList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedPlugin = PluginList.SelectedItem as LoadedPlugin;
        _settings = _selectedPlugin is not null
            ? _loader.Registry.Settings.GetValueOrDefault(_selectedPlugin.Id)
            : null;
        RenderSettings();
    }

    private void RenderSettings()
    {
        SettingsPanel.Children.Clear();
        _editors.Clear();
        PluginTitle.Text = _selectedPlugin?.DisplayName ?? "No plugin selected";
        PluginIcon.Source = _selectedPlugin?.Icon;
        PluginIcon.Visibility = _selectedPlugin?.Icon is null ? Visibility.Collapsed : Visibility.Visible;
        PluginVersion.Text = _selectedPlugin is null ? "" : $"Version {_selectedPlugin.Version}";
        SaveButton.IsEnabled = EditJsonButton.IsEnabled = _settings is not null;

        if (_settings is null)
        {
            SettingsPanel.Children.Add(new TextBlock { Text = "This plugin does not expose any settings.", Foreground = System.Windows.Media.Brushes.DimGray });
            return;
        }

        foreach (var definition in _settings.Definitions)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            container.Children.Add(new TextBlock { Text = definition.DisplayName, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrWhiteSpace(definition.Description))
            {
                container.Children.Add(new TextBlock { Text = definition.Description, Foreground = System.Windows.Media.Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 5) });
            }

            var editor = CreateEditor(definition, _settings.GetValue(definition.Key));
            _editors[definition.Key] = editor;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(editor);
            if (definition.Type == PluginSettingType.Color && editor is TextBox colorTextBox)
            {
                var chooseColor = new Button { Content = "Choose...", Margin = new Thickness(7, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
                chooseColor.Click += (_, _) =>
                {
                    using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
                    try { dialog.Color = System.Drawing.ColorTranslator.FromHtml(colorTextBox.Text); } catch { }
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        colorTextBox.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                    }
                };
                row.Children.Add(chooseColor);
            }
            if (!string.IsNullOrWhiteSpace(definition.Suffix))
            {
                row.Children.Add(new TextBlock { Text = definition.Suffix, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            container.Children.Add(row);
            SettingsPanel.Children.Add(container);
        }
    }

    private static Control CreateEditor(PluginSettingDefinition definition, string value)
    {
        if (definition.Type == PluginSettingType.Boolean)
        {
            return new CheckBox { IsChecked = bool.Parse(value), VerticalAlignment = VerticalAlignment.Center };
        }
        if (definition.Type == PluginSettingType.Choice)
        {
            var choices = definition.Choices ?? [];
            return new ComboBox
            {
                Width = 240,
                ItemsSource = choices,
                DisplayMemberPath = nameof(PluginSettingChoice.DisplayName),
                SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase)),
            };
        }
        if (definition.Type == PluginSettingType.Password)
        {
            return new PasswordBox { Password = value, Width = 240, Padding = new Thickness(4, 3, 4, 3) };
        }
        return new TextBox { Text = value, Width = 240, Padding = new Thickness(4, 3, 4, 3) };
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        try
        {
            foreach (var definition in _settings.Definitions)
            {
                _settings.SetValue(definition.Key, ReadEditorValue(definition, _editors[definition.Key]));
            }
            MessageBox.Show(this, "Plugin settings saved.", "JeffDock", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            MessageBox.Show(this, exception.Message, "Invalid setting", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string ReadEditorValue(PluginSettingDefinition definition, Control editor) => definition.Type switch
    {
        PluginSettingType.Boolean => ((CheckBox)editor).IsChecked == true ? "true" : "false",
        PluginSettingType.Choice => ((PluginSettingChoice?)((ComboBox)editor).SelectedItem)?.Value
            ?? throw new FormatException($"Select a value for {definition.DisplayName}."),
        PluginSettingType.Integer => long.Parse(((TextBox)editor).Text, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        PluginSettingType.Decimal => decimal.Parse(((TextBox)editor).Text, NumberStyles.Number, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        PluginSettingType.Password => ((PasswordBox)editor).Password,
        _ => ((TextBox)editor).Text,
    };

    private void EditJsonButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        var editor = new TextBox
        {
            Text = _settings.GetJson(), AcceptsReturn = true, AcceptsTab = true,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var save = new Button { Content = "Apply", IsDefault = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(save, Dock.Bottom);
        panel.Children.Add(save);
        panel.Children.Add(editor);
        var dialog = new Window { Owner = this, Title = $"Edit {_selectedPlugin!.DisplayName} JSON", Width = 620, Height = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        save.Click += (_, _) =>
        {
            try { _settings.ApplyJson(editor.Text); dialog.DialogResult = true; }
            catch (Exception exception) when (exception is JsonException or ArgumentException) { MessageBox.Show(dialog, exception.Message, "Invalid JSON", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        if (dialog.ShowDialog() == true) RenderSettings();
    }
}
