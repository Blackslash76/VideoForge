using VideoForge.Core.Enums;

namespace VideoForge.Api.DTOs;

public class AnimateRequest
{
    public AnimationType AnimationType { get; set; } = AnimationType.KenBurns;
    public double DurationSeconds { get; set; } = 5.0;
    public int Fps { get; set; } = 30;
    public int OutputWidth { get; set; } = 1920;
    public int OutputHeight { get; set; } = 1080;
    public int BitrateKbps { get; set; } = 8000;
    public bool UseHardwareAcceleration { get; set; } = true;
    public bool Loop { get; set; } = false;

    // Ken Burns
    public KenBurnsDirection KenBurnsDirection { get; set; } = KenBurnsDirection.ZoomIn;
    public double KenBurnsZoomFactor { get; set; } = 1.3;

    // Parallax
    public double ParallaxIntensity { get; set; } = 0.5;
    public string ParallaxMotion { get; set; } = "horizontal";

    // Cinemagraph
    public double CinemagraphMotionIntensity { get; set; } = 0.5;
    public string CinemagraphMotionType { get; set; } = "flow";

    // AI Motion
    public double AiMotionStrength { get; set; } = 0.7;
    public int AiMotionSeed { get; set; } = -1;
    public string? AiMotionPrompt { get; set; }
}
