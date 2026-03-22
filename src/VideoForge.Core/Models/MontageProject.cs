using VideoForge.Core.Enums;

namespace VideoForge.Core.Models;

public class MontageProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public MontageStyle Style { get; set; } = MontageStyle.Cinematic;
    public MontageStatus Status { get; set; } = MontageStatus.Created;

    // Contenuti
    public List<MontagePhoto> Photos { get; set; } = new();
    public List<AudioTrack> AudioTracks { get; set; } = new();

    // Impostazioni video
    public int OutputWidth { get; set; } = 1920;
    public int OutputHeight { get; set; } = 1080;
    public int Fps { get; set; } = 30;
    public int BitrateKbps { get; set; } = 12000;
    public bool UseHardwareAcceleration { get; set; } = true;

    // Intro/Outro
    public string? IntroText { get; set; }
    public string? OutroText { get; set; }
    public string? IntroFontColor { get; set; } = "#FFFFFF";
    public double IntroDuration { get; set; } = 4.0;
    public double OutroDuration { get; set; } = 5.0;

    // Progresso
    public double Progress { get; set; }
    public string? StatusMessage { get; set; }
    public int PhotosAnimated { get; set; }
    public int TotalPhotos { get; set; }

    // Output
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public long? FileSizeBytes { get; set; }
    public double? TotalDurationSeconds { get; set; }
}
