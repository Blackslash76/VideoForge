using VideoForge.Core.Models;

namespace VideoForge.Api.DTOs;

public class AnimationJobResponse
{
    public Guid Id { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string AnimationType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public double Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? FileSizeFormatted { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }

    public static AnimationJobResponse FromJob(AnimationJob job) => new()
    {
        Id = job.Id,
        SourceFileName = job.SourceFileName,
        AnimationType = job.AnimationType.ToString(),
        Status = job.Status.ToString(),
        StatusMessage = job.StatusMessage,
        Progress = Math.Round(job.Progress, 1),
        ErrorMessage = job.ErrorMessage,
        CreatedAt = job.CreatedAt,
        CompletedAt = job.CompletedAt,
        FileSizeBytes = job.FileSizeBytes,
        FileSizeFormatted = FormatFileSize(job.FileSizeBytes),
        ImageWidth = job.ImageWidth,
        ImageHeight = job.ImageHeight
    };

    private static string? FormatFileSize(long? bytes)
    {
        if (bytes == null) return null;
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes.Value;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }
}
