using JeffDock.Core.Deck;

namespace JeffDock.App.Presets;

internal sealed record DeckPresetSection(string Id, string Name, IReadOnlyList<DeckControlPreset> Presets);

internal sealed record DeckControlPreset(
    string Id,
    string Name,
    string? Description,
    IReadOnlyList<string> ControlTypes,
    IReadOnlyList<DeckPresetBinding> Bindings,
    string? IconMode,
    string? IconId = null,
    string? IconForeground = null,
    string? IconBackground = null)
{
    public bool Supports(DeckControlType controlType) =>
        ControlTypes.Any(value => string.Equals(value, controlType.ToString(), StringComparison.OrdinalIgnoreCase));

    public DeckInputEventType? ResolveTrigger(DeckControlType controlType, DeckPresetBinding binding)
    {
        return binding.Trigger.ToLowerInvariant() switch
        {
            "press" when controlType == DeckControlType.Button => DeckInputEventType.ButtonPress,
            "press" when controlType == DeckControlType.Encoder => DeckInputEventType.EncoderPress,
            "turn" when controlType == DeckControlType.Encoder => DeckInputEventType.EncoderTurn,
            _ => null,
        };
    }
}

internal sealed record DeckPresetBinding(
    string Trigger,
    string ActionId,
    IReadOnlyDictionary<string, string>? Parameters = null);
