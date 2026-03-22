using VideoForge.Core.Enums;

namespace VideoForge.Core.Models;

public class AnimationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceImagePath { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }

    public AnimationType AnimationType { get; set; } = AnimationType.KenBurns;
    public AnimationSettings Settings { get; set; } = new();
    public AnimationStatus Status { get; set; } = AnimationStatus.Queued;
    public double Progress { get; set; }
    public string? StatusMessage { get; set; }

    public string? OutputPath { get; set; }
    public string? DepthMapPath { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public long? FileSizeBytes { get; set; }
}
