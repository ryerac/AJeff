using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JeffDock.App.Bindings;
using JeffDock.Core.Deck;

namespace JeffDock.App;

public partial class MainWindow : Window
{
    private readonly DeckMonitorService _monitor;
    private readonly DeckBindingStore _bindingStore;
    private readonly DeckActionExecutor _actionExecutor;
    private readonly Dictionary<(DeckControlType ControlType, int ControlIndex), Border> _controlVisuals = new();
    private readonly Dictionary<(DeckControlType ControlType, int ControlIndex), DeckControlLayout> _controlLayouts = new();
    private readonly Dictionary<Border, Brush> _idleBrushes = new();
    private readonly Dictionary<Border, int> _pulseVersions = new();
    private DeckLayoutDefinition? _renderedLayout;
    private (DeckControlType ControlType, int ControlIndex)? _selectedControl;
    private string? _selectedDeviceId;
    private bool _isUpdatingBindingEditor;

    private static readonly DeckBindingActionKind[] TurnActions =
    [
        DeckBindingActionKind.None,
        DeckBindingActionKind.VolumeAdjust,
    ];

    private static readonly DeckBindingActionKind[] PressActions =
    [
        DeckBindingActionKind.None,
        DeckBindingActionKind.ToggleMute,
    ];

    public MainWindow()
    {
        InitializeComponent();

        _bindingStore = new DeckBindingStore();
        _actionExecutor = new DeckActionExecutor();
        _monitor = new DeckMonitorService(DeckProfileCatalog.SupportedProfiles, maxLinesPerDevice: 100);
        _monitor.DevicesChanged += OnDevicesChanged;
        _monitor.DeviceLogChanged += OnDeviceLogChanged;
        _monitor.InputEventReceived += OnInputEventReceived;
        _monitor.Start();

        RefreshUi();
    }

    protected override void OnClosed(EventArgs e)
    {
        _monitor.DevicesChanged -= OnDevicesChanged;
        _monitor.DeviceLogChanged -= OnDeviceLogChanged;
        _monitor.InputEventReceived -= OnInputEventReceived;
        _monitor.Dispose();
        _actionExecutor.Dispose();
        base.OnClosed(e);
    }

