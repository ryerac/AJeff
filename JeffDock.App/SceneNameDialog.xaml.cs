using System.Windows;

namespace JeffDock.App;

public partial class SceneNameDialog : Window
{
    public SceneNameDialog()
        : this("New Scene", "Create", string.Empty)
    {
    }

    public SceneNameDialog(string title, string submitButtonText, string initialName)
    {
        InitializeComponent();
        Title = title;
        SubmitButton.Content = submitButtonText;
        SceneNameTextBox.Text = initialName;
        Loaded += (_, _) =>
        {
            SceneNameTextBox.Focus();
            SceneNameTextBox.SelectAll();
        };
    }

    public string SceneName => SceneNameTextBox.Text.Trim();

    private void CreateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SceneName))
        {
            MessageBox.Show(this, "Enter a scene name.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            SceneNameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
