namespace Simetric.Tutorials;

public sealed class TutorialDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Route { get; init; }
    public IReadOnlyList<string> RouteAliases { get; init; } = Array.Empty<string>();
    public string? DefaultTargetSelector { get; init; }
    public string Category { get; init; } = "General";
    public bool UseGlobalHost { get; init; } = true;
    public IReadOnlyList<TutorialStep> Steps { get; init; } = Array.Empty<TutorialStep>();
}
