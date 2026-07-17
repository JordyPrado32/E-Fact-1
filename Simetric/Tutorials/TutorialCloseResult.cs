namespace Simetric.Tutorials;

public sealed class TutorialCloseResult
{
    public required string TutorialId { get; init; }
    public bool Completed { get; init; }
    public bool AutoShowDisabled { get; init; }
    public int? LastStepIndex { get; init; }
}
