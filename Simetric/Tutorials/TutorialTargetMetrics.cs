namespace Simetric.Tutorials;

public sealed class TutorialTargetMetrics
{
    public double Top { get; set; }
    public double Left { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }
    public string Shape { get; set; } = "rounded";
}
