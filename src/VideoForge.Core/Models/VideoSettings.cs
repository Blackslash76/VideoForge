using VideoForge.Core.Enums;

namespace VideoForge.Core.Models;

public class VideoSettings
{
    public VideoResolution Resolution { get; set; } = VideoResolution.FullHD_1080p;
    public int Fps { get; set; } = 30;
    public int BitrateKbps { get; set; } = 8000;
    public bool UseHardwareAcceleration { get; set; } = true;
    public TransitionType DefaultTransition { get; set; } = TransitionType.Fade;
    public double DefaultDisplayDuration { get; set; } = 3.0;
    public double DefaultTransitionDuration { get; set; } = 1.0;
}
