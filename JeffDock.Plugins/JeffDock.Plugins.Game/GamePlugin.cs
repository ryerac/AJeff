using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using JeffDock.Core.Deck;
using JeffDock.PluginContracts;

namespace JeffDock.Plugins.Game;

public sealed class GamePlugin : IJeffDockPlugin
{
    public string Id => "jeffdock.game";
    public string DisplayName => "Fun";
    public Version Version => new(1, 0, 0);

    public void Register(IJeffDockPluginRegistry registry)
    {
        var diceState = new GameStateSource(GameStateSource.DiceStateSourceId, isCoin: false);
        var coinState = new GameStateSource(GameStateSource.CoinStateSourceId, isCoin: true);
        registry.AddStateSource(diceState);
        registry.AddStateSource(coinState);
        registry.AddAction(new DiceAction(diceState));
        registry.AddAction(new CoinFlipAction(coinState));
        registry.AddPresetJson(Presets);
    }

    private const string Presets = """
        {
          "version": 1,
          "sections": [
            {
              "id": "game",
              "name": "Fun",
              "presets": [
                {
                  "id": "game.dice",
                  "name": "Dice",
                  "description": "Roll a six-sided die and show the result.",
                  "controlTypes": [ "Button" ],
                  "bindings": [ { "trigger": "Press", "actionId": "jeffdock.game.dice" } ],
                  "iconMode": "Dynamic"
                },
                {
                  "id": "game.coin-flip",
                  "name": "Coin Flip",
                  "description": "Flip a coin and show heads or tails.",
                  "controlTypes": [ "Button" ],
                  "bindings": [ { "trigger": "Press", "actionId": "jeffdock.game.coin-flip" } ],
                  "iconMode": "Dynamic"
                }
              ]
            }
          ]
        }
        """;
}

internal abstract class GameAction(GameStateSource state)
{
    private readonly object _sync = new();
    private readonly Dictionary<GameKey, DateTime> _lastPressByControl = [];

    protected bool AcceptPress(PluginActionContext context)
    {
        var key = GameKey.From(context);
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (_lastPressByControl.TryGetValue(key, out var last) && now - last < TimeSpan.FromMilliseconds(500))
            {
                return false;
            }

            _lastPressByControl[key] = now;
            return true;
        }
    }

    protected GameStateSource State { get; } = state;
}

internal sealed class DiceAction(GameStateSource state) : GameAction(state), IPluginDeckAction
{
    public string Id => "jeffdock.game.dice";
    public string DisplayName => "Roll D6";
    public PluginActionGroup Group { get; } = new("game", "Fun");
    public PluginActionVisual Visual { get; } = new(
        GameStateSource.DiceStateSourceId,
        [new("ready", "Ready"), new("rolled", "Rolled")],
        IsImageManaged: true);

    public bool Supports(DeckInputEventType triggerEventType) => triggerEventType == DeckInputEventType.ButtonPress;

    public void Execute(PluginActionContext context)
    {
        if (AcceptPress(context))
        {
            State.RollDice(GameKey.From(context));
        }
    }
}

internal sealed class CoinFlipAction(GameStateSource state) : GameAction(state), IPluginDeckAction
{
    public string Id => "jeffdock.game.coin-flip";
    public string DisplayName => "Flip Coin";
    public PluginActionGroup Group { get; } = new("game", "Fun");
    public PluginActionVisual Visual { get; } = new(
        GameStateSource.CoinStateSourceId,
        [new("ready", "Ready"), new("heads", "Heads"), new("tails", "Tails")],
        IsImageManaged: true);

    public bool Supports(DeckInputEventType triggerEventType) => triggerEventType == DeckInputEventType.ButtonPress;

    public void Execute(PluginActionContext context)
    {
        if (AcceptPress(context))
        {
            State.FlipCoin(GameKey.From(context));
        }
    }
}

internal sealed class GameStateSource(string id, bool isCoin) : IPluginDeckStateSource
{
    public const string DiceStateSourceId = "jeffdock.game.dice.state";
    public const string CoinStateSourceId = "jeffdock.game.coin.state";

    private readonly object _sync = new();
    private readonly Dictionary<GameKey, int> _diceResults = [];
    private readonly Dictionary<GameKey, bool> _coinResults = [];

    public string Id { get; } = id;
    public string CurrentState => "ready";
    public event EventHandler<string>? StateChanged;

