using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using JeffDock.App.Bindings;
using JeffDock.App.Bindings.Core;
using JeffDock.App.Icons;
using JeffDock.Core.Deck;

namespace JeffDock.App;

public partial class MainWindow : Window
{
    private readonly DeckMonitorService _monitor;
    private readonly DeckBindingStore _bindingStore;
    private readonly DeckIconStore _iconStore;
    private readonly IconLibraryCatalog _iconLibraryCatalog;
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
    private int _decksSleeping;

    public MainWindow()
    {
        InitializeComponent();

        _bindingStore = new DeckBindingStore();
        _iconStore = new DeckIconStore();
        _iconLibraryCatalog = new IconLibraryCatalog();
        _bindingStore.ActiveSceneChanged += OnActiveSceneChanged;
        _actionCatalog = new DeckActionCatalog(_bindingStore);
        _actionCatalog.StateCatalog.StateChanged += OnDeckStateChanged;
        _actionExecutor = new DeckActionExecutor(_actionCatalog);
        _monitor = new DeckMonitorService(DeckProfileCatalog.SupportedProfiles, maxLinesPerDevice: 100);
        _monitor.DevicesChanged += OnDevicesChanged;
        _monitor.DeviceLogChanged += OnDeviceLogChanged;
        _monitor.InputEventReceived += OnInputEventReceived;
        _monitor.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Application.Current.SessionEnding += OnSessionEnding;

        RefreshUi();
    }

    protected override void OnClosed(EventArgs e)
    {
        SleepDecks();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Application.Current.SessionEnding -= OnSessionEnding;
        _monitor.DevicesChanged -= OnDevicesChanged;
        _monitor.DeviceLogChanged -= OnDeviceLogChanged;
        _monitor.InputEventReceived -= OnInputEventReceived;
        _bindingStore.ActiveSceneChanged -= OnActiveSceneChanged;
        _actionCatalog.StateCatalog.StateChanged -= OnDeckStateChanged;
        _monitor.Dispose();
        _actionCatalog.Dispose();
        base.OnClosed(e);
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        SleepDecks();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            SleepDecks();
            return;
        }

        if (e.Mode != PowerModes.Resume || Interlocked.Exchange(ref _decksSleeping, 0) == 0)
        {
            return;
        }