    private void DevicesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedDeviceId = (DevicesListBox.SelectedItem as MonitoredDeckDevice)?.DeviceId;
        RefreshBindingEditor();
        RefreshLogs();
    }

    private void TurnActionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindingEditor || TurnActionComboBox.SelectedItem is not DeckBindingActionKind action)
        {
            return;
        }

        SaveBinding(DeckInputEventType.EncoderTurn, action);
    }

    private void PressActionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindingEditor || PressActionComboBox.SelectedItem is not DeckBindingActionKind action)
        {
            return;
        }

        var triggerEventType = _selectedControl?.ControlType == DeckControlType.Button
            ? DeckInputEventType.ButtonPress
            : DeckInputEventType.EncoderPress;

        SaveBinding(triggerEventType, action);
    }

    private void OnDevicesChanged()
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshUi));
    }

    private void OnDeviceLogChanged(MonitoredDeckDevice device)
    {
        if (_selectedDeviceId is null || !string.Equals(_selectedDeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshLogs));
    }

    private void RefreshUi()
    {
        var connected = _monitor.GetConnectedDevices();
        var selectedId = _selectedDeviceId ?? (DevicesListBox.SelectedItem as MonitoredDeckDevice)?.DeviceId;

        DevicesListBox.ItemsSource = connected;

        if (connected.Count == 0)
        {
            _selectedDeviceId = null;
            _selectedControl = null;
            SelectedDeviceTitle.Text = "Raw Input Log";
            LogListBox.ItemsSource = new[] { "No supported device connected." };
            RenderFaceplate(null);
            RefreshBindingEditor();
            return;
        }

        var selected = connected.FirstOrDefault(d => d.DeviceId == selectedId) ?? connected[0];
        _selectedDeviceId = selected.DeviceId;
        if (!Equals(DevicesListBox.SelectedItem, selected))
        {
            DevicesListBox.SelectedItem = selected;
        }

        RenderFaceplate(selected.Layout);
        EnsureSelectedControl(selected.Layout);
        RefreshBindingEditor();
        RefreshLogs();
    }

    private void RefreshLogs()
    {
        var selected = DevicesListBox.SelectedItem as MonitoredDeckDevice;
        if (selected is null)
        {
            SelectedDeviceTitle.Text = "Raw Input Log";
            LogListBox.ItemsSource = new[] { "Select a device." };
            return;
        }

        SelectedDeviceTitle.Text = $"Raw Input Log - {selected.ProfileName}";

        var lines = _monitor.GetLogLines(selected.DeviceId);
        if (lines.Count == 0)
        {
            LogListBox.ItemsSource = new[] { "Waiting for input..." };
            return;
        }

        LogListBox.ItemsSource = lines;
        if (lines.Count > 0)
        {
            LogListBox.ScrollIntoView(lines[^1]);
        }
    }

    private void RenderFaceplate(DeckLayoutDefinition? layout)
    {
        if (layout is null)
        {
            FaceplateCanvas.Children.Clear();
            FaceplateCanvas.Width = 0;
            FaceplateCanvas.Height = 0;
            _controlVisuals.Clear();
            _controlLayouts.Clear();
            _idleBrushes.Clear();
            _pulseVersions.Clear();
            _renderedLayout = null;
            return;
        }

        if (ReferenceEquals(_renderedLayout, layout))
        {
            return;
        }

        FaceplateCanvas.Children.Clear();
        FaceplateCanvas.Width = layout.Width;
        FaceplateCanvas.Height = layout.Height;
        _controlVisuals.Clear();
        _controlLayouts.Clear();
        _idleBrushes.Clear();
        _pulseVersions.Clear();

        foreach (var control in layout.Controls)
        {
            var border = BuildControlVisual(control);
            border.Tag = control;
            border.MouseLeftButtonUp += ControlBorder_OnMouseLeftButtonUp;
            Canvas.SetLeft(border, control.X);
            Canvas.SetTop(border, control.Y);
            FaceplateCanvas.Children.Add(border);

            _controlVisuals[(control.ControlType, control.ControlIndex)] = border;
            _controlLayouts[(control.ControlType, control.ControlIndex)] = control;
            _idleBrushes[border] = border.Background;
            _pulseVersions[border] = 0;
        }

        _renderedLayout = layout;
        RefreshSelectionVisuals();
    }

    private void OnInputEventReceived(MonitoredDeckDevice device, DeckInputEvent evt)
    {
        _actionExecutor.Execute(device, evt, _bindingStore);

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_selectedDeviceId is null || !string.Equals(_selectedDeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (evt.Type == DeckInputEventType.ButtonPress &&
                _controlVisuals.TryGetValue((DeckControlType.Button, evt.ControlIndex), out var buttonBorder))
            {
                PulseBorder(buttonBorder, Colors.Gold, 180);
                return;
            }

            if ((evt.Type == DeckInputEventType.EncoderPress || evt.Type == DeckInputEventType.EncoderTurn) &&
                _controlVisuals.TryGetValue((DeckControlType.Encoder, evt.ControlIndex), out var knobBorder))
            {
                PulseBorder(knobBorder, evt.Type == DeckInputEventType.EncoderPress ? Colors.DeepSkyBlue : Colors.Orange, 140);
            }
        }));
    }

    private void ControlBorder_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: DeckControlLayout control })
        {
            return;
        }

        _selectedControl = (control.ControlType, control.ControlIndex);
        RefreshSelectionVisuals();
        RefreshBindingEditor();
    }

    private void EnsureSelectedControl(DeckLayoutDefinition layout)
    {
        if (_selectedControl is { } selectedControl && layout.Controls.Any(control => control.ControlType == selectedControl.ControlType && control.ControlIndex == selectedControl.ControlIndex))
        {
            return;
        }

        var first = layout.Controls.FirstOrDefault();
        _selectedControl = first is null ? null : (first.ControlType, first.ControlIndex);
    }

    private void RefreshSelectionVisuals()
    {
        foreach (var (key, border) in _controlVisuals)
        {
            var isSelected = _selectedControl is { } selectedControl && selectedControl == key;
            border.BorderBrush = isSelected ? CreateBrush("#C96F1A") : CreateBrush("#777777");
            border.BorderThickness = new Thickness(key.ControlType == DeckControlType.Encoder ? (isSelected ? 4 : 2) : (isSelected ? 3 : 1));
        }
    }

    private void RefreshBindingEditor()
    {
        _isUpdatingBindingEditor = true;

        try
        {
            var device = GetSelectedDevice();
            if (device is null || _selectedControl is not { } selectedControl || !_controlLayouts.TryGetValue(selectedControl, out var layout))
            {
                SelectedControlText.Text = "Selected Control: none";
                TurnBindingRow.Visibility = Visibility.Collapsed;
                PressBindingRow.Visibility = Visibility.Collapsed;
                TurnActionComboBox.ItemsSource = null;
                PressActionComboBox.ItemsSource = null;
                return;
            }

            SelectedControlText.Text = $"Selected Control: {DescribeControl(layout)}";

            if (selectedControl.ControlType == DeckControlType.Encoder)
            {
                TurnBindingRow.Visibility = Visibility.Visible;
                TurnActionComboBox.ItemsSource = TurnActions;
                TurnActionComboBox.SelectedItem = _bindingStore.GetAction(device, DeckControlType.Encoder, selectedControl.ControlIndex, DeckInputEventType.EncoderTurn);

                PressBindingRow.Visibility = Visibility.Visible;
                PressBindingLabel.Text = "Press Action";
                PressActionComboBox.ItemsSource = PressActions;
                PressActionComboBox.SelectedItem = _bindingStore.GetAction(device, DeckControlType.Encoder, selectedControl.ControlIndex, DeckInputEventType.EncoderPress);
            }
            else
            {
                TurnBindingRow.Visibility = Visibility.Collapsed;
                PressBindingRow.Visibility = Visibility.Visible;
                PressBindingLabel.Text = "Button Action";
                PressActionComboBox.ItemsSource = PressActions;
                PressActionComboBox.SelectedItem = _bindingStore.GetAction(device, DeckControlType.Button, selectedControl.ControlIndex, DeckInputEventType.ButtonPress);
            }
        }
        finally
        {
            _isUpdatingBindingEditor = false;
        }
    }

    private void SaveBinding(DeckInputEventType triggerEventType, DeckBindingActionKind action)
    {
        var device = GetSelectedDevice();
        if (device is null || _selectedControl is not { } selectedControl)
        {
            return;
        }

        _bindingStore.SetAction(device, selectedControl.ControlType, selectedControl.ControlIndex, triggerEventType, action);
    }

    private MonitoredDeckDevice? GetSelectedDevice()
    {
        return DevicesListBox.SelectedItem as MonitoredDeckDevice;
    }

    private static string DescribeControl(DeckControlLayout control)
    {
        return control.ControlType switch
        {
            DeckControlType.Button => $"Button {control.ControlIndex}",
            DeckControlType.Encoder => $"Knob {control.ControlIndex}",
            _ => control.Label,
        };
    }

    private static Border BuildControlVisual(DeckControlLayout control)
    {
        var background = control.VisualKind switch
        {
            DeckControlVisualKind.SquareButton => CreateBrush("#2A2A2A"),
            DeckControlVisualKind.RoundButton => CreateBrush("#3A3A3A"),
            DeckControlVisualKind.Knob => CreateBrush("#555555"),
            _ => Brushes.Gray,
        };

        var foreground = control.VisualKind == DeckControlVisualKind.Knob
            ? Brushes.WhiteSmoke
            : Brushes.Gainsboro;

        var cornerRadius = control.VisualKind switch
        {
            DeckControlVisualKind.SquareButton => 8.0,
            DeckControlVisualKind.RoundButton => control.Height / 2,
            DeckControlVisualKind.Knob => control.Width / 2,
            _ => 4.0,
        };

        var borderThickness = control.VisualKind == DeckControlVisualKind.Knob ? 2.0 : 1.0;

        return new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = background,
            BorderBrush = CreateBrush("#777777"),
            BorderThickness = new Thickness(borderThickness),
            CornerRadius = new CornerRadius(cornerRadius),
            Child = new TextBlock
            {
                Text = control.Label,
                Foreground = foreground,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private async void PulseBorder(Border border, Color color, int milliseconds)
    {
        var version = _pulseVersions.TryGetValue(border, out var currentVersion) ? currentVersion + 1 : 1;
        _pulseVersions[border] = version;
        border.Background = new SolidColorBrush(color);

        await Task.Delay(milliseconds);

        if (_pulseVersions.TryGetValue(border, out var latestVersion) &&
            latestVersion == version &&
            _idleBrushes.TryGetValue(border, out var idleBrush))
        {
            border.Background = idleBrush;
        }
    }

    private static SolidColorBrush CreateBrush(string colorValue)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorValue));
        brush.Freeze();
        return brush;
    }
}