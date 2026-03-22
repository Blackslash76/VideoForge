using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SixLabors.ImageSharp;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services;

public class MontageService : IMontageService
{
    private readonly ConcurrentDictionary<Guid, MontageProject> _projects = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationTokens = new();
    private readonly IEnumerable<IAnimationEngine> _engines;
    private readonly IImageValidationService _imageValidation;
    private readonly IAnimationCache? _animationCache;
    private readonly string _storagePath;

    public MontageService(IEnumerable<IAnimationEngine> engines, IImageValidationService imageValidation,
        IAnimationCache? animationCache = null)
    {
        _engines = engines;
        _imageValidation = imageValidation;
        _animationCache = animationCache;
        _storagePath = Path.Combine(Path.GetTempPath(), "VideoForge", "montages");
        Directory.CreateDirectory(_storagePath);
    }

    public Task<MontageProject> CreateProjectAsync(string name, MontageStyle style, MontageProject project)
    {
        project.Name = string.IsNullOrWhiteSpace(name) ? $"Montaggio_{DateTime.UtcNow:yyyyMMdd_HHmmss}" : name;
        project.Style = style;
        project.TotalPhotos = project.Photos.Count;
        _projects[project.Id] = project;

        // Apply style presets
        ApplyStylePresets(project);

        // Start processing in background
        var cts = new CancellationTokenSource();
        _cancellationTokens[project.Id] = cts;
        _ = Task.Run(() => ProcessMontageAsync(project, cts.Token), cts.Token);

        return Task.FromResult(project);
    }

    public Task<MontageProject?> GetProjectAsync(Guid id)
    {
        _projects.TryGetValue(id, out var project);
        return Task.FromResult(project);
    }

    public Task<List<MontageProject>> GetAllProjectsAsync()
    {
        return Task.FromResult(_projects.Values.OrderByDescending(p => p.CreatedAt).ToList());
    }