        _monitor.WakeAllDevices();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            foreach (var device in _monitor.GetConnectedDevices().DistinctBy(device => device.DeviceId))
            {
                QueueIconSync(device);
            }
        }));
    }

    private void SleepDecks()
    {
        if (Interlocked.Exchange(ref _decksSleeping, 1) == 0)
        {
            _monitor.SleepAllDevices();
        }
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

    private void TurnActionGroupComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindingEditor || TurnActionGroupComboBox.SelectedItem is not DeckActionGroup group)
        {
            return;
        }

        ChangeActionGroup(DeckInputEventType.EncoderTurn, group, TurnActionComboBox);
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

    private void PressActionGroupComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindingEditor || PressActionGroupComboBox.SelectedItem is not DeckActionGroup group)
        {
            return;
        }

        var triggerEventType = _selectedControl?.ControlType == DeckControlType.Button
            ? DeckInputEventType.ButtonPress
            : DeckInputEventType.EncoderPress;

        ChangeActionGroup(triggerEventType, group, PressActionComboBox);
    }

    private void RecordKeyboardShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPressBinding(out var device, out var controlType, out var controlIndex, out var eventType))
        {
            return;
        }

        var parameters = _bindingStore.GetActionParameters(device, controlType, controlIndex, eventType);
        KeyboardShortcut.TryParse(parameters.GetValueOrDefault(KeyboardShortcutAction.ShortcutParameter), out var currentShortcut);
        var dialog = new KeyboardShortcutDialog(currentShortcut) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Shortcut is null)
        {
            return;
        }

        _bindingStore.SetActionParameter(
            device,
            controlType,
            controlIndex,
            eventType,
            KeyboardShortcutAction.ShortcutParameter,
            dialog.Shortcut.Serialize());
        RefreshBindingEditor();
    }

    private void ClearKeyboardShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPressBinding(out var device, out var controlType, out var controlIndex, out var eventType))
        {
            return;
        }

        _bindingStore.SetActionParameter(
            device,
            controlType,
            controlIndex,
            eventType,
            KeyboardShortcutAction.ShortcutParameter,
            null);
        RefreshBindingEditor();
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

    private void IconLibraryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var device = GetSelectedDevice();
        if (device is null
            || _selectedControl is not { ControlType: DeckControlType.Button } selectedControl
            || !_controlLayouts.TryGetValue(selectedControl, out var layout)
            || !layout.CanHaveIcon)
        {
            return;
        }

        if (_iconLibraryCatalog.Icons.Count == 0)
        {
            MessageBox.Show(this, "No bundled icons were found.", "Icon Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new IconLibraryDialog(_iconLibraryCatalog.Icons) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedImageBytes is not { } imageBytes)
        {
            return;
        }

        try
        {
            var scene = _bindingStore.GetActiveScene(device);
            _iconStore.SaveIcon(device.DeviceId, scene.Id, selectedControl.ControlIndex, imageBytes);
            RefreshButtonIcons();
            RefreshBindingEditor();
            QueueIconSync(device);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"The icon could not be saved.\n\n{exception.Message}", "Icon Library", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void IconModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingBindingEditor
            || IconModeComboBox.SelectedItem is not DeckIconMode mode
            || GetSelectedDevice() is not { } device
            || _selectedControl is not { ControlType: DeckControlType.Button } selectedControl)
        {
            return;
        }

        _bindingStore.SetIconMode(device, selectedControl.ControlIndex, mode);
        RefreshButtonIcons();
        RefreshBindingEditor();
        QueueIconSync(device);
    }

    private void DynamicStateLibraryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DeckActionVisualState state })
        {
            ChooseDynamicStateIcon(state, useLibrary: true);
        }
    }

    private void DynamicStateUploadButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DeckActionVisualState state })
        {
            ChooseDynamicStateIcon(state, useLibrary: false);
        }
    }

    private void DynamicStateRemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeckActionVisualState state }
            || GetSelectedDevice() is not { } device
            || _selectedControl is not { ControlType: DeckControlType.Button } selectedControl)
        {
            return;
        }

        var scene = _bindingStore.GetActiveScene(device);
        if (_iconStore.DeleteStateIcon(device.DeviceId, scene.Id, selectedControl.ControlIndex, state.Id))
        {
            RefreshButtonIcons();
            RefreshBindingEditor();
            QueueIconSync(device);
        }
    }

    private void ChooseDynamicStateIcon(DeckActionVisualState state, bool useLibrary)
    {
        var device = GetSelectedDevice();
        if (device is null || _selectedControl is not { ControlType: DeckControlType.Button } selectedControl)
        {
            return;
        }

        byte[]? imageBytes = null;
        string? imagePath = null;
        if (useLibrary)
        {
            var picker = new IconLibraryDialog(_iconLibraryCatalog.Icons) { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedImageBytes is not { } selectedBytes)
            {
                return;
            }

            imageBytes = selectedBytes;
        }
        else
        {
            var picker = new OpenFileDialog
            {
                Title = $"Choose the {state.DisplayName} icon",
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };
            if (picker.ShowDialog(this) != true)
            {
                return;
            }

            imagePath = picker.FileName;
        }

        try
        {
            var scene = _bindingStore.GetActiveScene(device);
            if (imageBytes is not null)
            {
                _iconStore.SaveStateIcon(device.DeviceId, scene.Id, selectedControl.ControlIndex, state.Id, imageBytes);
            }
            else
            {
                _iconStore.SaveStateIcon(device.DeviceId, scene.Id, selectedControl.ControlIndex, state.Id, imagePath!);
            }

            RefreshButtonIcons();
            RefreshBindingEditor();
            QueueIconSync(device);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"The icon could not be saved.\n\n{exception.Message}", "Dynamic Icon", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void OnDeckStateChanged(string sourceId, string state)
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var device = GetSelectedDevice();
            if (device is null || !DeviceUsesStateSource(device, sourceId))
            {
                return;
            }

            RefreshButtonIcons();
            RefreshBindingEditor();
            QueueIconSync(device);
        }));
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
                IconModeComboBox.ItemsSource = null;
                DynamicIconStatesPanel.Children.Clear();
                TurnBindingRow.Visibility = Visibility.Collapsed;
                PressBindingRow.Visibility = Visibility.Collapsed;
                TurnActionComboBox.ItemsSource = null;
                PressActionComboBox.ItemsSource = null;
                TurnActionGroupComboBox.ItemsSource = null;
                PressActionGroupComboBox.ItemsSource = null;
                KeyboardShortcutRow.Visibility = Visibility.Collapsed;
                return;
            }

            SelectedControlText.Text = $"Selected Control: {DescribeControl(layout)}";
            IconBindingRow.Visibility = layout.CanHaveIcon ? Visibility.Visible : Visibility.Collapsed;
            if (layout.CanHaveIcon)
            {
                ConfigureIconEditor(device, selectedControl.ControlIndex);
            }

            if (selectedControl.ControlType == DeckControlType.Encoder)
            {
                TurnBindingRow.Visibility = Visibility.Visible;
                ConfigureActionEditor(
                    DeckInputEventType.EncoderTurn,
                    _bindingStore.GetActionId(device, DeckControlType.Encoder, selectedControl.ControlIndex, DeckInputEventType.EncoderTurn),
                    TurnActionGroupComboBox,
                    TurnActionComboBox);

                PressBindingRow.Visibility = Visibility.Visible;
                PressBindingLabel.Text = "Press Action";
                ConfigureActionEditor(
                    DeckInputEventType.EncoderPress,
                    _bindingStore.GetActionId(device, DeckControlType.Encoder, selectedControl.ControlIndex, DeckInputEventType.EncoderPress),
                    PressActionGroupComboBox,
                    PressActionComboBox);
            }
            else
            {
                TurnBindingRow.Visibility = Visibility.Collapsed;
                PressBindingRow.Visibility = Visibility.Visible;
                PressBindingLabel.Text = "Button Action";
                ConfigureActionEditor(
                    DeckInputEventType.ButtonPress,
                    _bindingStore.GetActionId(device, DeckControlType.Button, selectedControl.ControlIndex, DeckInputEventType.ButtonPress),
                    PressActionGroupComboBox,
                    PressActionComboBox);
            }

            ConfigureKeyboardShortcutEditor(device, selectedControl.ControlType, selectedControl.ControlIndex);
        }
        finally
        {
            _isUpdatingBindingEditor = false;
        }
    }

    private void ConfigureKeyboardShortcutEditor(MonitoredDeckDevice device, DeckControlType controlType, int controlIndex)
    {
        var eventType = controlType == DeckControlType.Button
            ? DeckInputEventType.ButtonPress
            : DeckInputEventType.EncoderPress;
        var actionId = _bindingStore.GetActionId(device, controlType, controlIndex, eventType);
        var isKeyboardShortcut = string.Equals(actionId, KeyboardShortcutAction.ActionId, StringComparison.OrdinalIgnoreCase);
        KeyboardShortcutRow.Visibility = isKeyboardShortcut ? Visibility.Visible : Visibility.Collapsed;
        if (!isKeyboardShortcut)
        {
            KeyboardShortcutTextBox.Text = string.Empty;
            ClearKeyboardShortcutButton.IsEnabled = false;
            return;
        }

        var parameters = _bindingStore.GetActionParameters(device, controlType, controlIndex, eventType);
        var value = parameters.GetValueOrDefault(KeyboardShortcutAction.ShortcutParameter);
        var hasShortcut = KeyboardShortcut.TryParse(value, out var shortcut) && shortcut is not null;
        KeyboardShortcutTextBox.Text = hasShortcut ? shortcut!.ToString() : "Not configured";
        ClearKeyboardShortcutButton.IsEnabled = hasShortcut;
    }

    private bool TryGetSelectedPressBinding(
        out MonitoredDeckDevice device,
        out DeckControlType controlType,
        out int controlIndex,
        out DeckInputEventType eventType)
    {
        device = null!;
        controlType = default;
        controlIndex = default;
        eventType = default;

        if (GetSelectedDevice() is not { } selectedDevice || _selectedControl is not { } selectedControl)
        {
            return false;
        }

        var selectedEventType = selectedControl.ControlType == DeckControlType.Button
            ? DeckInputEventType.ButtonPress
            : DeckInputEventType.EncoderPress;
        var actionId = _bindingStore.GetActionId(
            selectedDevice,
            selectedControl.ControlType,
            selectedControl.ControlIndex,
            selectedEventType);
        if (!string.Equals(actionId, KeyboardShortcutAction.ActionId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        device = selectedDevice;
        controlType = selectedControl.ControlType;
        controlIndex = selectedControl.ControlIndex;
        eventType = selectedEventType;
        return true;
    }

    private void ConfigureIconEditor(MonitoredDeckDevice device, int controlIndex)
    {
        var scene = _bindingStore.GetActiveScene(device);
        var action = _actionCatalog.GetAction(_bindingStore.GetActionId(
            device,
            DeckControlType.Button,
            controlIndex,
            DeckInputEventType.ButtonPress));
        var visual = action.Visual;
        var mode = _bindingStore.GetIconMode(device, controlIndex);
        if (visual is null)
        {
            mode = DeckIconMode.Static;
        }

        IconModeComboBox.ItemsSource = visual is null
            ? new[] { DeckIconMode.Static }
            : Enum.GetValues<DeckIconMode>();
        IconModeComboBox.SelectedItem = mode;
        StaticIconRow.Visibility = mode == DeckIconMode.Static ? Visibility.Visible : Visibility.Collapsed;
        DynamicIconPanel.Visibility = mode == DeckIconMode.Dynamic ? Visibility.Visible : Visibility.Collapsed;
        RemoveIconButton.IsEnabled = mode == DeckIconMode.Static
                                     && _iconStore.FindIconPath(device.DeviceId, scene.Id, controlIndex) is not null;

        DynamicIconStatesPanel.Children.Clear();
        if (mode != DeckIconMode.Dynamic || visual is null)
        {
            DynamicIconStateText.Text = string.Empty;
            return;
        }

        var currentState = _actionCatalog.StateCatalog.GetCurrentState(visual.StateSourceId);
        DynamicIconStateText.Text = $"Current state: {currentState}";
        foreach (var state in visual.States)
        {
            var customPath = _iconStore.FindStateIconPath(device.DeviceId, scene.Id, controlIndex, state.Id);
            var previewBytes = ReadIconBytes(customPath)
                               ?? (state.DefaultIconId is null
                                   ? null
                                   : _iconLibraryCatalog.FindIcon(state.DefaultIconId)?.ImageBytes);
            var sourceDescription = customPath is not null
                ? "Custom"
                : state.DefaultIconId is not null ? "Action default" : "Blank";

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(BuildDynamicStatePreview(previewBytes, state.DisplayName));
            row.Children.Add(new TextBlock
            {
                Text = state.DisplayName,
                Width = 80,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = string.Equals(state.Id, currentState, StringComparison.OrdinalIgnoreCase)
                    ? FontWeights.Bold
                    : FontWeights.Normal,
            });
            row.Children.Add(new TextBlock
            {
                Text = sourceDescription,
                Width = 80,
                Foreground = CreateBrush("#666666"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var libraryButton = new Button { Content = "Library...", Tag = state, Padding = new Thickness(8, 3, 8, 3) };
            libraryButton.Click += DynamicStateLibraryButton_OnClick;
            row.Children.Add(libraryButton);

            var uploadButton = new Button { Content = "Upload...", Tag = state, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
            uploadButton.Click += DynamicStateUploadButton_OnClick;
            row.Children.Add(uploadButton);

            var removeButton = new Button
            {
                Content = "Reset",
                Tag = state,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(8, 3, 8, 3),
                IsEnabled = customPath is not null,
            };
            removeButton.Click += DynamicStateRemoveButton_OnClick;
            row.Children.Add(removeButton);
            DynamicIconStatesPanel.Children.Add(row);
        }
    }

    private static Border BuildDynamicStatePreview(byte[]? iconBytes, string stateName)
    {
        var preview = new Border
        {
            Width = 38,
            Height = 38,
            Margin = new Thickness(0, 0, 8, 0),
            Background = CreateBrush("#222222"),
            BorderBrush = CreateBrush("#555555"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ToolTip = $"{stateName} icon preview",
        };

        if (iconBytes is null)
        {
            preview.Child = new TextBlock
            {
                Text = "—",
                Foreground = CreateBrush("#999999"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return preview;
        }

        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            using var stream = new MemoryStream(iconBytes, writable: false);
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            preview.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
        }
        catch
        {
            preview.Child = new TextBlock
            {
                Text = "!",
                Foreground = Brushes.OrangeRed,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        return preview;
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

        if (selectedControl.ControlType == DeckControlType.Button)
        {
            if (_bindingStore.GetIconMode(device, selectedControl.ControlIndex) == DeckIconMode.Dynamic
                && _actionCatalog.GetAction(actionId).Visual is null)
            {
                _bindingStore.SetIconMode(device, selectedControl.ControlIndex, DeckIconMode.Static);
            }

            RefreshButtonIcons();
            RefreshBindingEditor();
            QueueIconSync(device);
        }
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

    private void ConfigureActionEditor(
        DeckInputEventType triggerEventType,
        string actionId,
        ComboBox groupComboBox,
        ComboBox actionComboBox)
    {
        var action = _actionCatalog.GetAction(actionId);
        groupComboBox.ItemsSource = _actionCatalog.GetGroupsFor(triggerEventType);
        groupComboBox.SelectedItem = action.Group;
        actionComboBox.ItemsSource = _actionCatalog.GetActionsFor(triggerEventType, action.Group.Id);
        actionComboBox.SelectedItem = action;
    }

    private void ChangeActionGroup(
        DeckInputEventType triggerEventType,
        DeckActionGroup group,
        ComboBox actionComboBox)
    {
        var actions = _actionCatalog.GetActionsFor(triggerEventType, group.Id);
        var firstAction = actions.FirstOrDefault();

        _isUpdatingBindingEditor = true;
        try
        {
            actionComboBox.ItemsSource = actions;
            actionComboBox.SelectedItem = firstAction;
        }
        finally
        {
            _isUpdatingBindingEditor = false;
        }

        if (firstAction is not null)
        {
            SaveBinding(triggerEventType, firstAction.Id);
        }
    }

    private void RefreshButtonIcons()
    {
        var device = GetSelectedDevice();
        if (device is null)
        {
            return;
        }

        foreach (var (key, layout) in _controlLayouts.Where(entry => entry.Value.CanHaveIcon))
        {
            if (!_controlVisuals.TryGetValue(key, out var border))
            {
                continue;
            }

            var iconBytes = ResolveButtonIconBytes(device, layout.ControlIndex);
            border.Child = iconBytes is null
                ? BuildControlLabel(layout, Brushes.Gainsboro)
                : BuildIconImage(iconBytes, layout);
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

        var sourceImages = buttonIndexes.ToDictionary(
            buttonIndex => buttonIndex,
            buttonIndex => ResolveButtonIconBytes(device, buttonIndex));
        var images = _iconStore.LoadButtonImages(
            sourceImages,
            device.Layout.ButtonImageOutputWidth,
            device.Layout.ButtonImageOutputHeight,
            device.Layout.ButtonImageRotationDegreesClockwise);
        _ = Task.Run(() => _monitor.TrySetButtonImages(device.DeviceId, images));
    }

    private bool DeviceUsesStateSource(MonitoredDeckDevice device, string sourceId)
    {
        return device.Layout.Controls
            .Where(control => control.ControlType == DeckControlType.Button && control.CanHaveIcon)
            .Any(control =>
            {
                if (_bindingStore.GetIconMode(device, control.ControlIndex) != DeckIconMode.Dynamic)
                {
                    return false;
                }

                var actionId = _bindingStore.GetActionId(
                    device,
                    DeckControlType.Button,
                    control.ControlIndex,
                    DeckInputEventType.ButtonPress);
                return string.Equals(
                    _actionCatalog.GetAction(actionId).Visual?.StateSourceId,
                    sourceId,
                    StringComparison.OrdinalIgnoreCase);
            });
    }

    private byte[]? ResolveButtonIconBytes(MonitoredDeckDevice device, int buttonIndex)
    {
        var scene = _bindingStore.GetActiveScene(device);
        if (_bindingStore.GetIconMode(device, buttonIndex) == DeckIconMode.Static)
        {
            var staticPath = _iconStore.FindIconPath(device.DeviceId, scene.Id, buttonIndex);
            return ReadIconBytes(staticPath);
        }

        var actionId = _bindingStore.GetActionId(
            device,
            DeckControlType.Button,
            buttonIndex,
            DeckInputEventType.ButtonPress);
        var visual = _actionCatalog.GetAction(actionId).Visual;
        if (visual is null)
        {
            return null;
        }

        var currentState = _actionCatalog.StateCatalog.GetCurrentState(visual.StateSourceId);
        var state = visual.States.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, currentState, StringComparison.OrdinalIgnoreCase));
        if (state is null)
        {
            return null;
        }

        var customPath = _iconStore.FindStateIconPath(device.DeviceId, scene.Id, buttonIndex, state.Id);
        var customBytes = ReadIconBytes(customPath);
        if (customBytes is not null)
        {
            return customBytes;
        }

        return state.DefaultIconId is null
            ? null
            : _iconLibraryCatalog.FindIcon(state.DefaultIconId)?.ImageBytes;
    }

    private static byte[]? ReadIconBytes(string? path)
    {
        try
        {
            return path is null ? null : File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }

    private static FrameworkElement BuildIconImage(byte[] iconBytes, DeckControlLayout control)
    {
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            using var stream = new MemoryStream(iconBytes, writable: false);
            bitmap.StreamSource = stream;
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
