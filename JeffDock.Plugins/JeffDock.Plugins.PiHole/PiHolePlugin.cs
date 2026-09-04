using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JeffDock.Core.Deck;
using JeffDock.PluginContracts;

namespace JeffDock.Plugins.PiHole;

public sealed class PiHolePlugin : IJeffDockPlugin
{
    public string Id => "jeffdock.pihole";
    public string DisplayName => "Pi-hole Monitor";
    public Version Version => new(1, 0, 0);

    public void Register(IJeffDockPluginRegistry registry)
    {
        var settings = registry.AddSettings(Id,
        [
            new("refreshSeconds", "Refresh interval", "How often configured Pi-holes are checked.",
                PluginSettingType.Integer, "60", Minimum: 15, Maximum: 3600, Suffix: "seconds"),
            new("timeoutSeconds", "Request timeout", "How long to wait before marking a Pi-hole offline.",
                PluginSettingType.Integer, "5", Minimum: 1, Maximum: 30, Suffix: "seconds"),
            new("allowInvalidCertificates", "Allow invalid HTTPS certificates",
                "Only enable this for a trusted local Pi-hole using a self-signed certificate.",
                PluginSettingType.Boolean, "false"),
        ]);

        var monitor = new PiHoleStateSource(settings);
        registry.AddStateSource(monitor);
        registry.AddAction(new PiHoleMonitorAction(monitor));
        registry.AddPresetJson(Presets);
    }

    private const string Presets = """
        {
          "version": 1,
          "sections": [
            {
              "id": "pihole",
              "name": "Pi-hole",
              "pluginId": "jeffdock.pihole",
              "presets": [
                {
                  "id": "pihole.monitor",
                  "name": "Pi-hole Monitor",
                  "description": "Show availability and the number of blocked queries in the last 24 hours. Press to refresh.",
                  "controlTypes": [ "Button" ],
                  "bindings": [ { "trigger": "Press", "actionId": "jeffdock.pihole.refresh" } ],
                  "iconMode": "Dynamic"
                }
              ]
            }
          ]
        }
        """;
}

internal sealed class PiHoleMonitorAction(PiHoleStateSource state) : IPluginDeckAction
{
    public string Id => "jeffdock.pihole.refresh";
    public string DisplayName => "Monitor / Refresh";
    public PluginActionGroup Group { get; } = new("pihole", "Pi-hole");
    public PluginActionVisual Visual { get; } = new(
        PiHoleStateSource.StateSourceId,
        [new("checking", "Checking"), new("online", "Online"), new("offline", "Offline"), new("authentication", "Authentication required"), new("invalid", "Invalid URL")],
        IsImageManaged: true);
    public IReadOnlyList<PluginSettingDefinition> Parameters { get; } =
    [
        new("url", "Pi-hole URL", "Base address, for example http://pi.hole or http://192.168.1.2.", PluginSettingType.String, "http://pi.hole"),
        new("name", "Display name", "Short label shown on the button.", PluginSettingType.String, "PI-HOLE"),
        new("password", "Application password", "Pi-hole v6 application password. Leave blank when API authentication is disabled.", PluginSettingType.Password, ""),
    ];

    public bool Supports(DeckInputEventType triggerEventType) => triggerEventType == DeckInputEventType.ButtonPress;
    public void Execute(PluginActionContext context) => state.Refresh(PiHoleKey.From(context), context.Parameters, force: true);
}

internal sealed class PiHoleStateSource : IPluginDeckStateSource
{
    public const string StateSourceId = "jeffdock.pihole.state";
    private readonly object _sync = new();
    private readonly IPluginSettings _settings;
    private readonly HttpClient _client;
    private readonly Dictionary<PiHoleKey, MonitorEntry> _entries = [];
    private System.Threading.Timer? _timer;
    private bool _disposed;

