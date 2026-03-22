using VideoForge.Core.Models;

namespace VideoForge.Api.DTOs;

public class MontageResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public double Progress { get; set; }
    public int PhotosAnimated { get; set; }
    public int TotalPhotos { get; set; }
    public int AudioTracks { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? FileSizeFormatted { get; set; }
    public double? TotalDurationSeconds { get; set; }
    public string? IntroText { get; set; }
    public string? OutroText { get; set; }

    public static MontageResponse FromProject(MontageProject p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Style = p.Style.ToString(),
        Status = p.Status.ToString(),
        StatusMessage = p.StatusMessage,
        Progress = Math.Round(p.Progress, 1),
        PhotosAnimated = p.PhotosAnimated,
        TotalPhotos = p.TotalPhotos,
        AudioTracks = p.AudioTracks.Count,
        ErrorMessage = p.ErrorMessage,
        CreatedAt = p.CreatedAt,
        CompletedAt = p.CompletedAt,
        FileSizeBytes = p.FileSizeBytes,
        FileSizeFormatted = FormatSize(p.FileSizeBytes),
        TotalDurationSeconds = p.TotalDurationSeconds,
        IntroText = p.IntroText,
        OutroText = p.OutroText
    };

    private static string? FormatSize(long? bytes)
    {
        if (bytes == null) return null;
        string[] s = { "B", "KB", "MB", "GB" };
        double l = bytes.Value; int o = 0;
        while (l >= 1024 && o < s.Length - 1) { o++; l /= 1024; }
        return $"{l:0.##} {s[o]}";
    }
}
