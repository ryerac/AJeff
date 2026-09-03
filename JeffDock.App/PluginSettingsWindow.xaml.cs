using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using JeffDock.App.Plugins;
using JeffDock.PluginContracts;

namespace JeffDock.App;

public partial class PluginSettingsWindow : Window
{
    private readonly JeffDockPluginLoader _loader;
    private readonly Dictionary<string, Control> _editors = new(StringComparer.OrdinalIgnoreCase);
    private LoadedPlugin? _selectedPlugin;
    private PluginSettingsStore? _settings;

    internal PluginSettingsWindow(JeffDockPluginLoader loader)
    {
        InitializeComponent();
        _loader = loader;
        PluginList.ItemsSource = loader.Plugins;
        DiagnosticsText.Text = string.Join(Environment.NewLine, loader.Diagnostics);
        PluginList.SelectedIndex = loader.Plugins.Count > 0 ? 0 : -1;
        if (loader.Plugins.Count == 0)
        {
            PluginTitle.Text = "No plugins loaded";
            SaveButton.IsEnabled = EditJsonButton.IsEnabled = false;
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
