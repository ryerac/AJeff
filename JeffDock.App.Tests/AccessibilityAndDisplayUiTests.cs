using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using JeffDock.App.Controls;
using JeffDock.App.Plugins;
using JeffDock.App.Settings;
using JeffDock.Core.Akp03e;
using JeffDock.Core.Deck;

namespace JeffDock.App.Tests;

public class AccessibilityAndDisplayUiTests
{
    [Fact]
    public void SettingsLoadPerDeviceAndExposeAccessibleEditorsWithoutStartingHardware()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "AJeffTests", Guid.NewGuid().ToString("N"));
            try
            {
                // Load application resources only. Do not run its dispatcher/startup or monitor USB.
                var app = new App();
                app.InitializeComponent();
                var settings = new DisplaySettingsStore(Path.Combine(directory, "display-settings.json"));
                settings.Save("second", new(24, true, 9, DeckSleepBehaviour.ScreenOff));
                var layout = new Akp03eProfile().Layout;
                MonitoredDeckDevice[] devices =
                [
                    new("first", "Test deck", "first", null, layout),
                    new("second", "Test deck", "second", null, layout),
                ];
                var previews = new List<(string DeviceId, DeckDisplaySettings Settings)>();
                var window = new PluginSettingsWindow(new JeffDockPluginLoader(), settings, devices, "first",
                    (id, value) => previews.Add((id, value)));
                var brightness = (Slider)window.FindName("BrightnessSlider");
                var sleep = (CheckBox)window.FindName("DisplaySleepCheckBox");
                var options = (StackPanel)window.FindName("DisplaySleepOptionsPanel");
                Assert.Equal(80, brightness.Value);
                Assert.False(options.IsEnabled);
                sleep.IsChecked = true;
                Assert.True(options.IsEnabled);
                Assert.Empty(previews);
                brightness.Value = 35;
                Assert.Equal(("first", new DeckDisplaySettings(Brightness: 35)), Assert.Single(previews));
                Assert.Equal(80, settings.Get("first").Brightness);
                ((ComboBox)window.FindName("DisplayDeviceComboBox")).SelectedIndex = 1;
                Assert.Equal(2, previews.Count);
                Assert.Equal(("first", new DeckDisplaySettings()), previews.Last());
                Assert.Equal(24, brightness.Value);
                Assert.True(sleep.IsChecked);
                Assert.Equal("9", ((TextBox)window.FindName("SleepMinutesTextBox")).Text);
                Assert.Equal(1, ((ComboBox)window.FindName("SleepBehaviourComboBox")).SelectedIndex);
                Assert.Equal("Display brightness percent", AutomationProperties.GetName(brightness));

                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(730, 840));
                content.Arrange(new Rect(0, 0, 730, 840));
                Assert.True(brightness.ActualWidth > 100);

                var border = new DeckControlBorder { Focusable = true };
                AutomationProperties.SetName(border, "Button 0");
                var peer = UIElementAutomationPeer.CreatePeerForElement(border)!;
                Assert.Equal("Button 0", peer.GetName());
                Assert.True(peer.IsControlElement());
                Assert.Equal(AutomationControlType.ListItem, peer.GetAutomationControlType());
                brightness.Value = 12;
                Assert.Equal(("second", new DeckDisplaySettings(12, true, 9, DeckSleepBehaviour.ScreenOff)), previews.Last());
                Assert.Equal(24, settings.Get("second").Brightness);
                window.Close();
                Assert.Equal(("second", settings.Get("second")), previews.Last());
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF settings test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
