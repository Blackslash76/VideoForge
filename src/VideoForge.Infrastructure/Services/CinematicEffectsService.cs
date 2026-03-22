using System.Diagnostics;
using System.Globalization;
using System.Text;
using VideoForge.Core.Enums;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services;

/// <summary>
/// Genera tutti gli effetti cinematici: color grading, vignette, grain,
/// intro/outro, didascalie, particelle, letterbox, memory flash.
/// </summary>
public class CinematicEffectsService
{
    // ─── Color Grading FFmpeg Filters ────────────────
    public static string GetColorGradeFilter(ColorGrade grade) => grade switch
    {
        ColorGrade.WarmMemory =>
            "colortemperature=temperature=5800,eq=saturation=1.15:brightness=0.03:gamma=1.05," +
            "curves=master='0/0 0.25/0.28 0.5/0.55 0.75/0.78 1/1':red='0/0 0.5/0.55 1/1'",

        ColorGrade.CinematicTeal =>
            "colorbalance=rs=-0.1:gs=0.05:bs=0.15:rm=0.1:gm=-0.02:bm=-0.05," +
            "eq=contrast=1.1:brightness=0.02:saturation=1.1," +
            "curves=master='0/0 0.15/0.1 0.5/0.52 0.85/0.9 1/1'",

        ColorGrade.VintageNostalgia =>
            "colortemperature=temperature=6500,eq=saturation=0.8:brightness=0.04:contrast=0.95," +
            "curves=master='0/0.05 0.25/0.28 0.75/0.72 1/0.92':" +
            "red='0/0.02 0.5/0.53 1/0.95':green='0/0.01 0.5/0.5 1/0.93':blue='0/0.05 0.5/0.48 1/0.88'",

        ColorGrade.DramaticBW =>
            "hue=s=0,eq=contrast=1.3:brightness=-0.02:gamma=0.95," +
            "curves=master='0/0 0.2/0.1 0.5/0.55 0.8/0.9 1/1'",

        ColorGrade.VividDream =>
            "eq=saturation=1.4:contrast=1.1:brightness=0.02," +
            "curves=master='0/0 0.25/0.22 0.5/0.55 0.75/0.82 1/1'",

        ColorGrade.SoftPastel =>
            "colortemperature=temperature=5500,eq=saturation=0.85:brightness=0.06:contrast=0.92," +
            "curves=master='0/0.08 0.5/0.55 1/0.95'",

        _ => ""
    };

    // ─── Vignette Filter ─────────────────────────────
    public static string GetVignetteFilter(double intensity)
    {
        var angle = (intensity * 0.5).ToString("F2", CultureInfo.InvariantCulture);
        return $"vignette=angle={angle}:mode=forward";
    }

    // ─── Film Grain Filter ───────────────────────────
    public static string GetFilmGrainFilter(double intensity, int width, int height)
    {
        var amount = (intensity * 30).ToString("F0", CultureInfo.InvariantCulture);
        return $"noise=c0s={amount}:c0f=t+u:allf=t+u";
    }

    // ─── Letterbox Filter ────────────────────────────
    public static string GetLetterboxFilter(double ratio, int width, int height)
    {
        var visibleHeight = (int)(width / ratio);
        var barSize = (height - visibleHeight) / 2;
        if (barSize <= 0) return "";
        return $"drawbox=x=0:y=0:w={width}:h={barSize}:color=black:t=fill," +
               $"drawbox=x=0:y={height - barSize}:w={width}:h={barSize}:color=black:t=fill";
    }

