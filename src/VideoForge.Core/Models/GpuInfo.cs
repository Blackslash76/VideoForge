namespace VideoForge.Core.Models;

public class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public string Driver { get; set; } = string.Empty;
    public bool NvencAvailable { get; set; }
    public long MemoryTotalBytes { get; set; }
    public long MemoryFreeBytes { get; set; }
    public string FfmpegVersion { get; set; } = string.Empty;
    public List<string> SupportedEncoders { get; set; } = new();
}
