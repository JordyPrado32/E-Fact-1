namespace Simetric.Tutorials;

public sealed class TutorialProgress
{
    public bool Completed { get; set; }
    public bool AutoShowDisabled { get; set; }
    public int? LastStepIndex { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? AutoShowDisabledAt { get; set; }
}