    // ─── Caption/Didascalia Filter ───────────────────
    public static string GetCaptionFilter(PhotoCaption caption, double clipDuration)
    {
        var text = EscapeText(caption.Text);
        var fontSize = caption.FontSize.ToString("F0", CultureInfo.InvariantCulture);
        var color = caption.FontColor.Replace("#", "0x");
        var delay = caption.DelaySeconds;
        var fadeIn = caption.FadeInDuration;
        var fadeOut = caption.FadeOutDuration;
        var endTime = clipDuration - 0.3;

        var y = caption.Position switch
        {
            "top" => "h*0.08",
            "center" => "(h-text_h)/2",
            _ => "h*0.85"
        };

        var shadow = caption.HasShadow ? ":shadowcolor=0x000000@0.6:shadowx=3:shadowy=3" : "";

        var alphaExpr = $"if(lt(t,{delay.ToString("F1", CultureInfo.InvariantCulture)}),0," +
                        $"if(lt(t,{(delay + fadeIn).ToString("F1", CultureInfo.InvariantCulture)})," +
                        $"(t-{delay.ToString("F1", CultureInfo.InvariantCulture)})/{fadeIn.ToString("F1", CultureInfo.InvariantCulture)}," +
                        $"if(gt(t,{(endTime - fadeOut).ToString("F1", CultureInfo.InvariantCulture)})," +
                        $"({endTime.ToString("F1", CultureInfo.InvariantCulture)}-t)/{fadeOut.ToString("F1", CultureInfo.InvariantCulture)}," +
                        $"1)))";

        return $"drawtext=text='{text}':fontsize={fontSize}:fontcolor={color}@1.0:" +
               $"x=(w-text_w)/2:y={y}:" +
               $"alpha='{alphaExpr}'{shadow}";
    }

    // ─── Memory Flash Filter (flash bianco tra foto) ─
    public static string GetMemoryFlashFilter(double duration, double clipDuration)
    {
        var flashStart = (clipDuration - duration).ToString("F2", CultureInfo.InvariantCulture);
        var dur = duration.ToString("F2", CultureInfo.InvariantCulture);
        return $"fade=t=out:st={flashStart}:d={dur}:color=white";
    }

