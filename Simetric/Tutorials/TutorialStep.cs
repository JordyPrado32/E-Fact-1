namespace Simetric.Tutorials;

public sealed class TutorialStep
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? TargetSelector { get; init; }
    public string? FallbackTargetSelector { get; init; }
    public string Shape { get; init; } = "rounded";
    public int Padding { get; init; } = 16;
    public string? CardPlacement { get; init; }
    public int OffsetX { get; init; }
    public int OffsetY { get; init; }
}
