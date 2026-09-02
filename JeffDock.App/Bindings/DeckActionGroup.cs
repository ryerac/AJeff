namespace JeffDock.App.Bindings;

internal sealed record DeckActionGroup(string Id, string DisplayName);

internal static class DeckActionGroups
{
    public static DeckActionGroup None { get; } = new("none", "None");

    public static DeckActionGroup Audio { get; } = new("core.audio", "Audio");

    public static DeckActionGroup Scenes { get; } = new("scenes", "Scenes");
}
