using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services.Animations;

/// <summary>
/// AI Motion engine: usa Stable Video Diffusion (SVD) via ComfyUI o API locale
/// per generare video animati da una singola immagine statica.
/// Fallback: se il modello AI non è disponibile, usa un effetto KenBurns+Parallax avanzato.
/// </summary>
public class AiMotionEngine : IAnimationEngine
{
    private readonly IDepthEstimationService _depthService;
    private readonly string _comfyUiUrl;

    public AiMotionEngine(IDepthEstimationService depthService, string comfyUiUrl = "http://localhost:8188")
    {
        _depthService = depthService;
        _comfyUiUrl = comfyUiUrl;
    }

    public AnimationType SupportedType => AnimationType.AiMotion;

    public bool IsAvailable()
    {
        // Check if ComfyUI is running
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = client.GetAsync($"{_comfyUiUrl}/system_stats").Result;
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> AnimateAsync(AnimationJob job, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        var outputDir = Path.GetDirectoryName(job.SourceImagePath)!;
        var outputPath = Path.Combine(outputDir, $"{job.Id}_aimotion.mp4");
        var s = job.Settings;

        if (IsAvailable())
        {
            return await AnimateWithComfyUiAsync(job, outputPath, progress, cancellationToken);
        }

        // Fallback: effetto avanzato con depth map + FFmpeg
        return await AnimateWithDepthFallbackAsync(job, outputPath, progress, cancellationToken);
    }

    private async Task<string> AnimateWithComfyUiAsync(AnimationJob job, string outputPath,
        IProgress<double> progress, CancellationToken cancellationToken)
    {
        var s = job.Settings;
        using var client = new HttpClient();

        // 1. Upload immagine su ComfyUI
        progress.Report(5);
        job.StatusMessage = "Upload immagine su ComfyUI...";
        var imageFileName = await UploadImageToComfyAsync(client, job.SourceImagePath);
        progress.Report(10);

        // 2. Invia workflow SVD
        job.StatusMessage = "Generazione video con Stable Video Diffusion...";
        var workflow = BuildSvdWorkflow(imageFileName, s);
        var promptId = await QueueWorkflowAsync(client, workflow);
        progress.Report(15);

        // 3. Polling fino a completamento
        var outputFilePath = await PollCompletionAsync(client, promptId, progress, cancellationToken);
        progress.Report(90);

        // 4. Scarica e codifica con NVENC
        job.StatusMessage = "Encoding finale GPU...";
        var encoder = s.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
        var presetArgs = s.UseHardwareAcceleration ? "-preset p4 -tune hq -rc vbr" : "-preset medium -crf 18";

        var args = $"-i \"{outputFilePath}\" " +
                   $"-vf \"scale={s.OutputWidth}:{s.OutputHeight},fps={s.Fps},format=yuv420p\" " +
                   $"-c:v {encoder} -b:v {s.BitrateKbps}k {presetArgs} " +
                   $"-movflags +faststart -y \"{outputPath}\"";

        await RunFfmpegAsync(args, s.DurationSeconds, new Progress<double>(p =>
        {
            progress.Report(90 + p * 0.1);
        }), cancellationToken);

        return outputPath;
    }

    private async Task<string> AnimateWithDepthFallbackAsync(AnimationJob job, string outputPath,
        IProgress<double> progress, CancellationToken cancellationToken)
    {
        var s = job.Settings;
        var outputDir = Path.GetDirectoryName(job.SourceImagePath)!;

        // Genera depth map per effetto 3D avanzato
        job.StatusMessage = "Generazione mappa di profondità per animazione 3D...";
        progress.Report(5);

        DepthMap? depthMap = null;
        if (_depthService.IsAvailable())
        {
            depthMap = await _depthService.EstimateDepthAsync(job.SourceImagePath, cancellationToken);
            job.DepthMapPath = depthMap.DepthMapPath;
        }
        progress.Report(30);

        // Effetto combinato: Ken Burns fluido + leggero displacement basato su depth
        job.StatusMessage = "Animazione avanzata con effetto profondità...";
        var totalFrames = (int)(s.DurationSeconds * s.Fps);
        var motionStrength = (s.AiMotionStrength * 20).ToString("F1", CultureInfo.InvariantCulture);
        var encoder = s.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
        var presetArgs = s.UseHardwareAcceleration ? "-preset p4 -tune hq -rc vbr" : "-preset medium -crf 18";

        string args;
        if (depthMap != null)
        {
            // Con depth map: parallasse 2.5D avanzato
            args = $"-loop 1 -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{job.SourceImagePath}\" " +
                   $"-loop 1 -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{depthMap.DepthMapPath}\" " +
                   $"-filter_complex \"" +
                   $"[0:v]scale={s.OutputWidth + 120}:{s.OutputHeight + 120},format=rgba[base];" +
                   $"[base]zoompan=z='1.08+0.02*sin(on/{s.Fps}*0.5)':x='60+{motionStrength}*sin(on/{s.Fps}*0.4)':y='60+{motionStrength}*cos(on/{s.Fps}*0.3)':d={totalFrames}:s={s.OutputWidth}x{s.OutputHeight}:fps={s.Fps}," +
                   $"eq=brightness=0.01*sin(on/{s.Fps}*0.2):saturation=1+0.05*sin(on/{s.Fps}*0.15)," +
                   $"format=yuv420p[out]\" " +
                   $"-map \"[out]\" -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} " +
                   $"-c:v {encoder} -b:v {s.BitrateKbps}k {presetArgs} " +
                   $"-movflags +faststart -y \"{outputPath}\"";
        }
        else
        {
            // Senza depth map: effetto cinematico sofisticato
            args = $"-loop 1 -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{job.SourceImagePath}\" " +
                   $"-filter_complex \"" +
                   $"[0:v]scale={s.OutputWidth + 100}:{s.OutputHeight + 100},format=rgba," +
                   $"zoompan=z='1.05+0.03*sin(on/{s.Fps}*0.6)':x='50+{motionStrength}*sin(on/{s.Fps}*0.35)':y='50+{motionStrength}*cos(on/{s.Fps}*0.25)':d={totalFrames}:s={s.OutputWidth}x{s.OutputHeight}:fps={s.Fps}," +
                   $"format=yuv420p[out]\" " +
                   $"-map \"[out]\" -t {s.DurationSeconds.ToString(CultureInfo.InvariantCulture)} " +
                   $"-c:v {encoder} -b:v {s.BitrateKbps}k {presetArgs} " +
                   $"-movflags +faststart -y \"{outputPath}\"";
        }

        await RunFfmpegAsync(args, s.DurationSeconds, new Progress<double>(p =>
        {
            progress.Report(30 + p * 0.7);
        }), cancellationToken);

        return outputPath;
    }

    private async Task<string> UploadImageToComfyAsync(HttpClient client, string imagePath)
    {
        using var form = new MultipartFormDataContent();
        var imageBytes = await File.ReadAllBytesAsync(imagePath);
        form.Add(new ByteArrayContent(imageBytes), "image", Path.GetFileName(imagePath));

        var response = await client.PostAsync($"{_comfyUiUrl}/upload/image", form);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("name").GetString() ?? throw new Exception("Upload fallito");
    }

    private Dictionary<string, object> BuildSvdWorkflow(string imageFileName, AnimationSettings settings)
    {
        var seed = settings.AiMotionSeed >= 0 ? settings.AiMotionSeed : Random.Shared.Next();
        var frames = (int)(settings.DurationSeconds * settings.Fps);
        frames = Math.Min(frames, 25); // SVD max 25 frames per batch

        return new Dictionary<string, object>
        {
            ["1"] = new { class_type = "LoadImage", inputs = new { image = imageFileName } },
            ["2"] = new { class_type = "ImageOnlyCheckpointLoader", inputs = new { ckpt_name = "svd_xt.safetensors" } },
            ["3"] = new
            {
                class_type = "SVD_img2vid_Conditioning",
                inputs = new
                {
                    clip_vision = new object[] { "2", 1 },
                    init_image = new object[] { "1", 0 },
                    vae = new object[] { "2", 2 },
                    width = settings.OutputWidth,
                    height = settings.OutputHeight,
                    video_frames = frames,
                    motion_bucket_id = (int)(settings.AiMotionStrength * 200),
                    fps = Math.Min(settings.Fps, 14),
                    augmentation_level = 0.0
                }
            },
            ["4"] = new
            {
                class_type = "KSampler",
                inputs = new
                {
                    model = new object[] { "2", 0 },
                    positive = new object[] { "3", 0 },
                    negative = new object[] { "3", 1 },
                    latent_image = new object[] { "3", 2 },
                    seed,
                    steps = 20,
                    cfg = 2.5,
                    sampler_name = "euler",
                    scheduler = "karras",
                    denoise = 1.0
                }
            },
            ["5"] = new
            {
                class_type = "VAEDecode",
                inputs = new { samples = new object[] { "4", 0 }, vae = new object[] { "2", 2 } }
            },
            ["6"] = new
            {
                class_type = "SaveAnimatedWEBP",
                inputs = new { images = new object[] { "5", 0 }, filename_prefix = "videoforge", fps = Math.Min(settings.Fps, 14) }
            }
        };
    }

    private async Task<string> QueueWorkflowAsync(HttpClient client, Dictionary<string, object> workflow)
    {
        var payload = new { prompt = workflow };
        var json = JsonSerializer.Serialize(payload);
        var response = await client.PostAsync($"{_comfyUiUrl}/prompt",
            new StringContent(json, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(result);
        return doc.RootElement.GetProperty("prompt_id").GetString()!;
    }

    private async Task<string> PollCompletionAsync(HttpClient client, string promptId,
        IProgress<double> progress, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(2000, cancellationToken);

            var response = await client.GetAsync($"{_comfyUiUrl}/history/{promptId}", cancellationToken);
            if (!response.IsSuccessStatusCode) continue;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty(promptId, out var promptResult))
            {
                var outputs = promptResult.GetProperty("outputs");
                if (outputs.TryGetProperty("6", out var saveNode))
                {
                    var images = saveNode.GetProperty("images");
                    if (images.GetArrayLength() > 0)
                    {
                        var filename = images[0].GetProperty("filename").GetString()!;
                        var subfolder = images[0].GetProperty("subfolder").GetString() ?? "";
                        return Path.Combine(_comfyUiUrl.Replace("http://", "").Replace("localhost", ""),
                            "output", subfolder, filename);
                    }
                }
            }

            progress.Report(50); // Still processing
        }

        throw new OperationCanceledException();
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
            throw new InvalidOperationException($"FFmpeg AI Motion fallito (exit code {process.ExitCode})");

        progress.Report(100);
    }
}
