using VideoForge.Core.Enums;

namespace VideoForge.Core.Models;

public class VideoProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; } = ProjectStatus.Created;
    public List<ImageEntry> Images { get; set; } = new();
    public VideoSettings Settings { get; set; } = new();
    public double Progress { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public long? FileSizeBytes { get; set; }
}
