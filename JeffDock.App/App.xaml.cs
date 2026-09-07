using System.Configuration;
using System.Data;
using System.Windows;

namespace JeffDock.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\AJeff.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out _ownsSingleInstanceMutex);
        if (!_ownsSingleInstanceMutex)
        {
            MessageBox.Show(
                "Another instance of AJeff is already running. Close it before starting this version.",
                "AJeff is already running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var simulatedDeviceIds = e.Args
            .Select(argument => string.Equals(argument, "--simulate-akp153", StringComparison.OrdinalIgnoreCase)
                ? "ajazz-akp153"
                : argument.StartsWith("--simulate=", StringComparison.OrdinalIgnoreCase)
                    ? argument["--simulate=".Length..]
                    : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var window = new MainWindow(simulatedDeviceIds);
        MainWindow = window;
        if (e.Args.Any(argument => string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase)))
        {
            window.WindowState = WindowState.Minimized;
        }
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

