using JeffDock.Core.Deck;

namespace JeffDock.App.Bindings;

internal sealed record ControlIconSnapshot(byte[]? StaticImage, IReadOnlyDictionary<string, byte[]> StateImages);

internal sealed record ControlConfigurationSnapshot(
    DeckControlType ControlType,
    IReadOnlyList<DeckControlBindingUpdate> Bindings,
    DeckIconMode IconMode,
    ControlIconSnapshot Icons)
{
    public bool CanPasteTo(DeckControlLayout target) => ControlType == target.ControlType
        && (target.CanHaveIcon || (IconMode == DeckIconMode.Static
            && Icons.StaticImage is null && Icons.StateImages.Count == 0));
}
