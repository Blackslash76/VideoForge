namespace VideoForge.Api.DTOs;

public class SystemInfoResponse
{
    public string GpuName { get; set; } = string.Empty;
    public string Driver { get; set; } = string.Empty;
    public bool NvencAvailable { get; set; }
    public string MemoryTotal { get; set; } = string.Empty;
    public string MemoryFree { get; set; } = string.Empty;
    public string FfmpegVersion { get; set; } = string.Empty;
    public List<string> SupportedEncoders { get; set; } = new();
}
