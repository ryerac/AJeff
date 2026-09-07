using System.Windows;
using System.Windows.Controls;

namespace JeffDock.App.Simulation;

internal sealed class SimulatedDeviceDialog : Window
{
    private readonly ComboBox _devicePicker;

    public SimulatedDeviceDialog(IEnumerable<SimulatedDeckDefinition> definitions)
    {
        Title = "Add simulated device";
        Width = 420;
        Height = 175;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _devicePicker = new ComboBox
        {
            ItemsSource = definitions.ToList(), DisplayMemberPath = nameof(SimulatedDeckDefinition.DisplayName),
            SelectedIndex = 0, Margin = new Thickness(0, 6, 0, 18),
        };
        var addButton = new Button { Content = "Add", Width = 82, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        addButton.Click += (_, _) => DialogResult = true;
        var cancelButton = new Button { Content = "Cancel", Width = 82, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(addButton);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Device model", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_devicePicker);
        panel.Children.Add(buttons);
        Content = panel;
    }

    public SimulatedDeckDefinition? SelectedDefinition => _devicePicker.SelectedItem as SimulatedDeckDefinition;
}
