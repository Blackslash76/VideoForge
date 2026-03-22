using System.Collections.Concurrent;
using SixLabors.ImageSharp;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services;

public class AnimationOrchestrator : IAnimationOrchestrator
{
    private readonly ConcurrentDictionary<Guid, AnimationJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationTokens = new();
    private readonly Dictionary<AnimationType, IAnimationEngine> _engines;
    private readonly IImageValidationService _imageValidation;
    private readonly IAnimationCache? _animationCache;
    private readonly string _storagePath;

    public AnimationOrchestrator(
        IEnumerable<IAnimationEngine> engines,
        IImageValidationService imageValidation,
        IAnimationCache? animationCache = null)
    {
        _engines = engines.ToDictionary(e => e.SupportedType);
        _imageValidation = imageValidation;
        _animationCache = animationCache;
        _storagePath = Path.Combine(Path.GetTempPath(), "VideoForge", "animations");
        Directory.CreateDirectory(_storagePath);
    }

    public async Task<AnimationJob> CreateJobAsync(Stream imageStream, string fileName,
        AnimationSettings settings, AnimationType animationType)
    {
        if (!_imageValidation.IsSupportedFormat(fileName))
            throw new ArgumentException($"Formato non supportato: {Path.GetExtension(fileName)}");

        // Validate image
        using var image = await Image.LoadAsync(imageStream);
        imageStream.Position = 0;

        var job = new AnimationJob
        {
            SourceFileName = fileName,
            AnimationType = animationType,
            Settings = settings,
            ImageWidth = image.Width,
            ImageHeight = image.Height
        };

        // Save image to storage
        var jobDir = Path.Combine(_storagePath, job.Id.ToString());
        Directory.CreateDirectory(jobDir);
        var imagePath = Path.Combine(jobDir, fileName);

        using (var fileStream = new FileStream(imagePath, FileMode.Create))
        {
            await imageStream.CopyToAsync(fileStream);
        }

        job.SourceImagePath = imagePath;
        _jobs[job.Id] = job;

        // Start animation in background
        var cts = new CancellationTokenSource();
        _cancellationTokens[job.Id] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                job.Status = AnimationStatus.AnalyzingImage;
                job.StatusMessage = "Analisi immagine in corso...";

                if (!_engines.TryGetValue(animationType, out var engine))
                    throw new InvalidOperationException($"Engine non disponibile per {animationType}");

                if (!engine.IsAvailable())
                {
                    // Fallback: se l'engine richiesto non è disponibile, usa KenBurns
                    if (_engines.TryGetValue(AnimationType.KenBurns, out var fallback))
                    {
                        engine = fallback;
                        job.StatusMessage = $"Engine {animationType} non disponibile, fallback a Ken Burns";
                    }
                    else
                    {
                        throw new InvalidOperationException($"Nessun engine di animazione disponibile");
                    }
                }

                job.Status = AnimationStatus.Animating;

                // Cache check
                string? cachedPath = null;
                string? fingerprint = null;
                if (_animationCache != null)
                {
                    fingerprint = _animationCache.ComputeSettingsFingerprint(
                        settings.OutputWidth, settings.OutputHeight, settings.Fps, settings.BitrateKbps,
                        settings.DurationSeconds, (int)animationType, (int)settings.KenBurnsDirection,
                        settings.KenBurnsZoomFactor, settings.ParallaxIntensity, settings.UseHardwareAcceleration);

                    cachedPath = await _animationCache.GetCachedClipAsync(job.SourceImagePath, fingerprint, cts.Token);
                }

                string outputPath;
                if (cachedPath != null)
                {
                    outputPath = cachedPath;
                    job.StatusMessage = "Clip recuperato dalla cache!";
                }
                else
                {
                    var progress = new Progress<double>(pct =>
                    {
                        job.Progress = pct;
                        if (pct > 80) job.Status = AnimationStatus.Encoding;
                    });

                    outputPath = await engine.AnimateAsync(job, progress, cts.Token);

                    if (_animationCache != null && fingerprint != null)
                        await _animationCache.StoreCachedClipAsync(job.SourceImagePath, fingerprint, outputPath, cts.Token);
                }

                job.OutputPath = outputPath;
                job.Status = AnimationStatus.Completed;
                job.Progress = 100;
                job.CompletedAt = DateTime.UtcNow;
                job.StatusMessage = "Animazione completata!";

                if (File.Exists(outputPath))
                    job.FileSizeBytes = new FileInfo(outputPath).Length;
            }
            catch (OperationCanceledException)
            {
                job.Status = AnimationStatus.Cancelled;
                job.StatusMessage = "Animazione annullata";
            }
            catch (Exception ex)
            {
                job.Status = AnimationStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.StatusMessage = "Errore durante l'animazione";
            }
            finally
            {
                _cancellationTokens.TryRemove(job.Id, out _);
            }
        }, cts.Token);

        return job;
    }

    public Task<AnimationJob?> GetJobAsync(Guid id)
    {
        _jobs.TryGetValue(id, out var job);
        return Task.FromResult(job);
    }

    public Task<List<AnimationJob>> GetAllJobsAsync()
    {
        return Task.FromResult(_jobs.Values.OrderByDescending(j => j.CreatedAt).ToList());
    }

    public Task<bool> CancelJobAsync(Guid id)
    {
        if (_cancellationTokens.TryGetValue(id, out var cts))
        {
            cts.Cancel();
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> DeleteJobAsync(Guid id)
    {
        if (!_jobs.TryRemove(id, out var job))
            return Task.FromResult(false);

        _cancellationTokens.TryRemove(id, out var cts);
        cts?.Cancel();

        var jobDir = Path.Combine(_storagePath, id.ToString());
        if (Directory.Exists(jobDir))
        {
            try { Directory.Delete(jobDir, true); } catch { }
        }

        if (job.OutputPath != null && File.Exists(job.OutputPath))
        {
            try { File.Delete(job.OutputPath); } catch { }
        }

        return Task.FromResult(true);
    }
}
