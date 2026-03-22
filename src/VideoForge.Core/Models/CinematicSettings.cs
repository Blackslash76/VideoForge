using VideoForge.Core.Enums;

namespace VideoForge.Core.Models;

public class CinematicSettings
{
    // Color Grading
    public ColorGrade ColorGrade { get; set; } = ColorGrade.WarmMemory;

    // Vignette - bordi scuri cinematografici
    public bool Vignette { get; set; } = true;
    public double VignetteIntensity { get; set; } = 0.4;

    // Letterbox - bande nere cinema
    public bool Letterbox { get; set; } = false;
    public double LetterboxRatio { get; set; } = 2.35; // 2.35:1 cinemascope

    // Film Grain - grana pellicola sottile
    public bool FilmGrain { get; set; } = true;
    public double FilmGrainIntensity { get; set; } = 0.15;

    // Particle Overlay
    public ParticleEffect ParticleEffect { get; set; } = ParticleEffect.None;
    public double ParticleIntensity { get; set; } = 0.5;

    // Intro cinematico
    public bool CinematicIntro { get; set; } = true;
    public string? IntroTitle { get; set; }
    public string? IntroSubtitle { get; set; }
    public string? IntroDate { get; set; }
    public double IntroDuration { get; set; } = 6.0;
    public double IntroFadeIn { get; set; } = 2.0;

    // Outro / Dedica
    public bool CinematicOutro { get; set; } = true;
    public string? OutroTitle { get; set; }
    public string? OutroMessage { get; set; }
    public string? OutroCredits { get; set; }
    public double OutroDuration { get; set; } = 8.0;

    // Slow motion momentaneo su certe foto
    public bool SlowMotionEffect { get; set; } = false;

    // Flash bianco tra transizioni per effetto "memoria"
    public bool MemoryFlash { get; set; } = true;
    public double MemoryFlashDuration { get; set; } = 0.15;
}
