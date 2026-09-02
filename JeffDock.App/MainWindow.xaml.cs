using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using JeffDock.App.Bindings;
using JeffDock.Core.Deck;

namespace JeffDock.App;

public partial class MainWindow : Window
{
    private readonly DeckMonitorService _monitor;
    private readonly DeckBindingStore _bindingStore;
    private readonly DeckIconStore _iconStore;
    private readonly DeckActionCatalog _actionCatalog;
    private readonly DeckActionExecutor _actionExecutor;
    private readonly Dictionary<(DeckControlType ControlType, int ControlIndex), Border> _controlVisuals = new();
    private readonly Dictionary<(DeckControlType ControlType, int ControlIndex), DeckControlLayout> _controlLayouts = new();
    private readonly Dictionary<Border, Brush> _idleBrushes = new();
    private readonly Dictionary<Border, int> _pulseVersions = new();
    private DeckLayoutDefinition? _renderedLayout;
    private (DeckControlType ControlType, int ControlIndex)? _selectedControl;
    private string? _selectedDeviceId;
    private bool _isUpdatingBindingEditor;
    private bool _isUpdatingSceneEditor;

    public MainWindow()
    {
        InitializeComponent();

        _bindingStore = new DeckBindingStore();
        _iconStore = new DeckIconStore();
        _bindingStore.ActiveSceneChanged += OnActiveSceneChanged;
        _actionCatalog = new DeckActionCatalog(_bindingStore);
        _actionExecutor = new DeckActionExecutor(_actionCatalog);
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
        _bindingStore.ActiveSceneChanged -= OnActiveSceneChanged;
        _monitor.Dispose();
        _actionCatalog.Dispose();
        base.OnClosed(e);
    }

    private void DevicesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var device = DevicesListBox.SelectedItem as MonitoredDeckDevice;
        _selectedDeviceId = device?.DeviceId;
        if (device is not null)
        {
            RenderFaceplate(device.Layout);
            EnsureSelectedControl(device.Layout);
        }

