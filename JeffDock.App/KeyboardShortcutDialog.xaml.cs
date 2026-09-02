using System.Windows;
using System.Windows.Input;
using JeffDock.App.Bindings.Core;

namespace JeffDock.App;

public partial class KeyboardShortcutDialog : Window
{
    internal KeyboardShortcut? Shortcut { get; private set; }

    internal KeyboardShortcutDialog(KeyboardShortcut? initialShortcut = null)
    {
        InitializeComponent();
        Shortcut = initialShortcut;
        if (initialShortcut is not null)
        {
            ShortcutText.Text = initialShortcut.ToString();
            SaveButton.IsEnabled = true;
        }
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };

        var shortcut = KeyboardShortcut.FromKey(key, Keyboard.Modifiers);
        if (shortcut is not null)
        {
            Shortcut = shortcut;
            ShortcutText.Text = shortcut.ToString();
            SaveButton.IsEnabled = true;
        }
        else
        {
            ShortcutText.Text = "Hold modifiers, then press another key...";
        }

        e.Handled = true;
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Shortcut is not null)
        {
            DialogResult = true;
        }
    }
}
