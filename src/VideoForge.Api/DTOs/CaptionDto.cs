using System.Text.Json.Serialization;

namespace VideoForge.Api.DTOs;

public class CaptionDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public string? Position { get; set; } = "bottom";

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 42;
}
