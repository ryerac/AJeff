namespace JeffDock.App.Bindings;

internal sealed record DeckActionGroup(string Id, string DisplayName);

internal static class DeckActionGroups
{
    public static DeckActionGroup None { get; } = new("none", "None");

    public static DeckActionGroup Audio { get; } = new("core.audio", "Audio");

    public static DeckActionGroup Keyboard { get; } = new("core.keyboard", "Keyboard");

    public static DeckActionGroup Machine { get; } = new("core.machine", "Machine");

    public static DeckActionGroup Media { get; } = new("core.media", "Media");

    public static DeckActionGroup Scenes { get; } = new("scenes", "Scenes");
}
