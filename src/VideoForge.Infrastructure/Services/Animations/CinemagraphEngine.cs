using System.Diagnostics;
using System.Globalization;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services.Animations;

/// <summary>
/// Cinemagraph: parti specifiche dell'immagine si muovono in loop.
/// Usa una maschera (opzionale) per definire le aree in movimento
/// e applica effetti wave/ripple/flow tramite FFmpeg.
/// Senza maschera, applica un'animazione sottile sull'intera immagine.
/// </summary>
public class CinemagraphEngine : IAnimationEngine
{
    public AnimationType SupportedType => AnimationType.Cinemagraph;

    public bool IsAvailable() => CanRunCommand("ffmpeg", "-version");

    public async Task<string> AnimateAsync(AnimationJob job, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        var outputDir = Path.GetDirectoryName(job.SourceImagePath)!;
        var outputPath = Path.Combine(outputDir, $"{job.Id}_cinemagraph.mp4");
        var s = job.Settings;

        var totalFrames = (int)(s.DurationSeconds * s.Fps);
        var encoder = s.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
        var presetArgs = s.UseHardwareAcceleration ? "-preset p4 -tune hq -rc vbr" : "-preset medium -crf 18";
        var intensity = s.CinemagraphMotionIntensity;

        string filterGraph;
        string inputArgs;

        if (!string.IsNullOrEmpty(s.CinemagraphMaskPath) && File.Exists(s.CinemagraphMaskPath))
        {
            // Con maschera: applica movimento solo alle aree bianche della maschera
            inputArgs = $"-loop 1 -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{job.SourceImagePath}\" " +
                        $"-loop 1 -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{s.CinemagraphMaskPath}\"";

            var motionFilter = BuildMotionFilter(s.CinemagraphMotionType, intensity, s.Fps);

            filterGraph = $"[0:v]scale={s.OutputWidth}:{s.OutputHeight},format=rgba[static];" +
                         $"[0:v]scale={s.OutputWidth}:{s.OutputHeight},format=rgba,{motionFilter}[animated];" +
                         $"[1:v]scale={s.OutputWidth}:{s.OutputHeight},format=gray[mask];" +
                         $"[static][animated][mask]maskedmerge[merged];" +
                         $"[merged]fps={s.Fps},format=yuv420p[out]";
        }
        else
        {
            // Senza maschera: applica effetto sottile a tutta l'immagine
            inputArgs = $"-loop 1 -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{job.SourceImagePath}\"";

            var motionFilter = BuildMotionFilter(s.CinemagraphMotionType, intensity * 0.3, s.Fps);

            filterGraph = $"[0:v]scale={s.OutputWidth}:{s.OutputHeight},{motionFilter}," +
                         $"fps={s.Fps},format=yuv420p[out]";
        }

        var args = $"{inputArgs} " +
                   $"-filter_complex \"{filterGraph}\" " +
                   $"-map \"[out]\" " +
                   $"-t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} " +
                   $"-c:v {encoder} -b:v {s.BitrateKbps}k {presetArgs} " +
                   $"-movflags +faststart -y \"{outputPath}\"";

        await RunFfmpegAsync(args, s.DurationSeconds, progress, cancellationToken);

        // Se loop è richiesto, rigenera come GIF o video loopable
        if (s.Loop)
        {
            var loopPath = Path.Combine(outputDir, $"{job.Id}_cinemagraph_loop.mp4");
            var loopArgs = $"-stream_loop 3 -i \"{outputPath}\" -c copy -y \"{loopPath}\"";
            await RunFfmpegAsync(loopArgs, s.DurationSeconds * 3, new Progress<double>(), cancellationToken);
            File.Delete(outputPath);
            return loopPath;
        }

        return outputPath;
    }

    private static string BuildMotionFilter(string motionType, double intensity, int fps)
    {
        var i = intensity.ToString("F2", CultureInfo.InvariantCulture);

        return motionType switch
        {
            "wave" =>
                // Effetto onda sinusoidale - perfetto per acqua, tessuti
                $"geq=lum='lum(X,Y+{i}*10*sin(2*PI*X/200+T*2))':cb='cb(X,Y+{i}*10*sin(2*PI*X/200+T*2))':cr='cr(X,Y+{i}*10*sin(2*PI*X/200+T*2))'",

            "ripple" =>
                // Effetto increspatura circolare - perfetto per riflessi nell'acqua
                $"geq=lum='lum(X+{i}*5*sin(2*PI*(sqrt((X-W/2)*(X-W/2)+(Y-H/2)*(Y-H/2)))/100+T*3),Y)':cb='cb(X+{i}*5*sin(2*PI*(sqrt((X-W/2)*(X-W/2)+(Y-H/2)*(Y-H/2)))/100+T*3),Y)':cr='cr(X+{i}*5*sin(2*PI*(sqrt((X-W/2)*(X-W/2)+(Y-H/2)*(Y-H/2)))/100+T*3),Y)'",

            _ => // "flow" - movimento fluido generico
                $"geq=lum='lum(X+{i}*8*sin(2*PI*Y/300+T*1.5),Y+{i}*3*cos(2*PI*X/400+T))':cb='cb(X+{i}*8*sin(2*PI*Y/300+T*1.5),Y+{i}*3*cos(2*PI*X/400+T))':cr='cr(X+{i}*8*sin(2*PI*Y/300+T*1.5),Y+{i}*3*cos(2*PI*X/400+T))'"
        };
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
            throw new InvalidOperationException($"FFmpeg Cinemagraph fallito (exit code {process.ExitCode})");

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
