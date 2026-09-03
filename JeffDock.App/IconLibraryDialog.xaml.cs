using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JeffDock.App.Icons;

namespace JeffDock.App;

public partial class IconLibraryDialog : Window
{
    private const int PageSize = 96;
    private readonly IReadOnlyList<IconLibraryItem> _icons;
    private int _pageIndex;

    public IconLibraryItem? SelectedIcon { get; private set; }
    public byte[]? SelectedImageBytes { get; private set; }

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
        _pageIndex = 0;
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

        RefreshFilteredIcons();
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _pageIndex = 0;
        RefreshFilteredIcons();
    }

    private void RefreshFilteredIcons()
    {
        if (PackComboBox.SelectedItem is not IconPackChoice pack)
        {
            return;
        }

        var category = CategoryComboBox.SelectedItem as string;
        var search = SearchTextBox.Text.Trim();
        var filtered = _icons.Where(icon =>
                string.Equals(icon.PackId, pack.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(icon.Category, category, StringComparison.CurrentCultureIgnoreCase)
                && (search.Length == 0
                    || icon.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                    || icon.Id.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var pageCount = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);
        IconsListBox.ItemsSource = filtered.Skip(_pageIndex * PageSize).Take(PageSize).ToList();
        ResultCountText.Text = filtered.Count == 0
            ? "No icons"
            : $"{filtered.Count:N0} icons · page {_pageIndex + 1}/{pageCount}";
        PreviousPageButton.IsEnabled = _pageIndex > 0;
        NextPageButton.IsEnabled = _pageIndex + 1 < pageCount;
    }

    private void PreviousPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_pageIndex > 0)
        {
            _pageIndex--;
            RefreshFilteredIcons();
        }
    }

    private void NextPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        _pageIndex++;
        RefreshFilteredIcons();
    }

    private void IconsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ColourPanel.Visibility = IconsListBox.SelectedItem is IconLibraryItem { IsVector: true }
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSelectedPreview();
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

        byte[] rendered;
        try
        {
            rendered = RenderSelectedIcon(icon);
        }
        catch (Exception exception)
        {
            ColourErrorText.Text = exception.Message;
            return;
        }

        SelectedIcon = icon;
        SelectedImageBytes = rendered;
        DialogResult = true;
    }

    private void ColourTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateColourSwatches();
        UpdateSelectedPreview();
    }

    private void ForegroundColourButton_OnClick(object sender, RoutedEventArgs e)
    {
        PickColour(ForegroundTextBox);
    }

    private void BackgroundColourButton_OnClick(object sender, RoutedEventArgs e)
    {
        PickColour(BackgroundTextBox);
    }

    private void PickColour(TextBox target)
    {
        using var picker = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            AnyColor = true,
        };

        try
        {
            var current = (Color)ColorConverter.ConvertFromString(SvgIconRenderer.NormalizeColor(target.Text));
            picker.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
        }
        catch (FormatException)
        {
            // An invalid typed value should not prevent the picker from opening.
        }

        if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            target.Text = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
        }
    }

    private void UpdateColourSwatches()
    {
        SetSwatch(ForegroundColourButton, ForegroundTextBox);
        SetSwatch(BackgroundColourButton, BackgroundTextBox);
    }

    private static void SetSwatch(Button? button, TextBox? textBox)
    {
        if (button is null || textBox is null)
        {
            return;
        }

        try
        {
            var colour = (Color)ColorConverter.ConvertFromString(SvgIconRenderer.NormalizeColor(textBox.Text));
            button.Background = new SolidColorBrush(colour);
        }
        catch (FormatException)
        {
            button.Background = Brushes.Transparent;
        }
    }

    private void BackgroundMode_OnChanged(object sender, RoutedEventArgs e)
    {
        if (BackgroundTextBox is not null)
        {
            BackgroundTextBox.IsEnabled = TransparentBackgroundCheckBox.IsChecked != true;
            BackgroundColourButton.IsEnabled = TransparentBackgroundCheckBox.IsChecked != true;
        }

        UpdateSelectedPreview();
    }

    private void UpdateSelectedPreview()
    {
        if (UseIconButton is null || IconsListBox.SelectedItem is not IconLibraryItem icon)
        {
            if (UseIconButton is not null)
            {
                UseIconButton.IsEnabled = false;
            }
            return;
        }

        try
        {
            var bytes = RenderSelectedIcon(icon);
            SelectedPreviewImage.Source = SvgIconRenderer.ToBitmapSource(bytes);
            ColourErrorText.Text = string.Empty;
            UseIconButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            ColourErrorText.Text = exception.Message;
            UseIconButton.IsEnabled = false;
        }
    }

    private byte[] RenderSelectedIcon(IconLibraryItem icon)
    {
        var background = TransparentBackgroundCheckBox.IsChecked == true
            ? null
            : BackgroundTextBox.Text;
        return icon.GetRenderedBytes(ForegroundTextBox.Text, background);
    }

    private sealed record IconPackChoice(string Id, string Name);
}