    public Task<bool> CancelProjectAsync(Guid id)
    {
        if (_cancellationTokens.TryGetValue(id, out var cts))
        {
            cts.Cancel();
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> DeleteProjectAsync(Guid id)
    {
        if (!_projects.TryRemove(id, out _)) return Task.FromResult(false);
        _cancellationTokens.TryRemove(id, out var cts);
        cts?.Cancel();

        var dir = Path.Combine(_storagePath, id.ToString());
        if (Directory.Exists(dir))
            try { Directory.Delete(dir, true); } catch { }

        return Task.FromResult(true);
    }

    public string GetProjectStoragePath(Guid projectId)
    {
        var path = Path.Combine(_storagePath, projectId.ToString());
        Directory.CreateDirectory(path);
        return path;
    }

    // ─── Pipeline principale ─────────────────────────
    private async Task ProcessMontageAsync(MontageProject project, CancellationToken ct)
    {
        var projectDir = Path.Combine(_storagePath, project.Id.ToString());
        Directory.CreateDirectory(projectDir);

        try
        {
            var cinema = project.Cinematic;

            // FASE 0: Genera intro cinematico
            string? introClipPath = null;
            if (cinema.CinematicIntro && !string.IsNullOrWhiteSpace(cinema.IntroTitle))
            {
                project.StatusMessage = "Generazione intro cinematico...";
                project.Progress = 2;
                introClipPath = await CinematicEffectsService.GenerateIntroClipAsync(
                    cinema, project.OutputWidth, project.OutputHeight, project.Fps, project.UseHardwareAcceleration, ct);
            }

            // FASE 1: Anima ogni foto individualmente
            project.Status = MontageStatus.AnimatingPhotos;
            project.StatusMessage = "Animazione foto in corso...";
            await AnimateAllPhotosAsync(project, projectDir, ct);

            // FASE 1.5: Applica didascalie alle clip animate
            await ApplyCaptionsAsync(project, ct);

            // FASE 2: Assembla i clip animati in sequenza con transizioni
            project.Status = MontageStatus.AssemblingVideo;
            project.StatusMessage = "Assemblaggio video con transizioni...";
            project.Progress = 60;
            var silentVideoPath = await AssembleClipsAsync(project, projectDir, ct, introClipPath);

            // FASE 2.5: Applica effetti cinematici (color grading, vignette, grain)
            if (cinema.ColorGrade != ColorGrade.None || cinema.Vignette || cinema.FilmGrain || cinema.Letterbox)
            {
                project.StatusMessage = "Applicazione effetti cinematici...";
                project.Progress = 68;
                silentVideoPath = await ApplyCinematicEffectsAsync(project, silentVideoPath, projectDir, ct);
            }

            // FASE 3: Mix audio - concatena tracce, fade in/out, sincronizza
            project.Status = MontageStatus.MixingAudio;
            project.StatusMessage = "Mixaggio audio...";
            project.Progress = 75;
            var audioPath = await MixAudioAsync(project, projectDir, ct);

            // FASE 4: Encoding finale - video + audio
            project.Status = MontageStatus.FinalEncoding;
            project.StatusMessage = "Encoding finale GPU...";
            project.Progress = 85;
            var finalPath = await FinalEncodeAsync(project, silentVideoPath, audioPath, projectDir, ct);

            project.OutputPath = finalPath;
            project.Status = MontageStatus.Completed;
            project.Progress = 100;
            project.CompletedAt = DateTime.UtcNow;
            project.StatusMessage = "Video completato!";

            if (File.Exists(finalPath))
            {
                var fi = new FileInfo(finalPath);
                project.FileSizeBytes = fi.Length;
            }

            // Calcola durata totale
            project.TotalDurationSeconds = project.Photos.Sum(p => p.DisplayDuration)
                - project.Photos.Skip(1).Sum(p => p.TransitionDuration);
        }
        catch (OperationCanceledException)
        {
            project.Status = MontageStatus.Cancelled;
            project.StatusMessage = "Montaggio annullato";
        }
        catch (Exception ex)
        {
            project.Status = MontageStatus.Failed;
            project.ErrorMessage = ex.Message;
            project.StatusMessage = "Errore durante il montaggio";
        }
        finally
        {
            _cancellationTokens.TryRemove(project.Id, out _);
        }
    }

    // ─── Fase 1: Anima ogni foto ─────────────────────
    private async Task AnimateAllPhotosAsync(MontageProject project, string projectDir, CancellationToken ct)
    {
        var enginesDict = _engines.ToDictionary(e => e.SupportedType);
        var random = new Random();
        var kbDirections = Enum.GetValues<KenBurnsDirection>().Where(d => d != KenBurnsDirection.Random).ToArray();

        for (int i = 0; i < project.Photos.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var photo = project.Photos[i];
            var photoProgress = (double)i / project.Photos.Count * 55; // 0-55%
            project.Progress = photoProgress;
            project.PhotosAnimated = i;
            project.StatusMessage = $"Animazione foto {i + 1}/{project.Photos.Count}: {photo.FileName}";

            // Risolvi direzione random
            var kbDir = photo.KenBurnsDirection == KenBurnsDirection.Random
                ? kbDirections[random.Next(kbDirections.Length)]
                : photo.KenBurnsDirection;

            // Prepara il job di animazione
            var animJob = new AnimationJob
            {
                SourceImagePath = photo.FilePath,
                SourceFileName = photo.FileName,
                ImageWidth = photo.Width,
                ImageHeight = photo.Height,
                AnimationType = photo.AnimationType,
                Settings = new AnimationSettings
                {
                    DurationSeconds = photo.DisplayDuration,
                    Fps = project.Fps,
                    OutputWidth = project.OutputWidth,
                    OutputHeight = project.OutputHeight,
                    BitrateKbps = project.BitrateKbps,
                    UseHardwareAcceleration = project.UseHardwareAcceleration,
                    KenBurnsDirection = kbDir,
                    KenBurnsZoomFactor = photo.KenBurnsZoomFactor,
                    ParallaxIntensity = photo.ParallaxIntensity
                }
            };

            // Trova l'engine giusto, fallback a KenBurns
            if (!enginesDict.TryGetValue(photo.AnimationType, out var engine) || !engine.IsAvailable())
            {
                engine = enginesDict.GetValueOrDefault(AnimationType.KenBurns)
                    ?? throw new InvalidOperationException("Nessun engine di animazione disponibile");
            }

            // Cache lookup: stessa foto + stessi parametri = skip rendering
            string? clipPath = null;
            string? settingsFingerprint = null;

            if (_animationCache != null)
            {
                settingsFingerprint = _animationCache.ComputeSettingsFingerprint(
                    project.OutputWidth, project.OutputHeight, project.Fps, project.BitrateKbps,
                    photo.DisplayDuration, (int)photo.AnimationType, (int)kbDir,
                    photo.KenBurnsZoomFactor, photo.ParallaxIntensity, project.UseHardwareAcceleration);

                clipPath = await _animationCache.GetCachedClipAsync(photo.FilePath, settingsFingerprint, ct);
            }

            if (clipPath != null)
            {
                // Cache hit — skip rendering
                project.StatusMessage = $"Foto {i + 1}/{project.Photos.Count}: {photo.FileName} (dalla cache)";
                project.Progress = photoProgress + 55.0 / project.Photos.Count;
            }
            else
            {
                // Cache miss — render e salva in cache
                clipPath = await engine.AnimateAsync(animJob, new Progress<double>(p =>
                {
                    project.Progress = photoProgress + (p / 100.0) * (55.0 / project.Photos.Count);
                }), ct);

                if (_animationCache != null && settingsFingerprint != null)
                    await _animationCache.StoreCachedClipAsync(photo.FilePath, settingsFingerprint, clipPath, ct);
            }

            photo.AnimatedClipPath = clipPath;
            photo.IsAnimated = true;
        }

        project.PhotosAnimated = project.Photos.Count;
    }

    // ─── Fase 1.5: Applica didascalie ──────────────────
    private async Task ApplyCaptionsAsync(MontageProject project, CancellationToken ct)
    {
        foreach (var photo in project.Photos.Where(p => p.IsAnimated && p.Caption != null && !string.IsNullOrWhiteSpace(p.Caption.Text)))
        {
            var clipPath = photo.AnimatedClipPath!;
            var captionedPath = clipPath.Replace(".mp4", "_cap.mp4");

            var captionFilter = CinematicEffectsService.GetCaptionFilter(photo.Caption!, photo.DisplayDuration);
            var memoryFlash = project.Cinematic.MemoryFlash
                ? "," + CinematicEffectsService.GetMemoryFlashFilter(project.Cinematic.MemoryFlashDuration, photo.DisplayDuration)
                : "";

            var encoder = project.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
            var preset = project.UseHardwareAcceleration ? "-preset p4 -tune hq" : "-preset medium";
            var args = $"-i \"{clipPath}\" -vf \"{captionFilter}{memoryFlash}\" " +
                       $"-c:v {encoder} {preset} -b:v {project.BitrateKbps}k -y \"{captionedPath}\"";

            await RunFfmpegAsync(args, ct);

            photo.AnimatedClipPath = captionedPath;
        }
    }

    // ─── Fase 2: Assembla clip con transizioni ───────
    private async Task<string> AssembleClipsAsync(MontageProject project, string projectDir, CancellationToken ct, string? introClipPath = null)
    {
        var outputPath = Path.Combine(projectDir, "assembled_silent.mp4");
        var clips = project.Photos.Where(p => p.IsAnimated && p.AnimatedClipPath != null).ToList();

        if (clips.Count == 0)
            throw new InvalidOperationException("Nessun clip animato disponibile");

        // Prepend intro clip as a virtual MontagePhoto
        if (introClipPath != null)
        {
            clips.Insert(0, new MontagePhoto
            {
                AnimatedClipPath = introClipPath,
                IsAnimated = true,
                DisplayDuration = project.Cinematic.IntroDuration,
                TransitionToNext = TransitionType.CrossFade,
                TransitionDuration = 2.0,
                FileName = "_intro_"
            });
        }

        // Append outro clip
        if (project.Cinematic.CinematicOutro && !string.IsNullOrWhiteSpace(project.Cinematic.OutroTitle))
        {
            var outroPath = await CinematicEffectsService.GenerateOutroClipAsync(
                project.Cinematic, project.OutputWidth, project.OutputHeight, project.Fps, project.UseHardwareAcceleration, ct);
            clips.Add(new MontagePhoto
            {
                AnimatedClipPath = outroPath,
                IsAnimated = true,
                DisplayDuration = project.Cinematic.OutroDuration,
                TransitionToNext = TransitionType.Fade,
                TransitionDuration = 2.0,
                FileName = "_outro_"
            });
        }

        if (clips.Count == 1)
        {
            File.Copy(clips[0].AnimatedClipPath!, outputPath, true);
            return outputPath;
        }

        // Costruisci comando FFmpeg con xfade per le transizioni
        var sb = new StringBuilder();

        // Input files
        foreach (var clip in clips)
            sb.Append($"-i \"{clip.AnimatedClipPath}\" ");

        // Filter graph con transizioni
        sb.Append("-filter_complex \"");

        // Se ci sono intro/outro text, aggiungiamo dopo
        var lastOutput = "[0:v]";
        double currentOffset = 0;

        for (int i = 0; i < clips.Count - 1; i++)
        {
            var current = clips[i];
            var next = clips[i + 1];
            var transDur = next.TransitionDuration;
            var transType = MapTransitionToXfade(next.TransitionToNext);

            currentOffset += current.DisplayDuration - transDur;
            var segOut = i < clips.Count - 2 ? $"[v{i}]" : "[vout]";

            if (i == 0)
                sb.Append($"[0:v][1:v]xfade=transition={transType}:duration={transDur.ToString("F2", CultureInfo.InvariantCulture)}:offset={currentOffset.ToString("F2", CultureInfo.InvariantCulture)}{segOut};");
            else
                sb.Append($"{lastOutput}[{i + 1}:v]xfade=transition={transType}:duration={transDur.ToString("F2", CultureInfo.InvariantCulture)}:offset={currentOffset.ToString("F2", CultureInfo.InvariantCulture)}{segOut};");

            lastOutput = segOut;
        }

        // Intro text overlay
        if (!string.IsNullOrWhiteSpace(project.IntroText))
        {
            var introColor = (project.IntroFontColor ?? "#FFFFFF").Replace("#", "");
            sb.Append($"[vout]drawtext=text='{EscapeFfmpegText(project.IntroText)}':fontsize=64:fontcolor=0x{introColor}:x=(w-text_w)/2:y=(h-text_h)/2:enable='between(t,0,{project.IntroDuration.ToString("F1", CultureInfo.InvariantCulture)})':alpha='if(lt(t,1),t,if(gt(t,{(project.IntroDuration - 1).ToString("F1", CultureInfo.InvariantCulture)}),{project.IntroDuration.ToString("F1", CultureInfo.InvariantCulture)}-t,1))'[final];");
        }
        else
        {
            // Rinomina output
            sb.Replace("[vout];", "[final];");
            if (!sb.ToString().Contains("[final]"))
                sb.Append("[vout]null[final];");
        }

        // Rimuovi ultimo ;
        var filter = sb.ToString().TrimEnd(';');
        filter += "\" ";

        var encoder = project.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
        var presetArgs = project.UseHardwareAcceleration ? "-preset p4 -tune hq -rc vbr" : "-preset medium -crf 18";

        var args = $"{filter} -map \"[final]\" -c:v {encoder} -b:v {project.BitrateKbps}k {presetArgs} " +
                   $"-r {project.Fps} -pix_fmt yuv420p -movflags +faststart -y \"{outputPath}\"";

        await RunFfmpegAsync(args, ct);
        return outputPath;
    }

    // ─── Fase 2.5: Effetti cinematici globali ──────────
    private async Task<string> ApplyCinematicEffectsAsync(MontageProject project, string videoPath, string projectDir, CancellationToken ct)
    {
        var outputPath = Path.Combine(projectDir, "cinematic_graded.mp4");
        var filterChain = CinematicEffectsService.BuildCinematicFilterChain(
            project.Cinematic, project.OutputWidth, project.OutputHeight);

        if (string.IsNullOrEmpty(filterChain))
            return videoPath;

        var encoder = project.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
        var preset = project.UseHardwareAcceleration ? "-preset p4 -tune hq -rc vbr" : "-preset medium -crf 18";

        var args = $"-i \"{videoPath}\" -vf \"{filterChain}\" " +
                   $"-c:v {encoder} -b:v {project.BitrateKbps}k {preset} " +
                   $"-pix_fmt yuv420p -movflags +faststart -y \"{outputPath}\"";

        await RunFfmpegAsync(args, ct);
        return outputPath;
    }

    // ─── Fase 3: Mix audio ───────────────────────────
    private async Task<string?> MixAudioAsync(MontageProject project, string projectDir, CancellationToken ct)
    {
        if (project.AudioTracks.Count == 0)
            return null;

        var outputPath = Path.Combine(projectDir, "mixed_audio.aac");
        var videoDuration = project.Photos.Sum(p => p.DisplayDuration)
            - project.Photos.Skip(1).Sum(p => p.TransitionDuration);

        if (project.AudioTracks.Count == 1)
        {
            // Singola traccia: taglia alla durata video + fade in/out
            var track = project.AudioTracks[0];
            var fadeIn = track.FadeInSeconds.ToString("F1", CultureInfo.InvariantCulture);
            var fadeOut = track.FadeOutSeconds.ToString("F1", CultureInfo.InvariantCulture);
            var dur = videoDuration.ToString("F2", CultureInfo.InvariantCulture);
            var fadeOutStart = (videoDuration - track.FadeOutSeconds).ToString("F2", CultureInfo.InvariantCulture);
            var vol = track.Volume.ToString("F2", CultureInfo.InvariantCulture);

            var args = $"-i \"{track.FilePath}\" " +
                       $"-af \"afade=t=in:st=0:d={fadeIn},afade=t=out:st={fadeOutStart}:d={fadeOut},volume={vol}\" " +
                       $"-t {dur} -c:a aac -b:a 256k -y \"{outputPath}\"";

            await RunFfmpegAsync(args, ct);
        }
        else
        {
            // Multiple tracce: concatena con crossfade tra di loro
            var sb = new StringBuilder();
            foreach (var track in project.AudioTracks.OrderBy(t => t.Order))
                sb.Append($"-i \"{track.FilePath}\" ");

            sb.Append("-filter_complex \"");

            // Fade e volume per ogni traccia
            for (int i = 0; i < project.AudioTracks.Count; i++)
            {
                var track = project.AudioTracks.OrderBy(t => t.Order).ElementAt(i);
                var vol = track.Volume.ToString("F2", CultureInfo.InvariantCulture);
                sb.Append($"[{i}:a]volume={vol}[a{i}];");
            }

            // Concatena
            for (int i = 0; i < project.AudioTracks.Count; i++)
                sb.Append($"[a{i}]");
            sb.Append($"concat=n={project.AudioTracks.Count}:v=0:a=1[mixed];");

            // Fade globali e taglia alla durata video
            var globalFadeIn = project.AudioTracks.First().FadeInSeconds.ToString("F1", CultureInfo.InvariantCulture);
            var globalFadeOut = project.AudioTracks.Last().FadeOutSeconds.ToString("F1", CultureInfo.InvariantCulture);
            var fadeOutSt = (videoDuration - project.AudioTracks.Last().FadeOutSeconds).ToString("F2", CultureInfo.InvariantCulture);

            sb.Append($"[mixed]afade=t=in:st=0:d={globalFadeIn},afade=t=out:st={fadeOutSt}:d={globalFadeOut}[out]\"");

            var dur = videoDuration.ToString("F2", CultureInfo.InvariantCulture);
            var args = $"{sb} -map \"[out]\" -t {dur} -c:a aac -b:a 256k -y \"{outputPath}\"";
            await RunFfmpegAsync(args, ct);
        }

        return outputPath;
    }

    // ─── Fase 4: Encoding finale ─────────────────────
    private async Task<string> FinalEncodeAsync(MontageProject project, string videoPath, string? audioPath,
        string projectDir, CancellationToken ct)
    {
        var outputPath = Path.Combine(projectDir, $"{SanitizeFileName(project.Name)}.mp4");
        var encoder = project.UseHardwareAcceleration ? "h264_nvenc" : "libx264";
        var presetArgs = project.UseHardwareAcceleration ? "-preset p4 -tune hq -rc vbr" : "-preset medium -crf 18";

        string args;
        if (audioPath != null)
        {
            args = $"-i \"{videoPath}\" -i \"{audioPath}\" " +
                   $"-c:v {encoder} -b:v {project.BitrateKbps}k {presetArgs} " +
                   $"-c:a aac -b:a 256k -shortest " +
                   $"-pix_fmt yuv420p -movflags +faststart -y \"{outputPath}\"";
        }
        else
        {
            // Senza audio
            args = $"-i \"{videoPath}\" " +
                   $"-c:v {encoder} -b:v {project.BitrateKbps}k {presetArgs} " +
                   $"-pix_fmt yuv420p -movflags +faststart -y \"{outputPath}\"";
        }

        await RunFfmpegAsync(args, ct);
        return outputPath;
    }

    // ─── Style presets ───────────────────────────────
    private void ApplyStylePresets(MontageProject project)
    {
        var random = new Random();
        var photos = project.Photos;

        switch (project.Style)
        {
            case MontageStyle.Cinematic:
                // Lento, emotivo: KenBurns + Parallax alternati, crossfade lunghi
                for (int i = 0; i < photos.Count; i++)
                {
                    photos[i].DisplayDuration = Math.Max(photos[i].DisplayDuration, 5.0);
                    photos[i].TransitionDuration = 2.0;
                    photos[i].TransitionToNext = TransitionType.CrossFade;
                    photos[i].AnimationType = i % 3 == 0 ? AnimationType.Parallax
                        : i % 3 == 1 ? AnimationType.KenBurns
                        : AnimationType.KenBurnsParallax;
                    photos[i].KenBurnsZoomFactor = 1.15 + random.NextDouble() * 0.15;
                    photos[i].KenBurnsDirection = KenBurnsDirection.Random;
                }
                break;

            case MontageStyle.Storytelling:
                // Mix bilanciato di effetti
                for (int i = 0; i < photos.Count; i++)
                {
                    photos[i].DisplayDuration = Math.Max(photos[i].DisplayDuration, 4.0);
                    photos[i].TransitionDuration = 1.5;
                    photos[i].TransitionToNext = (TransitionType)(random.Next(1, 6));
                    photos[i].AnimationType = (AnimationType)(random.Next(0, 3));
                    photos[i].KenBurnsDirection = KenBurnsDirection.Random;
                }
                break;

            case MontageStyle.Energetic:
                // Veloce, dinamico
                for (int i = 0; i < photos.Count; i++)
                {
                    photos[i].DisplayDuration = Math.Max(photos[i].DisplayDuration, 2.5);
                    photos[i].TransitionDuration = 0.8;
                    photos[i].TransitionToNext = random.Next(2) == 0 ? TransitionType.Slide : TransitionType.Zoom;
                    photos[i].AnimationType = AnimationType.KenBurns;
                    photos[i].KenBurnsZoomFactor = 1.3 + random.NextDouble() * 0.2;
                    photos[i].KenBurnsDirection = KenBurnsDirection.Random;
                }
                break;

            case MontageStyle.Elegant:
                // Minimal, morbido
                for (int i = 0; i < photos.Count; i++)
                {
                    photos[i].DisplayDuration = Math.Max(photos[i].DisplayDuration, 6.0);
                    photos[i].TransitionDuration = 2.5;
                    photos[i].TransitionToNext = TransitionType.Dissolve;
                    photos[i].AnimationType = i % 2 == 0 ? AnimationType.KenBurns : AnimationType.Parallax;
                    photos[i].KenBurnsZoomFactor = 1.1;
                    photos[i].KenBurnsDirection = KenBurnsDirection.Random;
                    photos[i].ParallaxIntensity = 0.3;
                }
                break;
        }
    }

    // ─── Helpers ─────────────────────────────────────
    private static string MapTransitionToXfade(TransitionType transition) => transition switch
    {
        TransitionType.Fade => "fade",
        TransitionType.CrossFade => "fadeblack",
        TransitionType.Slide => "slideleft",
        TransitionType.Zoom => "squeezeh",
        TransitionType.Dissolve => "dissolve",
        _ => "fade"
    };

    private static string EscapeFfmpegText(string text) =>
        text.Replace("'", "'\\''").Replace(":", "\\:").Replace("\\", "\\\\");

    private static string SanitizeFileName(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

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
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"FFmpeg errore (exit {process.ExitCode}): {error[..Math.Min(error.Length, 500)]}");
        }
    }
}
