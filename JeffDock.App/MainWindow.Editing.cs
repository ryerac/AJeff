using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using JeffDock.App.Bindings;
using JeffDock.App.Bindings.Core;
using JeffDock.Core.Deck;

namespace JeffDock.App;

public partial class MainWindow
{
    // Keep action parameters inside AJeff, including any plugin credentials.
    private ControlConfigurationSnapshot? _copiedControl;

    private void ConfigureControlAccessibility(Border border, DeckControlLayout control)
    {
        border.Focusable = true;
        KeyboardNavigation.SetIsTabStop(border, true);
        AutomationProperties.SetName(border, DescribeControl(control));
        AutomationProperties.SetHelpText(border,
            "Use arrow keys to select a control, Enter to edit, Ctrl+C to copy and Ctrl+V to paste within AJeff.");
        border.GotKeyboardFocus += (_, _) => SelectControl(control);
        border.LostKeyboardFocus += (_, _) => RefreshSelectionVisuals();
        border.KeyDown += ControlBorder_OnKeyDown;
        border.ContextMenu = new ContextMenu();
        var copy = new MenuItem { Header = "_Copy control", InputGestureText = "Ctrl+C" };
        var paste = new MenuItem { Header = "_Paste control", InputGestureText = "Ctrl+V" };
        var reset = new MenuItem { Header = "_Reset control", InputGestureText = "Delete" };
        copy.Click += (_, _) => CopyControl(control);
        paste.Click += (_, _) => PasteControl(control);
        reset.Click += (sender, e) =>
        {
            SelectControl(control);
            ResetControlButton_OnClick(sender, e);
        };
        border.ContextMenu.Items.Add(copy);
        border.ContextMenu.Items.Add(paste);
        border.ContextMenu.Items.Add(new Separator());
        border.ContextMenu.Items.Add(reset);
        border.ContextMenuOpening += (_, _) =>
        {
            border.Focus();
            paste.IsEnabled = _copiedControl?.CanPasteTo(control) == true;
        };
    }

    private void SelectControl(DeckControlLayout control)
    {
        _selectedControl = (control.ControlType, control.ControlIndex);
        RefreshSelectionVisuals();
        RefreshBindingEditor();
    }

    private void ControlBorder_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Border { Tag: DeckControlLayout control }) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.C or Key.V)
        {
            if (e.Key == Key.C) CopyControl(control);
            else PasteControl(control);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key is Key.Enter or Key.Space)
        {
            SelectControl(control);
            (control.ControlType == DeckControlType.Encoder ? TurnActionGroupComboBox : PressActionGroupComboBox).Focus();
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var horizontal = e.Key is Key.Left or Key.Right;
            var sign = e.Key is Key.Left or Key.Up ? -1 : 1;
            var x = control.X + control.Width / 2;
            var y = control.Y + control.Height / 2;
            var next = _controlLayouts.Values
                .Select(candidate => new
                {
                    Control = candidate,
                    Dx = candidate.X + candidate.Width / 2 - x,
                    Dy = candidate.Y + candidate.Height / 2 - y,
                })
                .Where(candidate => (horizontal ? candidate.Dx : candidate.Dy) * sign > 1)
                .OrderBy(candidate => Math.Abs(horizontal ? candidate.Dx : candidate.Dy)
                    + 2 * Math.Abs(horizontal ? candidate.Dy : candidate.Dx))
                .FirstOrDefault();
            if (next is not null)
                _controlVisuals[(next.Control.ControlType, next.Control.ControlIndex)].Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            SelectControl(control);
            ResetControlButton_OnClick(sender, e);
            e.Handled = true;
        }
    }

    private ControlConfigurationSnapshot CaptureControl(MonitoredDeckDevice device, DeckControlLayout control)
    {
        var scene = _bindingStore.GetActiveScene(device);
        return new ControlConfigurationSnapshot(control.ControlType,
            _bindingStore.CaptureControl(device, control.ControlType, control.ControlIndex),
            control.CanHaveIcon ? _bindingStore.GetIconMode(device, control.ControlIndex) : DeckIconMode.Static,
            control.CanHaveIcon ? _iconStore.CaptureControlIcons(device.DeviceId, scene.Id, control.ControlIndex)
                : new ControlIconSnapshot(null, new Dictionary<string, byte[]>()));
    }

    private void CopyControl(DeckControlLayout control)
    {
        if (GetSelectedDevice() is not { } device) return;
        try
        {
            _copiedControl = CaptureControl(device, control);
            EditingStatusText.Text = $"Copied {DescribeControl(control)}. Select a compatible control and paste.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Could not copy control", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PasteControl(DeckControlLayout control)
    {
        if (_copiedControl is not { } snapshot || GetSelectedDevice() is not { } device) return;
        if (!snapshot.CanPasteTo(control))
        {
            EditingStatusText.Text = "Paste requires the same control type and a display if artwork is included.";
            return;
        }

        try
        {
            var previous = CaptureControl(device, control);
            var hasConfiguration = previous.Bindings.Any(binding => binding.ActionId != NoAction.ActionId)
                || previous.IconMode == DeckIconMode.Dynamic
                || previous.Icons.StaticImage is not null || previous.Icons.StateImages.Count > 0;
            if (hasConfiguration && MessageBox.Show(this,
                    $"Replace all actions and artwork for {DescribeControl(control)} in the current scene?",
                    "Paste Control", MessageBoxButton.YesNo, MessageBoxImage.Question,
                    MessageBoxResult.No) != MessageBoxResult.Yes) return;

            var scene = _bindingStore.GetActiveScene(device);
            void Apply(ControlConfigurationSnapshot value)
            {
                if (control.CanHaveIcon)
                    _iconStore.ReplaceControlIcons(device.DeviceId, scene.Id, control.ControlIndex, value.Icons);
                _bindingStore.ApplyControlPreset(device, control.ControlType, control.ControlIndex,
                    value.Bindings, value.IconMode);
            }
            try { Apply(snapshot); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                try { Apply(previous); }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"Paste failed: {exception.Message} Restoring the previous configuration also failed: {rollback.Message}", exception);
                }
                throw;
            }

            SelectControl(control);
            RefreshButtonIcons();
            QueueIconSync(device);
            EditingStatusText.Text = $"Pasted configuration to {DescribeControl(control)}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message, "Could not paste control", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
