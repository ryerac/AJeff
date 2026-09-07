using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using JeffDock.Core.Deck;

namespace JeffDock.App.Simulation;

internal sealed class SimulatedDeckCatalog
{
    private const string ResourceSuffix = "Assets.Simulation.simulated-devices.json";
    private readonly IReadOnlyList<SimulatedDeckDefinition> _definitions;

    public SimulatedDeckCatalog()
    {
        var assembly = typeof(SimulatedDeckCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The simulated device catalogue could not be opened.");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var document = JsonSerializer.Deserialize<SimulatedDeckCatalogueDocument>(stream, options)
            ?? throw new InvalidOperationException("The simulated device catalogue is empty.");
        Validate(document.Devices);
        _definitions = document.Devices;
    }

    public IReadOnlyList<SimulatedDeckDefinition> Definitions => _definitions;

    public MonitoredDeckDevice Create(string definitionId)
    {
        var definition = _definitions.FirstOrDefault(item =>
            string.Equals(item.Id, definitionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown simulated device '{definitionId}'.", nameof(definitionId));
        return new MonitoredDeckDevice(
            $"simulation:{definition.Id}", $"{definition.DisplayName} (simulated)",
            "UI simulation - no USB access", null,
            new DeckLayoutDefinition(definition.Width, definition.Height, definition.Controls,
                definition.ButtonImageRotationDegreesClockwise, definition.ButtonImageOutputWidth,
                definition.ButtonImageOutputHeight), IsSimulated: true);
    }

    private static void Validate(IReadOnlyList<SimulatedDeckDefinition> definitions)
    {
        if (definitions.Count == 0) throw new InvalidDataException("The simulated device catalogue contains no devices.");
        if (definitions.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.DisplayName)))
            throw new InvalidDataException("Every simulated device needs an id and display name.");
        if (definitions.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("Simulated device ids must be unique.");
        if (definitions.Any(item => item.Width <= 0 || item.Height <= 0 || item.Controls.Count == 0))
            throw new InvalidDataException("Every simulated device needs positive dimensions and at least one control.");
    }
}

internal sealed record SimulatedDeckCatalogueDocument(IReadOnlyList<SimulatedDeckDefinition> Devices);
internal sealed record SimulatedDeckDefinition(
    string Id, string DisplayName, double Width, double Height,
    IReadOnlyList<DeckControlLayout> Controls,
    int ButtonImageRotationDegreesClockwise = 0,
    int ButtonImageOutputWidth = 60,
    int ButtonImageOutputHeight = 60);