    public string GetCurrentState(PluginVisualContext context)
    {
        var key = GameKey.From(context);
        lock (_sync)
        {
            if (!isCoin && _diceResults.ContainsKey(key)) return "rolled";
            if (isCoin && _coinResults.TryGetValue(key, out var heads)) return heads ? "heads" : "tails";
            return "ready";
        }
    }

    public byte[] GetCurrentImageBytes(PluginVisualContext context)
    {
        var key = GameKey.From(context);
        lock (_sync)
        {
            if (!isCoin && _diceResults.TryGetValue(key, out var result)) return RenderDie(result);
            if (isCoin && _coinResults.TryGetValue(key, out var heads)) return RenderCoin(heads);
        }

        return isCoin ? RenderCoin(null) : RenderDie(null);
    }

    public void RollDice(GameKey key)
    {
        lock (_sync)
        {
            _coinResults.Remove(key);
            _diceResults[key] = Random.Shared.Next(1, 7);
        }

        StateChanged?.Invoke(this, "rolled");
    }

    public void FlipCoin(GameKey key)
    {
        bool heads;
        lock (_sync)
        {
            _diceResults.Remove(key);
            heads = Random.Shared.Next(2) == 0;
            _coinResults[key] = heads;
        }

        StateChanged?.Invoke(this, heads ? "heads" : "tails");
    }

    public void Start() { }
    public void Dispose() { }

    private static byte[] RenderDie(int? value)
    {
        using var bitmap = CreateBitmap(out var graphics);
        using (graphics)
        {
            graphics.Clear(Color.FromArgb(21, 27, 38));
            using var dieBrush = new SolidBrush(Color.FromArgb(246, 247, 250));
            using var outline = new Pen(Color.FromArgb(185, 195, 210), 3);
            graphics.FillRoundedRectangle(dieBrush, new Rectangle(22, 15, 84, 84), new Size(15, 15));
            graphics.DrawRoundedRectangle(outline, new Rectangle(22, 15, 84, 84), new Size(15, 15));

            var face = value ?? 5;
            using var pipBrush = new SolidBrush(Color.FromArgb(28, 33, 44));
            foreach (var point in GetPips(face))
            {
                graphics.FillEllipse(pipBrush, point.X - 7, point.Y - 7, 14, 14);
            }

            DrawCaption(graphics, value is null ? "D6" : $"ROLLED {value}");
        }
        return Encode(bitmap);
    }

    private static byte[] RenderCoin(bool? result)
    {
        using var bitmap = CreateBitmap(out var graphics);
        using (graphics)
        {
            graphics.Clear(Color.FromArgb(21, 27, 38));
            var heads = result == true;
            using var coinBrush = new SolidBrush(result is null || heads ? Color.FromArgb(248, 195, 54) : Color.FromArgb(205, 211, 221));
            using var outline = new Pen(result is null || heads ? Color.FromArgb(255, 224, 119) : Color.WhiteSmoke, 4);
            graphics.FillEllipse(coinBrush, 25, 13, 78, 78);
            graphics.DrawEllipse(outline, 25, 13, 78, 78);
            using var letterFont = new Font("Segoe UI", 44, FontStyle.Bold, GraphicsUnit.Pixel);
            using var letterBrush = new SolidBrush(Color.FromArgb(42, 45, 52));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(result is null ? "?" : heads ? "H" : "T", letterFont, letterBrush, new RectangleF(25, 13, 78, 78), format);
            DrawCaption(graphics, result is null ? "COIN" : heads ? "HEADS" : "TAILS");
        }
        return Encode(bitmap);
    }

    private static Bitmap CreateBitmap(out Graphics graphics)
    {
        var bitmap = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
        graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return bitmap;
    }

    private static void DrawCaption(Graphics graphics, string caption)
    {
        using var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(caption, font, brush, new RectangleF(0, 101, 128, 24), format);
    }

    private static byte[] Encode(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static IEnumerable<Point> GetPips(int value)
    {
        if (value is 1 or 3 or 5) yield return new Point(64, 57);
        if (value >= 2) { yield return new Point(43, 36); yield return new Point(85, 78); }
        if (value >= 4) { yield return new Point(85, 36); yield return new Point(43, 78); }
        if (value == 6) { yield return new Point(43, 57); yield return new Point(85, 57); }
    }
}

internal readonly record struct GameKey(string DeviceId, string SceneId, int ControlIndex)
{
    public static GameKey From(PluginActionContext context) => new(context.Device.DeviceId, context.SceneId, context.InputEvent.ControlIndex);
    public static GameKey From(PluginVisualContext context) => new(context.DeviceId, context.SceneId, context.ControlIndex);
}
