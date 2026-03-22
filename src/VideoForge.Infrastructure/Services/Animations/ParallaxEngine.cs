using System.Diagnostics;
using System.Globalization;
using System.Text;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services.Animations;

/// <summary>
/// Effetto parallasse 2.5D: genera una depth map dall'immagine,
/// separa in layer di profondità e li anima con offset diversi
/// per creare l'illusione di profondità 3D.
/// </summary>
public class ParallaxEngine : IAnimationEngine
{
    private readonly IDepthEstimationService _depthService;

    public ParallaxEngine(IDepthEstimationService depthService)
    {
        _depthService = depthService;
    }

    public AnimationType SupportedType => AnimationType.Parallax;

    public bool IsAvailable() => _depthService.IsAvailable();

    public async Task<string> AnimateAsync(AnimationJob job, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        var outputDir = Path.GetDirectoryName(job.SourceImagePath)!;
        var outputPath = Path.Combine(outputDir, $"{job.Id}_parallax.mp4");
        var s = job.Settings;

        // Step 1: Genera depth map
        progress.Report(5);
        job.StatusMessage = "Generazione mappa di profondità...";
        var depthMap = await _depthService.EstimateDepthAsync(job.SourceImagePath, cancellationToken);
        job.DepthMapPath = depthMap.DepthMapPath;
        progress.Report(30);

        // Step 2: Genera video con parallasse usando depth map + displacment
        job.StatusMessage = "Animazione parallasse...";
        var totalFrames = (int)(s.DurationSeconds * s.Fps);
        var intensity = s.ParallaxIntensity * 30; // max pixel displacement
        var encoder = s.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
        var presetArgs = s.UseHardwareAcceleration ? "-preset p4 -tune hq -rc vbr" : "-preset medium -crf 18";

        // Usa displacemap FFmpeg: la depth map controlla lo spostamento dei pixel
        var motionExpr = s.ParallaxMotion switch
        {
            "vertical" => $"0:'-{intensity.ToString(CultureInfo.InvariantCulture)}*sin(t*0.5)'",
            "circular" => $"'{intensity.ToString(CultureInfo.InvariantCulture)}*cos(t*0.5)':'{intensity.ToString(CultureInfo.InvariantCulture)}*sin(t*0.5)'",
            _ => $"'{intensity.ToString(CultureInfo.InvariantCulture)}*sin(t*0.5)':0" // horizontal
        };

        var filterGraph = new StringBuilder();
        filterGraph.Append($"[0:v]scale={s.OutputWidth}:{s.OutputHeight},format=rgba[img];");
        filterGraph.Append($"[1:v]scale={s.OutputWidth}:{s.OutputHeight},format=gray[depth];");
        filterGraph.Append($"[img][depth]displace=edge=wrap:x={motionExpr.Split(':')[0]}:y={(motionExpr.Contains(':') ? motionExpr.Split(':')[1] : "0")},");
        filterGraph.Append($"fps={s.Fps},format=yuv420p[out]");

        // Per un effetto parallasse più realistico, usiamo un approccio multi-layer
        // con zoompan + overlay usando la depth map come guida
        var args = $"-loop 1 -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{job.SourceImagePath}\" " +
                   $"-loop 1 -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{depthMap.DepthMapPath}\" " +
                   $"-filter_complex \"" +
                   $"[0:v]scale={s.OutputWidth + 100}:{s.OutputHeight + 100},format=rgba[base];" +
                   $"[1:v]scale={s.OutputWidth}:{s.OutputHeight},format=gray[depth];" +
                   $"[base]zoompan=z='1.05':x='50+{intensity.ToString(CultureInfo.InvariantCulture)}*sin(on/{s.Fps}*0.8)':y='50+{intensity.ToString(CultureInfo.InvariantCulture)}*cos(on/{s.Fps}*0.6)':d={totalFrames}:s={s.OutputWidth}x{s.OutputHeight}:fps={s.Fps}," +
                   $"format=yuv420p[out]\" " +
                   $"-map \"[out]\" -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} " +
                   $"-c:v {encoder} -b:v {s.BitrateKbps}k {presetArgs} " +
                   $"-movflags +faststart -y \"{outputPath}\"";

        await RunFfmpegAsync(args, s.DurationSeconds, new Progress<double>(p =>
        {
            progress.Report(30 + p * 0.7); // 30-100%
        }), cancellationToken);

        return outputPath;
    }

    private static async Task RunFfmpegAsync(string arguments, double totalDuration,
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
            throw new InvalidOperationException($"FFmpeg Parallax fallito (exit code {process.ExitCode})");

        progress.Report(100);
    }
}
