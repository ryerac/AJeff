using System.Configuration;
using System.Data;
using System.Windows;

namespace JeffDock.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Any(argument => string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase)))
        {
            window.WindowState = WindowState.Minimized;
        }
        window.Show();
    }
}

