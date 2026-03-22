namespace VideoForge.Core.Models;

public class AudioTrack
{
    public int Order { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public double FadeInSeconds { get; set; } = 2.0;
    public double FadeOutSeconds { get; set; } = 3.0;
    public double Volume { get; set; } = 1.0;
}
