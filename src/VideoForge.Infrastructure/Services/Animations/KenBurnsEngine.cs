using System.Diagnostics;
using System.Globalization;
using System.Text;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services.Animations;

public class KenBurnsEngine : IAnimationEngine
{
    public AnimationType SupportedType => AnimationType.KenBurns;

    public bool IsAvailable() => CanRunCommand("ffmpeg", "-version");

    public async Task<string> AnimateAsync(AnimationJob job, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        var outputPath = Path.Combine(Path.GetDirectoryName(job.SourceImagePath)!, $"{job.Id}_kenburns.mp4");
        var s = job.Settings;
        var totalFrames = (int)(s.DurationSeconds * s.Fps);
        var zoom = s.KenBurnsZoomFactor;

        var (zoomExpr, xExpr, yExpr) = BuildZoomPanExpressions(s.KenBurnsDirection, zoom, totalFrames, job.ImageWidth, job.ImageHeight, s.OutputWidth, s.OutputHeight);

        var encoder = s.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
        var presetArgs = s.UseHardwareAcceleration ? "-preset p4 -tune hq -rc vbr" : "-preset medium -crf 18";

        var args = $"-loop 1 -i \"{job.SourceImagePath}\" " +
                   $"-vf \"zoompan=z='{zoomExpr}':x='{xExpr}':y='{yExpr}':d={totalFrames}:s={s.OutputWidth}x{s.OutputHeight}:fps={s.Fps},format=yuv420p\" " +
                   $"-t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} " +
                   $"-c:v {encoder} -b:v {s.BitrateKbps}k {presetArgs} " +
                   $"-movflags +faststart -y \"{outputPath}\"";

        await RunFfmpegWithProgressAsync(args, s.DurationSeconds, progress, cancellationToken);
        return outputPath;
    }

    private static (string zoom, string x, string y) BuildZoomPanExpressions(
        KenBurnsDirection direction, double zoomFactor, int totalFrames, int imgW, int imgH, int outW, int outH)
    {
        var zoomStep = (zoomFactor - 1.0) / totalFrames;
        var zf = zoomStep.ToString("F6", CultureInfo.InvariantCulture);

        return direction switch
        {
            KenBurnsDirection.ZoomIn =>
                ($"min(zoom+{zf},  {zoomFactor.ToString(CultureInfo.InvariantCulture)})",
                 "iw/2-(iw/zoom/2)",
                 "ih/2-(ih/zoom/2)"),

            KenBurnsDirection.ZoomOut =>
                ($"if(eq(on,1),{zoomFactor.ToString(CultureInfo.InvariantCulture)},max(zoom-{zf},1))",
                 "iw/2-(iw/zoom/2)",
                 "ih/2-(ih/zoom/2)"),

            KenBurnsDirection.PanLeft =>
                ("1.1",
                 $"iw-iw/zoom-on/({totalFrames}/(iw-iw/zoom))",
                 "ih/2-(ih/zoom/2)"),

            KenBurnsDirection.PanRight =>
                ("1.1",
                 $"on/({totalFrames}/(iw-iw/zoom))",
                 "ih/2-(ih/zoom/2)"),

            KenBurnsDirection.PanUp =>
                ("1.1",
                 "iw/2-(iw/zoom/2)",
                 $"ih-ih/zoom-on/({totalFrames}/(ih-ih/zoom))"),

            KenBurnsDirection.PanDown =>
                ("1.1",
                 "iw/2-(iw/zoom/2)",
                 $"on/({totalFrames}/(ih-ih/zoom))"),

            KenBurnsDirection.PanLeftZoomIn =>
                ($"min(zoom+{zf},{zoomFactor.ToString(CultureInfo.InvariantCulture)})",
                 "iw/2-(iw/zoom/2)-on*2",
                 "ih/2-(ih/zoom/2)"),

            KenBurnsDirection.PanRightZoomIn =>
                ($"min(zoom+{zf},{zoomFactor.ToString(CultureInfo.InvariantCulture)})",
                 "iw/2-(iw/zoom/2)+on*2",
                 "ih/2-(ih/zoom/2)"),

            _ => // Random - default to zoom in
                ($"min(zoom+{zf},{zoomFactor.ToString(CultureInfo.InvariantCulture)})",
                 "iw/2-(iw/zoom/2)",
                 "ih/2-(ih/zoom/2)")
        };
    }

    private static async Task RunFfmpegWithProgressAsync(string arguments, double totalDuration,
        IProgress<double> progress, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossibile avviare FFmpeg");

        var timeRegex = new System.Text.RegularExpressions.Regex(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");

        while (!process.StandardError.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (line == null) continue;

            var match = timeRegex.Match(line);
            if (match.Success && totalDuration > 0)
            {
                var currentTime = int.Parse(match.Groups[1].Value) * 3600 +
                                  int.Parse(match.Groups[2].Value) * 60 +
                                  int.Parse(match.Groups[3].Value) +
                                  int.Parse(match.Groups[4].Value) / 100.0;
                progress.Report(Math.Min(currentTime / totalDuration * 100, 99));
            }
        }

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg Ken Burns fallito (exit code {process.ExitCode})");

        progress.Report(100);
    }

    private static bool CanRunCommand(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = command, Arguments = args, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }
}
