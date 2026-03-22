using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services;

public class FfmpegService : IFfmpegService
{
    private readonly string _ffmpegPath;

    public FfmpegService(string? ffmpegPath = null)
    {
        _ffmpegPath = ffmpegPath ?? "ffmpeg";
    }

    public async Task<string> GenerateVideoAsync(VideoProject project, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        var outputDir = Path.GetDirectoryName(project.Images.First().FilePath)!;
        var outputPath = Path.Combine(outputDir, $"{project.Id}.mp4");
        var (width, height) = GetResolutionDimensions(project.Settings.Resolution);

        var args = BuildFfmpegArguments(project, outputPath, width, height);

        var totalDuration = CalculateTotalDuration(project.Images);

        await RunFfmpegAsync(args, totalDuration, progress, cancellationToken);

        return outputPath;
    }

    public async Task<GpuInfo> GetSystemInfoAsync()
    {
        var info = new GpuInfo();

        // FFmpeg version
        try
        {
            var versionOutput = await RunCommandAsync(_ffmpegPath, "-version");
            var firstLine = versionOutput.Split('\n').FirstOrDefault() ?? "";
            info.FfmpegVersion = firstLine;
        }
        catch
        {
            info.FfmpegVersion = "FFmpeg non trovato";
        }

        // Encoders disponibili
        try
        {
            var encodersOutput = await RunCommandAsync(_ffmpegPath, "-encoders -hide_banner");
            var nvencEncoders = encodersOutput
                .Split('\n')
                .Where(l => l.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Trim())
                .ToList();

            info.SupportedEncoders = nvencEncoders;
            info.NvencAvailable = nvencEncoders.Any(e => e.Contains("h264_nvenc"));
        }
        catch
        {
            info.NvencAvailable = false;
        }

        // GPU info via nvidia-smi
        try
        {
            var gpuOutput = await RunCommandAsync("nvidia-smi",
                "--query-gpu=name,driver_version,memory.total,memory.free --format=csv,noheader,nounits");
            var parts = gpuOutput.Trim().Split(',');
            if (parts.Length >= 4)
            {
                info.Name = parts[0].Trim();
                info.Driver = parts[1].Trim();
                if (long.TryParse(parts[2].Trim(), out var totalMb))
                    info.MemoryTotalBytes = totalMb * 1024 * 1024;
                if (long.TryParse(parts[3].Trim(), out var freeMb))
                    info.MemoryFreeBytes = freeMb * 1024 * 1024;
            }
        }
        catch
        {
            info.Name = "GPU non rilevata";
        }

        return info;
    }

    public string BuildFilterGraph(List<ImageEntry> images, VideoSettings settings)
    {
        var (width, height) = GetResolutionDimensions(settings.Resolution);
        var sb = new StringBuilder();
        var fps = settings.Fps;

        // Scale and pad each input
        for (int i = 0; i < images.Count; i++)
        {
            sb.Append($"[{i}:v]scale={width}:{height}:force_original_aspect_ratio=decrease,");
            sb.Append($"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black,");
            sb.Append($"setsar=1,fps={fps},format=yuv420p[img{i}];");
        }

        if (images.Count == 1)
        {
            var dur = images[0].DisplayDuration;
            sb.Append($"[img0]trim=duration={dur:F2},setpts=PTS-STARTPTS[out]");
            return sb.ToString();
        }

        // Create segments with transitions
        string lastOutput = "img0";
        double currentOffset = 0;

        for (int i = 0; i < images.Count - 1; i++)
        {
            var img = images[i];
            var nextImg = images[i + 1];
            var transDur = nextImg.TransitionDuration;
            var transition = nextImg.Transition;
            var segOut = i < images.Count - 2 ? $"seg{i}" : "out";

            currentOffset += img.DisplayDuration - transDur;

            sb.Append(BuildTransitionFilter(lastOutput, $"img{i + 1}", segOut, transition, transDur, currentOffset));

            lastOutput = segOut;
        }

        return sb.ToString();
    }

