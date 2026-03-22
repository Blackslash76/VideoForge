using VideoForge.Core.Enums;

namespace VideoForge.Api.DTOs;

public class CreateVideoRequest
{
    public string? Name { get; set; }
    public VideoResolution Resolution { get; set; } = VideoResolution.FullHD_1080p;
    public int Fps { get; set; } = 30;
    public int BitrateKbps { get; set; } = 8000;
    public bool UseHardwareAcceleration { get; set; } = true;
    public TransitionType DefaultTransition { get; set; } = TransitionType.Fade;
    public double DisplayDuration { get; set; } = 3.0;
    public double TransitionDuration { get; set; } = 1.0;
}
