using VideoForge.Core.Models;

namespace VideoForge.Core.Interfaces;

public interface IFfmpegService
{
    Task<string> GenerateVideoAsync(VideoProject project, IProgress<double> progress, CancellationToken cancellationToken = default);
    Task<GpuInfo> GetSystemInfoAsync();
    string BuildFilterGraph(List<ImageEntry> images, VideoSettings settings);
}
