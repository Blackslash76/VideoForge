namespace VideoForge.Core.Models;

public class PhotoCaption
{
    public string Text { get; set; } = string.Empty;
    public string Position { get; set; } = "bottom"; // top, center, bottom
    public double FontSize { get; set; } = 42;
    public string FontColor { get; set; } = "#FFFFFF";
    public bool HasShadow { get; set; } = true;
    public double FadeInDuration { get; set; } = 0.8;
    public double FadeOutDuration { get; set; } = 0.8;
    public double DelaySeconds { get; set; } = 0.5;
}
