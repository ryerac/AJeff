using JeffDock.Core.Deck;
using JeffDock.PluginContracts;

namespace JeffDock.Plugins.Image;

public sealed class ImagePlugin : IJeffDockPlugin
{
    public string Id => "jeffdock.image";
    public string DisplayName => "Image";
    public Version Version => new(1, 0, 0);

    public void Register(IJeffDockPluginRegistry registry)
    {
        registry.AddAction(new ImageAction());
        registry.AddPresetJson("""
            {
              "version": 1,
              "sections": [
                {
                  "id": "image",
                  "name": "Image",
                  "presets": [
                    {
                      "id": "image.show",
                      "name": "Image",
                      "description": "Show a still image. Choose Library or Upload Icon after applying. Pressing does nothing. GIF imports are static, not animated.",
                      "controlTypes": [ "Button" ],
                      "requiresDisplay": true,
                      "bindings": [ { "trigger": "Press", "actionId": "jeffdock.image.show" } ],
                      "iconMode": "Static"
                    }
                  ]
                }
              ]
            }
            """);
    }

    private sealed class ImageAction : IPluginDeckAction
    {
        public string Id => "jeffdock.image.show";
        public string DisplayName => "Show Image";
        public PluginActionGroup Group { get; } = new("image", "Image");
        public bool Supports(DeckInputEventType triggerEventType) => triggerEventType == DeckInputEventType.ButtonPress;

        // Artwork is stored and rendered by the host's static icon pipeline.
        public void Execute(PluginActionContext context) { }
    }
}
