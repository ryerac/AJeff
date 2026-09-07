using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using JeffDock.Core.Deck;
using JeffDock.PluginContracts;

namespace JeffDock.Plugins.System;

internal sealed class UsageAction(string metric, string name, string stateSourceId) : IPluginDeckAction
{
    public string Id => $"jeffdock.system.{metric}";
    public string DisplayName => name;
    public PluginActionGroup Group { get; } = new("system", "System");
    public PluginActionVisual Visual { get; } = new(stateSourceId,
        [new("ready", "Live usage"), new("unavailable", "Waiting for reading")], IsImageManaged: true);
    public bool Supports(DeckInputEventType triggerEventType) => triggerEventType == DeckInputEventType.ButtonPress;
    public void Execute(PluginActionContext context) { }
}

internal sealed class UsageStateSource(string metric, string label, Func<double?> read) : IPluginDeckStateSource
{
    private readonly object _sync = new();
    private global::System.Threading.Timer? _timer;
    private bool _disposed;
    private int? _percentage;
    private byte[] _image = Render(label, null);

    public string Id => $"jeffdock.system.{metric}.state";
    public string CurrentState { get { lock (_sync) return _percentage.HasValue ? "ready" : "unavailable"; } }
    public byte[] CurrentImageBytes { get { lock (_sync) return _image.ToArray(); } }
    public event EventHandler<string>? StateChanged;

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _timer ??= new global::System.Threading.Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
    }

    internal void Poll()
    {
        string state;
        lock (_sync)
        {
            if (_disposed) return;
            double? value;
            try { value = read(); }
            catch (Win32Exception) { value = null; }
            var percentage = value is { } sample && double.IsFinite(sample)
                ? (int?)Math.Round(Math.Clamp(sample, 0, 100), MidpointRounding.AwayFromZero) : null;
            if (_percentage == percentage) return;
            _image = Render(label, percentage);
            _percentage = percentage;
            state = percentage.HasValue ? "ready" : "unavailable";
        }
        StateChanged?.Invoke(this, state);
    }

    private static byte[] Render(string label, int? percentage)
    {
        using var bitmap = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.FromArgb(20, 24, 33));
        using var labelFont = new Font("Segoe UI", 23, FontStyle.Bold, GraphicsUnit.Pixel);
        using var numberFont = new Font("Segoe UI", 38, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        using var accent = new SolidBrush(label == "CPU" ? Color.FromArgb(84, 170, 255) : Color.FromArgb(58, 210, 166));
        using var track = new SolidBrush(Color.FromArgb(55, 64, 79));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(label, labelFont, accent, new RectangleF(0, 9, 128, 31), format);
        graphics.DrawString(percentage is { } value ? $"{value}%" : "--", numberFont, textBrush,
            new RectangleF(0, 42, 128, 51), format);
        graphics.FillRectangle(track, 12, 108, 104, 8);
        if (percentage is { } fill) graphics.FillRectangle(accent, 12, 108, 104 * fill / 100f, 8);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