    public PiHoleStateSource(IPluginSettings settings)
    {
        _settings = settings;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None || ReadBoolean("allowInvalidCertificates"),
        };
        _client = new HttpClient(handler);
        _settings.Changed += OnSettingChanged;
    }

    public string Id => StateSourceId;
    public string CurrentState => "checking";
    public event EventHandler<string>? StateChanged;

    public void Start() => ResetTimer();

    public string GetCurrentState(PluginVisualContext context)
    {
        var entry = Observe(context);
        return entry.Status;
    }

    public byte[] GetCurrentImageBytes(PluginVisualContext context)
    {
        var entry = Observe(context);
        return Render(entry);
    }

    public void Refresh(PiHoleKey key, IReadOnlyDictionary<string, string> parameters, bool force)
    {
        MonitorEntry entry;
        lock (_sync)
        {
            if (_disposed) return;
            entry = Upsert(key, parameters);
            if (entry.IsRefreshing || (!force && DateTime.UtcNow - entry.CheckedAt < RefreshInterval)) return;
            entry.IsRefreshing = true;
            entry.Status = "checking";
        }

        StateChanged?.Invoke(this, "checking");
        _ = CheckAsync(key, entry);
    }

    private MonitorEntry Observe(PluginVisualContext context)
    {
        var key = PiHoleKey.From(context);
        MonitorEntry entry;
        lock (_sync) entry = Upsert(key, context.Parameters);
        Refresh(key, context.Parameters, force: false);
        return entry;
    }

    private MonitorEntry Upsert(PiHoleKey key, IReadOnlyDictionary<string, string> parameters)
    {
        var url = GetParameter(parameters, "url", "http://pi.hole");
        var name = GetParameter(parameters, "name", "PI-HOLE");
        var password = GetParameter(parameters, "password", "");
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new MonitorEntry(url, name, password);
            _entries[key] = entry;
        }
        else if (entry.Url != url || entry.Name != name || entry.Password != password)
        {
            entry = new MonitorEntry(url, name, password);
            _entries[key] = entry;
        }
        return entry;
    }

    private async Task CheckAsync(PiHoleKey key, MonitorEntry request)
    {
        var status = "offline";
        long? blocked = null;
        string? detail = null;
        try
        {
            if (!TryGetApiBase(request.Url, out var apiBase))
            {
                status = "invalid";
                detail = "BAD URL";
            }
            else
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(ReadInteger("timeoutSeconds")));
                string? sid = null;
                if (!string.IsNullOrEmpty(request.Password))
                {
                    using var auth = await _client.PostAsJsonAsync(new Uri(apiBase, "auth"), new { password = request.Password }, cancellation.Token);
                    if (auth.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        status = "authentication";
                        detail = "AUTH FAILED";
                    }
                    else
                    {
                        auth.EnsureSuccessStatusCode();
                        using var document = JsonDocument.Parse(await auth.Content.ReadAsStreamAsync(cancellation.Token));
                        sid = document.RootElement.GetProperty("session").GetProperty("sid").GetString();
                    }
                }

                if (detail is null)
                {
                    using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(apiBase, "stats/summary"));
                    if (!string.IsNullOrEmpty(sid)) message.Headers.Add("X-FTL-SID", sid);
                    using var response = await _client.SendAsync(message, cancellation.Token);
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        status = "authentication";
                        detail = "AUTH NEEDED";
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellation.Token));
                        blocked = document.RootElement.GetProperty("queries").GetProperty("blocked").GetInt64();
                        status = "online";
                    }
                }

                if (!string.IsNullOrEmpty(sid))
                {
                    using var logout = new HttpRequestMessage(HttpMethod.Delete, new Uri(apiBase, "auth"));
                    logout.Headers.Add("X-FTL-SID", sid);
                    using var logoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try { await _client.SendAsync(logout, logoutCancellation.Token); } catch { }
                }
            }
        }
        catch (OperationCanceledException) { detail = "TIMEOUT"; }
        catch (HttpRequestException) { detail = "OFFLINE"; }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            detail = "API ERROR";
        }

        lock (_sync)
        {
            if (_disposed || !_entries.TryGetValue(key, out var current) || !ReferenceEquals(current, request)) return;
            current.Status = status;
            current.Blocked = blocked;
            current.Detail = detail;
            current.CheckedAt = DateTime.UtcNow;
            current.IsRefreshing = false;
        }
        StateChanged?.Invoke(this, status);
    }

    private static bool TryGetApiBase(string value, out Uri apiBase)
    {
        apiBase = null!;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        var builder = new UriBuilder(uri) { Query = "", Fragment = "" };
        var path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/admin", StringComparison.OrdinalIgnoreCase)) path = path[..^6];
        if (path.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        builder.Path = path + "/api/";
        apiBase = builder.Uri;
        return true;
    }

    private void Poll(object? state)
    {
        List<(PiHoleKey Key, Dictionary<string, string> Parameters)> targets;
        lock (_sync)
        {
            targets = _entries.Select(pair => (pair.Key, new Dictionary<string, string>
            {
                ["url"] = pair.Value.Url, ["name"] = pair.Value.Name, ["password"] = pair.Value.Password,
            })).ToList();
        }
        foreach (var target in targets) Refresh(target.Key, target.Parameters, force: true);
    }

    private void ResetTimer()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            if (!_disposed) _timer = new System.Threading.Timer(Poll, null, RefreshInterval, RefreshInterval);
        }
    }

    private void OnSettingChanged(object? sender, PluginSettingChangedEventArgs e) => ResetTimer();
    private TimeSpan RefreshInterval => TimeSpan.FromSeconds(ReadInteger("refreshSeconds"));
    private int ReadInteger(string key) => int.Parse(_settings.GetValue(key), CultureInfo.InvariantCulture);
    private bool ReadBoolean(string key) => bool.Parse(_settings.GetValue(key));
    private static string GetParameter(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) ? value.Trim() : fallback;

    public void Dispose()
    {
        lock (_sync) { _disposed = true; _timer?.Dispose(); _timer = null; }
        _settings.Changed -= OnSettingChanged;
        _client.Dispose();
    }

    private static byte[] Render(MonitorEntry entry)
    {
        using var bitmap = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var color = entry.Status switch
        {
            "online" => Color.FromArgb(58, 190, 105),
            "checking" => Color.FromArgb(241, 181, 56),
            _ => Color.FromArgb(222, 70, 70),
        };
        graphics.Clear(Color.FromArgb(18, 24, 31));
        using var statusBrush = new SolidBrush(color);
        graphics.FillEllipse(statusBrush, 8, 9, 12, 12);
        DrawText(graphics, Truncate(entry.Name.ToUpperInvariant(), 13), 13, FontStyle.Bold, Color.White, new RectangleF(24, 4, 98, 25));

        if (entry.Status == "online" && entry.Blocked.HasValue)
        {
            DrawText(graphics, FormatCount(entry.Blocked.Value), 31, FontStyle.Bold, Color.White, new RectangleF(3, 30, 122, 45));
            DrawText(graphics, "BLOCKED / 24H", 12, FontStyle.Bold, Color.FromArgb(178, 190, 203), new RectangleF(3, 78, 122, 22));
            DrawText(graphics, "ONLINE", 12, FontStyle.Bold, color, new RectangleF(3, 103, 122, 19));
        }
        else
        {
            DrawText(graphics, entry.Detail ?? (entry.Status == "checking" ? "CHECKING" : "OFFLINE"), 17, FontStyle.Bold, color, new RectangleF(3, 42, 122, 32));
            DrawText(graphics, "PI-HOLE", 12, FontStyle.Bold, Color.FromArgb(178, 190, 203), new RectangleF(3, 80, 122, 22));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static void DrawText(Graphics graphics, string text, float size, FontStyle style, Color color, RectangleF bounds)
    {
        using var font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static string FormatCount(long count) => count switch
    {
        >= 1_000_000 => $"{count / 1_000_000d:0.#}M",
        >= 10_000 => $"{count / 1_000d:0.#}K",
        _ => count.ToString("N0", CultureInfo.InvariantCulture),
    };
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private sealed class MonitorEntry(string url, string name, string password)
    {
        public string Url { get; } = url;
        public string Name { get; } = name;
        public string Password { get; } = password;
        public string Status { get; set; } = "checking";
        public string? Detail { get; set; }
        public long? Blocked { get; set; }
        public DateTime CheckedAt { get; set; }
        public bool IsRefreshing { get; set; }
    }
}

internal readonly record struct PiHoleKey(string DeviceId, string SceneId, int ControlIndex)
{
    public static PiHoleKey From(PluginActionContext context) => new(context.Device.DeviceId, context.SceneId, context.InputEvent.ControlIndex);
    public static PiHoleKey From(PluginVisualContext context) => new(context.DeviceId, context.SceneId, context.ControlIndex);
}
