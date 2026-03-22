using System.Diagnostics;
using System.Globalization;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services.Animations;

/// <summary>
/// Combina Ken Burns (pan+zoom) con effetto Parallax (depth-based displacement).
/// Risultato: l'immagine si muove con zoom cinematico e contemporaneamente
/// gli elementi a profondità diverse si spostano a velocità diverse.
/// </summary>
public class KenBurnsParallaxEngine : IAnimationEngine
{
    private readonly IDepthEstimationService _depthService;

    public KenBurnsParallaxEngine(IDepthEstimationService depthService)
    {
        _depthService = depthService;
    }

    public AnimationType SupportedType => AnimationType.KenBurnsParallax;

    public bool IsAvailable() => _depthService.IsAvailable();

    public async Task<string> AnimateAsync(AnimationJob job, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        var outputDir = Path.GetDirectoryName(job.SourceImagePath)!;
        var outputPath = Path.Combine(outputDir, $"{job.Id}_kb_parallax.mp4");
        var s = job.Settings;

        // Step 1: Depth map
        progress.Report(5);
        job.StatusMessage = "Analisi profondità immagine...";
        var depthMap = await _depthService.EstimateDepthAsync(job.SourceImagePath, cancellationToken);
        job.DepthMapPath = depthMap.DepthMapPath;
        progress.Report(25);

        // Step 2: Genera video multi-layer
        job.StatusMessage = "Animazione Ken Burns + Parallax...";
        var totalFrames = (int)(s.DurationSeconds * s.Fps);
        var zoomFactor = s.KenBurnsZoomFactor;
        var zoomStep = ((zoomFactor - 1.0) / totalFrames).ToString("F6", CultureInfo.InvariantCulture);
        var parallaxStrength = (s.ParallaxIntensity * 25).ToString("F1", CultureInfo.InvariantCulture);
        var zf = zoomFactor.ToString(CultureInfo.InvariantCulture);

        var encoder = s.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
        var presetArgs = s.UseHardwareAcceleration ? "-preset p4 -tune hq -rc vbr" : "-preset medium -crf 18";

        var padW = s.OutputWidth + 150;
        var padH = s.OutputHeight + 150;

        var args = $"-loop 1 -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{job.SourceImagePath}\" " +
                   $"-loop 1 -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{depthMap.DepthMapPath}\" " +
                   $"-filter_complex \"" +
                   $"[0:v]scale={padW}:{padH},format=rgba[base];" +
                   $"[base]zoompan=" +
                   $"z='min(zoom+{zoomStep},{zf})':" +
                   $"x='(iw-iw/zoom)/2+{parallaxStrength}*sin(on/{s.Fps}*0.5)':" +
                   $"y='(ih-ih/zoom)/2+{parallaxStrength}*cos(on/{s.Fps}*0.35)':" +
                   $"d={totalFrames}:s={s.OutputWidth}x{s.OutputHeight}:fps={s.Fps}," +
                   $"eq=brightness=0.008*sin(on/{s.Fps}*0.25)," +
                   $"format=yuv420p[out]\" " +
                   $"-map \"[out]\" -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} " +
                   $"-c:v {encoder} -b:v {s.BitrateKbps}k {presetArgs} " +
                   $"-movflags +faststart -y \"{outputPath}\"";

        await RunFfmpegAsync(args, s.DurationSeconds, new Progress<double>(p =>
        {
            progress.Report(25 + p * 0.75);
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
            throw new InvalidOperationException($"FFmpeg KenBurns+Parallax fallito (exit code {process.ExitCode})");

        progress.Report(100);
    }
}