    private string BuildFfmpegArguments(VideoProject project, string outputPath, int width, int height)
    {
        var sb = new StringBuilder();
        var settings = project.Settings;

        // Hardware acceleration input
        if (settings.UseHardwareAcceleration)
            sb.Append("-hwaccel cuda -hwaccel_output_format cuda ");

        // Input files
        foreach (var img in project.Images.OrderBy(i => i.Order))
        {
            sb.Append($"-loop 1 -t {img.DisplayDuration + 2:F2} -i \"{img.FilePath}\" ");
        }

        // Filter graph
        var filterGraph = BuildFilterGraph(project.Images.OrderBy(i => i.Order).ToList(), settings);
        sb.Append($"-filter_complex \"{filterGraph}\" ");
        sb.Append("-map \"[out]\" ");

        // Encoder settings
        var encoder = settings.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
        sb.Append($"-c:v {encoder} ");
        sb.Append($"-b:v {settings.BitrateKbps}k ");
        sb.Append($"-maxrate {settings.BitrateKbps * 1.5:F0}k ");
        sb.Append($"-bufsize {settings.BitrateKbps * 2}k ");

        if (settings.UseHardwareAcceleration)
        {
            sb.Append("-preset p4 -tune hq -rc vbr ");
        }
        else
        {
            sb.Append("-preset medium -crf 18 ");
        }

        sb.Append($"-r {settings.Fps} ");
        sb.Append("-pix_fmt yuv420p ");
        sb.Append("-movflags +faststart ");
        sb.Append($"-y \"{outputPath}\"");

        return sb.ToString();
    }

    private string BuildTransitionFilter(string input1, string input2, string output,
        TransitionType transition, double duration, double offset)
    {
        return transition switch
        {
            TransitionType.Fade =>
                $"[{input1}][{input2}]xfade=transition=fade:duration={duration:F2}:offset={offset:F2}[{output}];",
            TransitionType.CrossFade =>
                $"[{input1}][{input2}]xfade=transition=fadeblack:duration={duration:F2}:offset={offset:F2}[{output}];",
            TransitionType.Slide =>
                $"[{input1}][{input2}]xfade=transition=slideleft:duration={duration:F2}:offset={offset:F2}[{output}];",
            TransitionType.Zoom =>
                $"[{input1}][{input2}]xfade=transition=squeezeh:duration={duration:F2}:offset={offset:F2}[{output}];",
            TransitionType.Dissolve =>
                $"[{input1}][{input2}]xfade=transition=dissolve:duration={duration:F2}:offset={offset:F2}[{output}];",
            _ =>
                $"[{input1}][{input2}]xfade=transition=fade:duration={duration:F2}:offset={offset:F2}[{output}];",
        };
    }

    private async Task RunFfmpegAsync(string arguments, double totalDuration,
        IProgress<double> progress, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossibile avviare FFmpeg");

        var timeRegex = new Regex(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");

        while (!process.StandardError.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (line == null) continue;

            var match = timeRegex.Match(line);
            if (match.Success && totalDuration > 0)
            {
                var hours = int.Parse(match.Groups[1].Value);
                var minutes = int.Parse(match.Groups[2].Value);
                var seconds = int.Parse(match.Groups[3].Value);
                var centiseconds = int.Parse(match.Groups[4].Value);

                var currentTime = hours * 3600 + minutes * 60 + seconds + centiseconds / 100.0;
                var pct = Math.Min(currentTime / totalDuration * 100, 99);
                progress.Report(pct);
            }
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"FFmpeg terminato con errore (exit code {process.ExitCode}): {error}");
        }

        progress.Report(100);
    }

    private double CalculateTotalDuration(List<ImageEntry> images)
    {
        if (images.Count == 0) return 0;
        if (images.Count == 1) return images[0].DisplayDuration;

        double total = images.Sum(i => i.DisplayDuration);
        double transitions = images.Skip(1).Sum(i => i.TransitionDuration);
        return total - transitions;
    }

    private static (int width, int height) GetResolutionDimensions(VideoResolution resolution) => resolution switch
    {
        VideoResolution.HD_720p => (1280, 720),
        VideoResolution.FullHD_1080p => (1920, 1080),
        VideoResolution.QHD_1440p => (2560, 1440),
        VideoResolution.UHD_4K => (3840, 2160),
        _ => (1920, 1080)
    };

    private static async Task<string> RunCommandAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Impossibile avviare {fileName}");

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }
}