    // ─── Cinematic Intro Generator ───────────────────
    public static async Task<string> GenerateIntroClipAsync(CinematicSettings settings, int width, int height,
        int fps, bool hwAccel, CancellationToken ct)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "VideoForge", $"intro_{Guid.NewGuid():N}.mp4");
        var dur = settings.IntroDuration.ToString("F1", CultureInfo.InvariantCulture);
        var fadeIn = settings.IntroFadeIn.ToString("F1", CultureInfo.InvariantCulture);
        var encoder = hwAccel ? "h264_nvenc" : "libx264";
        var preset = hwAccel ? "-preset p4 -tune hq" : "-preset medium";

        var sb = new StringBuilder();
        sb.Append($"-f lavfi -i color=c=black:s={width}x{height}:d={dur}:r={fps} ");
        sb.Append("-filter_complex \"[0:v]format=yuv420p");

        double yPos = height * 0.35;
        double lineGap = 80;

        // Titolo principale
        if (!string.IsNullOrWhiteSpace(settings.IntroTitle))
        {
            var title = EscapeText(settings.IntroTitle);
            sb.Append($",drawtext=text='{title}':fontsize=72:fontcolor=0xFFFFFF:" +
                      $"x=(w-text_w)/2:y={yPos.ToString("F0", CultureInfo.InvariantCulture)}:" +
                      $"alpha='if(lt(t,{fadeIn}),t/{fadeIn},if(gt(t,{(settings.IntroDuration - 1.5).ToString("F1", CultureInfo.InvariantCulture)}),({(settings.IntroDuration).ToString("F1", CultureInfo.InvariantCulture)}-t)/1.5,1))':" +
                      $"shadowcolor=0x000000@0.5:shadowx=2:shadowy=2");
            yPos += lineGap;
        }

        // Sottotitolo
        if (!string.IsNullOrWhiteSpace(settings.IntroSubtitle))
        {
            var sub = EscapeText(settings.IntroSubtitle);
            var subDelay = 1.5;
            sb.Append($",drawtext=text='{sub}':fontsize=36:fontcolor=0xCCCCCC:" +
                      $"x=(w-text_w)/2:y={yPos.ToString("F0", CultureInfo.InvariantCulture)}:" +
                      $"alpha='if(lt(t,{subDelay.ToString("F1", CultureInfo.InvariantCulture)}),0," +
                      $"if(lt(t,{(subDelay + 1.5).ToString("F1", CultureInfo.InvariantCulture)}),(t-{subDelay.ToString("F1", CultureInfo.InvariantCulture)})/1.5,1))':" +
                      $"shadowcolor=0x000000@0.3:shadowx=1:shadowy=1");
            yPos += lineGap * 0.7;
        }

        // Data
        if (!string.IsNullOrWhiteSpace(settings.IntroDate))
        {
            var date = EscapeText(settings.IntroDate);
            var dateDelay = 2.5;
            sb.Append($",drawtext=text='{date}':fontsize=28:fontcolor=0x999999:" +
                      $"x=(w-text_w)/2:y={yPos.ToString("F0", CultureInfo.InvariantCulture)}:" +
                      $"alpha='if(lt(t,{dateDelay.ToString("F1", CultureInfo.InvariantCulture)}),0," +
                      $"if(lt(t,{(dateDelay + 1).ToString("F1", CultureInfo.InvariantCulture)}),(t-{dateDelay.ToString("F1", CultureInfo.InvariantCulture)}),1))'");
        }

        sb.Append("[out]\" ");
        sb.Append($"-map \"[out]\" -c:v {encoder} {preset} -t {dur} -movflags +faststart -y \"{outputPath}\"");

        await RunFfmpegAsync(sb.ToString(), ct);
        return outputPath;
    }

    // ─── Cinematic Outro Generator ───────────────────
    public static async Task<string> GenerateOutroClipAsync(CinematicSettings settings, int width, int height,
        int fps, bool hwAccel, CancellationToken ct)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "VideoForge", $"outro_{Guid.NewGuid():N}.mp4");
        var dur = settings.OutroDuration.ToString("F1", CultureInfo.InvariantCulture);
        var encoder = hwAccel ? "h264_nvenc" : "libx264";
        var preset = hwAccel ? "-preset p4 -tune hq" : "-preset medium";

        var sb = new StringBuilder();
        sb.Append($"-f lavfi -i color=c=black:s={width}x{height}:d={dur}:r={fps} ");
        sb.Append("-filter_complex \"[0:v]format=yuv420p");

        double yPos = height * 0.3;
        double lineGap = 75;

        // Titolo outro
        if (!string.IsNullOrWhiteSpace(settings.OutroTitle))
        {
            var title = EscapeText(settings.OutroTitle);
            sb.Append($",drawtext=text='{title}':fontsize=64:fontcolor=0xFFFFFF:" +
                      $"x=(w-text_w)/2:y={yPos.ToString("F0", CultureInfo.InvariantCulture)}:" +
                      $"alpha='if(lt(t,1.5),t/1.5,if(gt(t,{(settings.OutroDuration - 2).ToString("F1", CultureInfo.InvariantCulture)}),({settings.OutroDuration.ToString("F1", CultureInfo.InvariantCulture)}-t)/2,1))':" +
                      $"shadowcolor=0x000000@0.5:shadowx=2:shadowy=2");
            yPos += lineGap;
        }

        // Messaggio dedica
        if (!string.IsNullOrWhiteSpace(settings.OutroMessage))
        {
            var msg = EscapeText(settings.OutroMessage);
            var msgDelay = 2.0;
            sb.Append($",drawtext=text='{msg}':fontsize=32:fontcolor=0xDDDDDD:" +
                      $"x=(w-text_w)/2:y={yPos.ToString("F0", CultureInfo.InvariantCulture)}:" +
                      $"alpha='if(lt(t,{msgDelay.ToString("F1", CultureInfo.InvariantCulture)}),0," +
                      $"if(lt(t,{(msgDelay + 1.5).ToString("F1", CultureInfo.InvariantCulture)}),(t-{msgDelay.ToString("F1", CultureInfo.InvariantCulture)})/1.5," +
                      $"if(gt(t,{(settings.OutroDuration - 2).ToString("F1", CultureInfo.InvariantCulture)}),({settings.OutroDuration.ToString("F1", CultureInfo.InvariantCulture)}-t)/2,1)))'");
            yPos += lineGap;
        }

        // Credits
        if (!string.IsNullOrWhiteSpace(settings.OutroCredits))
        {
            var credits = EscapeText(settings.OutroCredits);
            var credDelay = 3.5;
            sb.Append($",drawtext=text='{credits}':fontsize=22:fontcolor=0x888888:" +
                      $"x=(w-text_w)/2:y={yPos.ToString("F0", CultureInfo.InvariantCulture)}:" +
                      $"alpha='if(lt(t,{credDelay.ToString("F1", CultureInfo.InvariantCulture)}),0," +
                      $"if(lt(t,{(credDelay + 1).ToString("F1", CultureInfo.InvariantCulture)}),(t-{credDelay.ToString("F1", CultureInfo.InvariantCulture)}),1))'");
        }

        sb.Append("[out]\" ");
        sb.Append($"-map \"[out]\" -c:v {encoder} {preset} -t {dur} -movflags +faststart -y \"{outputPath}\"");

        await RunFfmpegAsync(sb.ToString(), ct);
        return outputPath;
    }

    // ─── Particle Overlay Generator ──────────────────
    public static async Task<string> GenerateParticleOverlayAsync(ParticleEffect effect, double duration,
        int width, int height, int fps, double intensity, CancellationToken ct)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "VideoForge", $"particles_{Guid.NewGuid():N}.mp4");
        var dur = duration.ToString("F1", CultureInfo.InvariantCulture);
        var particleCount = (int)(intensity * 30);

        // Genera overlay con particelle animate usando lavfi
        var filterExpr = effect switch
        {
            ParticleEffect.Hearts => BuildHeartParticles(particleCount, width, height, fps),
            ParticleEffect.Stars => BuildStarParticles(particleCount, width, height, fps),
            ParticleEffect.Sparkles => BuildSparkleParticles(particleCount, width, height, fps),
            ParticleEffect.Snow => BuildSnowParticles(particleCount, width, height, fps),
            ParticleEffect.Fireflies => BuildFireflyParticles(particleCount, width, height, fps),
            ParticleEffect.Bubbles => BuildBubbleParticles(particleCount, width, height, fps),
            ParticleEffect.Confetti => BuildConfettiParticles(particleCount, width, height, fps),
            _ => ""
        };

        if (string.IsNullOrEmpty(filterExpr)) return "";

        var args = $"-f lavfi -i color=c=black@0.0:s={width}x{height}:d={dur}:r={fps},format=rgba " +
                   $"-filter_complex \"{filterExpr}\" " +
                   $"-c:v png -t {dur} -y \"{outputPath}\"";

        // Per particelle complesse, usiamo un approccio semplificato con drawtext di simboli
        // che si muovono usando le espressioni FFmpeg
        var simpleArgs = $"-f lavfi -i \"color=c=black@0.0:s={width}x{height}:d={dur}:r={fps},format=rgba\" " +
                         $"-vf \"{filterExpr}\" " +
                         $"-c:v qtrle -t {dur} -y \"{outputPath}\"";

        await RunFfmpegAsync(simpleArgs, ct);
        return outputPath;
    }

    // Particelle semplificate con drawtext di emoji/simboli animati
    private static string BuildHeartParticles(int count, int w, int h, int fps)
    {
        var sb = new StringBuilder("format=rgba");
        var rng = new Random(42);
        for (int i = 0; i < Math.Min(count, 12); i++)
        {
            var startX = rng.Next(50, w - 50);
            var speed = 20 + rng.Next(40);
            var size = 18 + rng.Next(24);
            var delay = rng.NextDouble() * 3;
            sb.Append($",drawtext=text='\\u2665':fontsize={size}:fontcolor=0xFF6B8A@0.7:" +
                      $"x={startX}+{rng.Next(-20, 20)}*sin(t*0.8):" +
                      $"y=h-({speed}*mod(t+{delay.ToString("F1", CultureInfo.InvariantCulture)},h/{speed}))");
        }
        return sb.ToString();
    }

    private static string BuildStarParticles(int count, int w, int h, int fps)
    {
        var sb = new StringBuilder("format=rgba");
        var rng = new Random(42);
        for (int i = 0; i < Math.Min(count, 15); i++)
        {
            var x = rng.Next(20, w - 20);
            var y = rng.Next(20, h - 20);
            var size = 12 + rng.Next(20);
            var phase = rng.NextDouble() * 6.28;
            sb.Append($",drawtext=text='\\u2726':fontsize={size}:fontcolor=0xFFD700@0.8:" +
                      $"x={x}:y={y}:" +
                      $"alpha='0.3+0.7*abs(sin(t*{(1 + rng.NextDouble()).ToString("F1", CultureInfo.InvariantCulture)}+{phase.ToString("F1", CultureInfo.InvariantCulture)}))'");
        }
        return sb.ToString();
    }

    private static string BuildSparkleParticles(int count, int w, int h, int fps)
    {
        var sb = new StringBuilder("format=rgba");
        var rng = new Random(42);
        for (int i = 0; i < Math.Min(count, 20); i++)
        {
            var x = rng.Next(10, w - 10);
            var y = rng.Next(10, h - 10);
            var size = 8 + rng.Next(16);
            var freq = 2 + rng.NextDouble() * 3;
            var phase = rng.NextDouble() * 6.28;
            sb.Append($",drawtext=text='\\u2727':fontsize={size}:fontcolor=0xFFFFFF:" +
                      $"x={x}:y={y}:" +
                      $"alpha='max(0,sin(t*{freq.ToString("F1", CultureInfo.InvariantCulture)}+{phase.ToString("F1", CultureInfo.InvariantCulture)}))'");
        }
        return sb.ToString();
    }

    private static string BuildSnowParticles(int count, int w, int h, int fps)
    {
        var sb = new StringBuilder("format=rgba");
        var rng = new Random(42);
        for (int i = 0; i < Math.Min(count, 20); i++)
        {
            var startX = rng.Next(0, w);
            var speed = 30 + rng.Next(50);
            var size = 10 + rng.Next(14);
            var sway = 15 + rng.Next(25);
            var delay = rng.NextDouble() * 5;
            sb.Append($",drawtext=text='\\u2022':fontsize={size}:fontcolor=0xFFFFFF@0.6:" +
                      $"x={startX}+{sway}*sin(t*0.5+{delay.ToString("F1", CultureInfo.InvariantCulture)}):" +
                      $"y=mod({speed}*(t+{delay.ToString("F1", CultureInfo.InvariantCulture)}),h+50)-25");
        }
        return sb.ToString();
    }

    private static string BuildFireflyParticles(int count, int w, int h, int fps)
    {
        var sb = new StringBuilder("format=rgba");
        var rng = new Random(42);
        for (int i = 0; i < Math.Min(count, 12); i++)
        {
            var cx = rng.Next(100, w - 100);
            var cy = rng.Next(100, h - 100);
            var radius = 30 + rng.Next(60);
            var speed = 0.3 + rng.NextDouble() * 0.5;
            var phase = rng.NextDouble() * 6.28;
            sb.Append($",drawtext=text='\\u2022':fontsize=10:fontcolor=0xFFFF88:" +
                      $"x={cx}+{radius}*cos(t*{speed.ToString("F2", CultureInfo.InvariantCulture)}+{phase.ToString("F1", CultureInfo.InvariantCulture)}):" +
                      $"y={cy}+{radius * 0.6}*sin(t*{(speed * 1.3).ToString("F2", CultureInfo.InvariantCulture)}+{phase.ToString("F1", CultureInfo.InvariantCulture)}):" +
                      $"alpha='0.2+0.8*abs(sin(t*{(1.5 + rng.NextDouble()).ToString("F1", CultureInfo.InvariantCulture)}))'");
        }
        return sb.ToString();
    }

    private static string BuildBubbleParticles(int count, int w, int h, int fps)
    {
        var sb = new StringBuilder("format=rgba");
        var rng = new Random(42);
        for (int i = 0; i < Math.Min(count, 10); i++)
        {
            var startX = rng.Next(50, w - 50);
            var speed = 25 + rng.Next(35);
            var size = 14 + rng.Next(18);
            var sway = 10 + rng.Next(20);
            var delay = rng.NextDouble() * 4;
            sb.Append($",drawtext=text='\\u25CB':fontsize={size}:fontcolor=0xAADDFF@0.5:" +
                      $"x={startX}+{sway}*sin(t*0.7+{delay.ToString("F1", CultureInfo.InvariantCulture)}):" +
                      $"y=h-({speed}*mod(t+{delay.ToString("F1", CultureInfo.InvariantCulture)},h/{speed}))");
        }
        return sb.ToString();
    }

    private static string BuildConfettiParticles(int count, int w, int h, int fps)
    {
        var sb = new StringBuilder("format=rgba");
        var rng = new Random(42);
        var colors = new[] { "0xFF6B6B", "0xFFD93D", "0x6BCB77", "0x4D96FF", "0xFF6BB5", "0xC084FC" };
        for (int i = 0; i < Math.Min(count, 18); i++)
        {
            var startX = rng.Next(0, w);
            var speed = 40 + rng.Next(60);
            var size = 12 + rng.Next(10);
            var sway = 20 + rng.Next(30);
            var delay = rng.NextDouble() * 4;
            var color = colors[rng.Next(colors.Length)];
            sb.Append($",drawtext=text='\\u25A0':fontsize={size}:fontcolor={color}@0.8:" +
                      $"x={startX}+{sway}*sin(t*1.2+{delay.ToString("F1", CultureInfo.InvariantCulture)}):" +
                      $"y=mod({speed}*(t+{delay.ToString("F1", CultureInfo.InvariantCulture)}),h+50)-25");
        }
        return sb.ToString();
    }

    // ─── Build complete cinematic filter chain ───────
    public static string BuildCinematicFilterChain(CinematicSettings settings, int width, int height)
    {
        var filters = new List<string>();

        if (settings.ColorGrade != ColorGrade.None)
        {
            var cf = GetColorGradeFilter(settings.ColorGrade);
            if (!string.IsNullOrEmpty(cf)) filters.Add(cf);
        }

        if (settings.Vignette)
            filters.Add(GetVignetteFilter(settings.VignetteIntensity));

        if (settings.FilmGrain)
            filters.Add(GetFilmGrainFilter(settings.FilmGrainIntensity, width, height));

        if (settings.Letterbox)
        {
            var lb = GetLetterboxFilter(settings.LetterboxRatio, width, height);
            if (!string.IsNullOrEmpty(lb)) filters.Add(lb);
        }

        return string.Join(",", filters);
    }

    // ─── Helpers ─────────────────────────────────────
    private static string EscapeText(string text) =>
        text.Replace("'", "'\\''").Replace(":", "\\:").Replace("\\", "\\\\").Replace("%", "%%");

    private static async Task RunFfmpegAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossibile avviare FFmpeg");
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"FFmpeg errore: {err[..Math.Min(err.Length, 500)]}");
        }
    }
}
