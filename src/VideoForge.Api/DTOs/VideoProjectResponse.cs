using VideoForge.Core.Enums;
using VideoForge.Core.Models;

namespace VideoForge.Api.DTOs;

public class VideoProjectResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Progress { get; set; }
    public int ImageCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? FileSizeFormatted { get; set; }
    public VideoSettingsResponse? Settings { get; set; }

    public static VideoProjectResponse FromProject(VideoProject project) => new()
    {
        Id = project.Id,
        Name = project.Name,
        Status = project.Status.ToString(),
        Progress = Math.Round(project.Progress, 1),
        ImageCount = project.Images.Count,
        ErrorMessage = project.ErrorMessage,
        CreatedAt = project.CreatedAt,
        CompletedAt = project.CompletedAt,
        FileSizeBytes = project.FileSizeBytes,
        FileSizeFormatted = FormatFileSize(project.FileSizeBytes),
        Settings = new VideoSettingsResponse
        {
            Resolution = project.Settings.Resolution.ToString(),
            Fps = project.Settings.Fps,
            BitrateKbps = project.Settings.BitrateKbps,
            UseHardwareAcceleration = project.Settings.UseHardwareAcceleration
        }
    };

    private static string? FormatFileSize(long? bytes)
    {
        if (bytes == null) return null;
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes.Value;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public class VideoSettingsResponse
{
    public string Resolution { get; set; } = string.Empty;
    public int Fps { get; set; }
    public int BitrateKbps { get; set; }
    public bool UseHardwareAcceleration { get; set; }
}
