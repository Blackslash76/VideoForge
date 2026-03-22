using VideoForge.Core.Enums;

namespace VideoForge.Core.Models;

public class ImageEntry
{
    public int Order { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double DisplayDuration { get; set; } = 3.0;
    public TransitionType Transition { get; set; } = TransitionType.Fade;
    public double TransitionDuration { get; set; } = 1.0;
}