        RefreshSceneEditor();
        RefreshButtonIcons();
        RefreshBindingEditor();
        RefreshLogs();
    }

    private void TurnActionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindingEditor || TurnActionComboBox.SelectedItem is not IDeckAction action)
        {
            return;
        }

        SaveBinding(DeckInputEventType.EncoderTurn, action.Id);
    }

    private void PressActionComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindingEditor || PressActionComboBox.SelectedItem is not IDeckAction action)
        {
            return;
        }

        var triggerEventType = _selectedControl?.ControlType == DeckControlType.Button
            ? DeckInputEventType.ButtonPress
            : DeckInputEventType.EncoderPress;

        SaveBinding(triggerEventType, action.Id);
    }

    private void SceneComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSceneEditor || SceneComboBox.SelectedItem is not DeckScene scene)
        {
            return;
        }

        var device = GetSelectedDevice();
        if (device is null)
        {
            return;
        }

        _bindingStore.SetActiveScene(device, scene.Id);
    }

    private void NewSceneButton_OnClick(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null)
        {
            return;
        }

        var dialog = new SceneNameDialog { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _bindingStore.CreateScene(device, dialog.SceneName);
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, exception.Message, "New Scene", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DeleteSceneButton_OnClick(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null || SceneComboBox.SelectedItem is not DeckScene scene || scene.IsDefault)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Delete the '{scene.Name}' scene and all of its bindings?",
            "Delete Scene",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _bindingStore.DeleteScene(device, scene.Id);
        _iconStore.DeleteSceneIcons(device.DeviceId, scene.Id);
    }

    private void UploadIconButton_OnClick(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null
            || _selectedControl is not { ControlType: DeckControlType.Button } selectedControl
            || !_controlLayouts.TryGetValue(selectedControl, out var layout)
            || !layout.CanHaveIcon)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"Choose an icon for Button {selectedControl.ControlIndex}",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var scene = _bindingStore.GetActiveScene(device);
            _iconStore.SaveIcon(device.DeviceId, scene.Id, selectedControl.ControlIndex, dialog.FileName);
            RefreshButtonIcons();
            RefreshBindingEditor();
            QueueIconSync(device);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"The icon could not be saved.\n\n{exception.Message}", "Upload Icon", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveIconButton_OnClick(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null
            || _selectedControl is not { ControlType: DeckControlType.Button } selectedControl
            || !_controlLayouts.TryGetValue(selectedControl, out var layout)
            || !layout.CanHaveIcon)
        {
            return;
        }

        var scene = _bindingStore.GetActiveScene(device);
        if (_iconStore.DeleteIcon(device.DeviceId, scene.Id, selectedControl.ControlIndex))
        {
            RefreshButtonIcons();
            RefreshBindingEditor();
            QueueIconSync(device);
        }
    }

    private void RenameSceneButton_OnClick(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null || SceneComboBox.SelectedItem is not DeckScene scene || scene.IsDefault)
        {
            return;
        }

        var dialog = new SceneNameDialog("Rename Scene", "Rename", scene.Name) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _bindingStore.RenameScene(device, scene.Id, dialog.SceneName);
            RefreshSceneEditor();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, exception.Message, "Rename Scene", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnActiveSceneChanged(MonitoredDeckDevice device)
    {
        if (_selectedDeviceId is null || !string.Equals(_selectedDeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            RefreshSceneEditor();
            RefreshButtonIcons();
            RefreshBindingEditor();
            QueueIconSync(device);
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            RefreshSceneEditor();
            RefreshButtonIcons();
            RefreshBindingEditor();
            QueueIconSync(device);
        }));
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
            RefreshSceneEditor();
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
        RefreshSceneEditor();
        RefreshButtonIcons();
        RefreshBindingEditor();
        RefreshLogs();

        foreach (var device in connected.DistinctBy(device => device.DeviceId))
        {
            QueueIconSync(device);
        }
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
                IconBindingRow.Visibility = Visibility.Collapsed;
                RemoveIconButton.IsEnabled = false;
                TurnBindingRow.Visibility = Visibility.Collapsed;
                PressBindingRow.Visibility = Visibility.Collapsed;
                TurnActionComboBox.ItemsSource = null;
                PressActionComboBox.ItemsSource = null;
                return;
            }

            SelectedControlText.Text = $"Selected Control: {DescribeControl(layout)}";
            IconBindingRow.Visibility = layout.CanHaveIcon ? Visibility.Visible : Visibility.Collapsed;
            RemoveIconButton.IsEnabled = layout.CanHaveIcon
                                         && _iconStore.FindIconPath(
                                             device.DeviceId,
                                             _bindingStore.GetActiveScene(device).Id,
                                             selectedControl.ControlIndex) is not null;

            if (selectedControl.ControlType == DeckControlType.Encoder)
            {
                TurnBindingRow.Visibility = Visibility.Visible;
                TurnActionComboBox.ItemsSource = _actionCatalog.GetActionsFor(DeckInputEventType.EncoderTurn);
                TurnActionComboBox.SelectedItem = _actionCatalog.GetAction(_bindingStore.GetActionId(device, DeckControlType.Encoder, selectedControl.ControlIndex, DeckInputEventType.EncoderTurn));

                PressBindingRow.Visibility = Visibility.Visible;
                PressBindingLabel.Text = "Press Action";
                PressActionComboBox.ItemsSource = _actionCatalog.GetActionsFor(DeckInputEventType.EncoderPress);
                PressActionComboBox.SelectedItem = _actionCatalog.GetAction(_bindingStore.GetActionId(device, DeckControlType.Encoder, selectedControl.ControlIndex, DeckInputEventType.EncoderPress));
            }
            else
            {
                TurnBindingRow.Visibility = Visibility.Collapsed;
                PressBindingRow.Visibility = Visibility.Visible;
                PressBindingLabel.Text = "Button Action";
                PressActionComboBox.ItemsSource = _actionCatalog.GetActionsFor(DeckInputEventType.ButtonPress);
                PressActionComboBox.SelectedItem = _actionCatalog.GetAction(_bindingStore.GetActionId(device, DeckControlType.Button, selectedControl.ControlIndex, DeckInputEventType.ButtonPress));
            }
        }
        finally
        {
            _isUpdatingBindingEditor = false;
        }
    }

    private void RefreshSceneEditor()
    {
        _isUpdatingSceneEditor = true;

        try
        {
            var device = GetSelectedDevice();
            if (device is null)
            {
                SceneComboBox.ItemsSource = null;
                RenameSceneButton.IsEnabled = false;
                DeleteSceneButton.IsEnabled = false;
                return;
            }

            var scenes = _bindingStore.GetScenes(device);
            var activeScene = _bindingStore.GetActiveScene(device);
            SceneComboBox.ItemsSource = scenes;
            SceneComboBox.SelectedItem = scenes.First(scene => string.Equals(scene.Id, activeScene.Id, StringComparison.OrdinalIgnoreCase));
            RenameSceneButton.IsEnabled = !activeScene.IsDefault;
            DeleteSceneButton.IsEnabled = !activeScene.IsDefault;
        }
        finally
        {
            _isUpdatingSceneEditor = false;
        }
    }

    private void SaveBinding(DeckInputEventType triggerEventType, string actionId)
    {
        var device = GetSelectedDevice();
        if (device is null || _selectedControl is not { } selectedControl)
        {
            return;
        }

        _bindingStore.SetAction(device, selectedControl.ControlType, selectedControl.ControlIndex, triggerEventType, actionId);
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
            Child = BuildControlLabel(control, foreground),
        };
    }

    private void RefreshButtonIcons()
    {
        var device = GetSelectedDevice();
        if (device is null)
        {
            return;
        }

        var scene = _bindingStore.GetActiveScene(device);
        foreach (var (key, layout) in _controlLayouts.Where(entry => entry.Value.CanHaveIcon))
        {
            if (!_controlVisuals.TryGetValue(key, out var border))
            {
                continue;
            }

            var iconPath = _iconStore.FindIconPath(device.DeviceId, scene.Id, layout.ControlIndex);
            border.Child = iconPath is null
                ? BuildControlLabel(layout, Brushes.Gainsboro)
                : BuildIconImage(iconPath, layout);
        }
    }

    private void QueueIconSync(MonitoredDeckDevice device)
    {
        var buttonIndexes = device.Layout.Controls
            .Where(control => control.ControlType == DeckControlType.Button && control.CanHaveIcon)
            .Select(control => control.ControlIndex)
            .ToList();

        if (buttonIndexes.Count == 0)
        {
            return;
        }

        var scene = _bindingStore.GetActiveScene(device);
        var images = _iconStore.LoadButtonImages(
            device.DeviceId,
            scene.Id,
            buttonIndexes,
            device.Layout.ButtonImageOutputWidth,
            device.Layout.ButtonImageOutputHeight,
            device.Layout.ButtonImageRotationDegreesClockwise);
        _ = Task.Run(() => _monitor.TrySetButtonImages(device.DeviceId, images));
    }

    private static FrameworkElement BuildIconImage(string iconPath, DeckControlLayout control)
    {
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            return new Image
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill,
                ToolTip = control.Label,
            };
        }
        catch
        {
            return BuildControlLabel(control, Brushes.Gainsboro);
        }
    }

    private static TextBlock BuildControlLabel(DeckControlLayout control, Brush foreground)
    {
        return new TextBlock
        {
            Text = control.Label,
            Foreground = foreground,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
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
