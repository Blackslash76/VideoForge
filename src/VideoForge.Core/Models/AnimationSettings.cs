using VideoForge.Core.Enums;

namespace VideoForge.Core.Models;

public class AnimationSettings
{
    // Generali
    public double DurationSeconds { get; set; } = 5.0;
    public int Fps { get; set; } = 30;
    public int OutputWidth { get; set; } = 1920;
    public int OutputHeight { get; set; } = 1080;
    public int BitrateKbps { get; set; } = 8000;
    public bool Loop { get; set; } = false;
    public bool UseHardwareAcceleration { get; set; } = true;

    // Ken Burns
    public KenBurnsDirection KenBurnsDirection { get; set; } = KenBurnsDirection.ZoomIn;
    public double KenBurnsZoomFactor { get; set; } = 1.3;
    public double KenBurnsSpeed { get; set; } = 1.0;

    // Parallax
    public double ParallaxIntensity { get; set; } = 0.5;
    public double ParallaxDepthLayers { get; set; } = 3;
    public string ParallaxMotion { get; set; } = "horizontal"; // horizontal, vertical, circular

    // Cinemagraph
    public string? CinemagraphMaskPath { get; set; }
    public double CinemagraphMotionIntensity { get; set; } = 0.5;
    public string CinemagraphMotionType { get; set; } = "flow"; // flow, wave, ripple

    // AI Motion
    public double AiMotionStrength { get; set; } = 0.7;
    public int AiMotionSeed { get; set; } = -1; // -1 = random
    public string? AiMotionPrompt { get; set; }
}
