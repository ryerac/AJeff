using JeffDock.Core.Deck;
using JeffDock.PluginContracts;

namespace JeffDock.App.Bindings;

internal interface IDeckAction
{
    string Id { get; }

    string DisplayName { get; }

    DeckActionGroup Group { get; }

    DeckActionVisualDefinition? Visual => null;
    IReadOnlyList<PluginSettingDefinition> Parameters => [];

    bool Supports(DeckInputEventType triggerEventType);

    void Execute(DeckActionContext context);
}

internal readonly record struct DeckActionContext(
    MonitoredDeckDevice Device,
    string SceneId,
    DeckInputEvent InputEvent,
    IReadOnlyDictionary<string, string> Parameters
);

internal sealed record DeckActionVisualDefinition(
    string StateSourceId,
    IReadOnlyList<DeckActionVisualState> States,
    bool IsImageManaged = false);

internal sealed record DeckActionVisualState(
    string Id,
    string DisplayName,
    string? DefaultIconId = null);
