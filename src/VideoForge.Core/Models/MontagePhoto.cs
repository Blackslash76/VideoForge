using VideoForge.Core.Enums;

namespace VideoForge.Core.Models;

public class MontagePhoto
{
    public int Order { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }

    // Animazione per questa foto
    public AnimationType AnimationType { get; set; } = AnimationType.KenBurns;
    public double DisplayDuration { get; set; } = 5.0;
    public TransitionType TransitionToNext { get; set; } = TransitionType.CrossFade;
    public double TransitionDuration { get; set; } = 1.5;

    // Ken Burns specifici
    public KenBurnsDirection KenBurnsDirection { get; set; } = KenBurnsDirection.Random;
    public double KenBurnsZoomFactor { get; set; } = 1.25;

    // Parallax
    public double ParallaxIntensity { get; set; } = 0.5;

    // Didascalia per questa foto
    public PhotoCaption? Caption { get; set; }

    // Output
    public string? AnimatedClipPath { get; set; }
    public bool IsAnimated { get; set; }
}
