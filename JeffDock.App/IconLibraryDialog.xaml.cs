using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JeffDock.App.Icons;

namespace JeffDock.App;

public partial class IconLibraryDialog : Window
{
    private readonly IReadOnlyList<IconLibraryItem> _icons;

    public IconLibraryItem? SelectedIcon { get; private set; }

    public IconLibraryDialog(IReadOnlyList<IconLibraryItem> icons)
    {
        InitializeComponent();
        _icons = icons;

        var packs = icons
            .Select(icon => new IconPackChoice(icon.PackId, icon.PackName))
            .DistinctBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        PackComboBox.ItemsSource = packs;
        PackComboBox.SelectedIndex = packs.Count > 0 ? 0 : -1;
    }

    private void FilterComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PackComboBox.SelectedItem is not IconPackChoice pack)
        {
            IconsListBox.ItemsSource = null;
            CategoryComboBox.ItemsSource = null;
            return;
        }

        if (ReferenceEquals(sender, PackComboBox))
        {
            var categories = _icons
                .Where(icon => string.Equals(icon.PackId, pack.Id, StringComparison.OrdinalIgnoreCase))
                .Select(icon => icon.Category)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            CategoryComboBox.ItemsSource = categories;
            CategoryComboBox.SelectedIndex = categories.Count > 0 ? 0 : -1;
        }

        var category = CategoryComboBox.SelectedItem as string;
        IconsListBox.ItemsSource = _icons.Where(icon =>
                string.Equals(icon.PackId, pack.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(icon.Category, category, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    private void IconsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UseIconButton.IsEnabled = IconsListBox.SelectedItem is IconLibraryItem;
    }

    private void IconsListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectIcon();
    }

    private void UseIconButton_OnClick(object sender, RoutedEventArgs e)
    {
        SelectIcon();
    }

    private void SelectIcon()
    {
        if (IconsListBox.SelectedItem is not IconLibraryItem icon)
        {
            return;
        }

        SelectedIcon = icon;
        DialogResult = true;
    }

    private sealed record IconPackChoice(string Id, string Name);
}
