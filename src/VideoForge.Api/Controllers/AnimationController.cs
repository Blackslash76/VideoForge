using Microsoft.AspNetCore.Mvc;
using VideoForge.Api.DTOs;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnimationController : ControllerBase
{
    private readonly IAnimationOrchestrator _orchestrator;
    private readonly IImageValidationService _imageValidation;

    public AnimationController(IAnimationOrchestrator orchestrator, IImageValidationService imageValidation)
    {
        _orchestrator = orchestrator;
        _imageValidation = imageValidation;
    }

    /// <summary>
    /// Anima una foto statica - la foto prende vita!
    /// Supporta: KenBurns, Parallax, Cinemagraph, AI Motion, KenBurns+Parallax
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(100_000_000)] // 100 MB
    public async Task<ActionResult<AnimationJobResponse>> AnimateImage(
        [FromForm] IFormFile image,
        [FromForm] AnimationType animationType = AnimationType.KenBurns,
        [FromForm] double durationSeconds = 5.0,
        [FromForm] int fps = 30,
        [FromForm] int outputWidth = 1920,
        [FromForm] int outputHeight = 1080,
        [FromForm] int bitrateKbps = 8000,
        [FromForm] bool useHardwareAcceleration = true,
        [FromForm] bool loop = false,
        [FromForm] KenBurnsDirection kenBurnsDirection = KenBurnsDirection.ZoomIn,
        [FromForm] double kenBurnsZoomFactor = 1.3,
        [FromForm] double parallaxIntensity = 0.5,
        [FromForm] string parallaxMotion = "horizontal",
        [FromForm] double cinemagraphMotionIntensity = 0.5,
        [FromForm] string cinemagraphMotionType = "flow",
        [FromForm] double aiMotionStrength = 0.7,
        [FromForm] int aiMotionSeed = -1,
        [FromForm] string? aiMotionPrompt = null,
        [FromForm] IFormFile? cinemagraphMask = null)
    {
        if (image == null || image.Length == 0)
            return BadRequest(new { error = "Nessuna immagine fornita" });

        if (!_imageValidation.IsSupportedFormat(image.FileName))
            return BadRequest(new { error = $"Formato non supportato: {image.FileName}" });

        var settings = new AnimationSettings
        {
            DurationSeconds = Math.Clamp(durationSeconds, 1, 30),
            Fps = Math.Clamp(fps, 15, 60),
            OutputWidth = Math.Clamp(outputWidth, 320, 3840),
            OutputHeight = Math.Clamp(outputHeight, 240, 2160),
            BitrateKbps = Math.Clamp(bitrateKbps, 1000, 50000),
            UseHardwareAcceleration = useHardwareAcceleration,
            Loop = loop,
            KenBurnsDirection = kenBurnsDirection,
            KenBurnsZoomFactor = Math.Clamp(kenBurnsZoomFactor, 1.05, 2.0),
            ParallaxIntensity = Math.Clamp(parallaxIntensity, 0.1, 1.0),
            ParallaxMotion = parallaxMotion,
            CinemagraphMotionIntensity = Math.Clamp(cinemagraphMotionIntensity, 0.1, 1.0),
            CinemagraphMotionType = cinemagraphMotionType,
            AiMotionStrength = Math.Clamp(aiMotionStrength, 0.1, 1.0),
            AiMotionSeed = aiMotionSeed,
            AiMotionPrompt = aiMotionPrompt
        };

        // Save cinemagraph mask if provided
        if (cinemagraphMask != null && animationType == AnimationType.Cinemagraph)
        {
            var maskDir = Path.Combine(Path.GetTempPath(), "VideoForge", "masks");
            Directory.CreateDirectory(maskDir);
            var maskPath = Path.Combine(maskDir, $"{Guid.NewGuid()}_{cinemagraphMask.FileName}");
            using var maskStream = new FileStream(maskPath, FileMode.Create);
            await cinemagraphMask.CopyToAsync(maskStream);
            settings.CinemagraphMaskPath = maskPath;
        }

        using var stream = image.OpenReadStream();
        var job = await _orchestrator.CreateJobAsync(stream, image.FileName, settings, animationType);

        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, AnimationJobResponse.FromJob(job));
    }

    /// <summary>
    /// Lista tutte le animazioni
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<AnimationJobResponse>>> GetAllJobs()
    {
        var jobs = await _orchestrator.GetAllJobsAsync();
        return Ok(jobs.Select(AnimationJobResponse.FromJob).ToList());
    }

    /// <summary>
    /// Stato di un'animazione (progresso in tempo reale)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AnimationJobResponse>> GetJob(Guid id)
    {
        var job = await _orchestrator.GetJobAsync(id);
        if (job == null)
            return NotFound(new { error = "Animazione non trovata" });

        return Ok(AnimationJobResponse.FromJob(job));
    }

    /// <summary>
    /// Scarica il video animato
    /// </summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadAnimation(Guid id)
    {
        var job = await _orchestrator.GetJobAsync(id);
        if (job == null)
            return NotFound(new { error = "Animazione non trovata" });

        if (job.Status != AnimationStatus.Completed || job.OutputPath == null)
            return BadRequest(new { error = "Video non ancora pronto", status = job.Status.ToString(), progress = job.Progress });

        if (!System.IO.File.Exists(job.OutputPath))
            return NotFound(new { error = "File video non trovato" });

        var stream = new FileStream(job.OutputPath, FileMode.Open, FileAccess.Read);
        var fileName = $"{Path.GetFileNameWithoutExtension(job.SourceFileName)}_{job.AnimationType}.mp4";
        return File(stream, "video/mp4", fileName);
    }

    /// <summary>
    /// Scarica la depth map generata (se disponibile)
    /// </summary>
    [HttpGet("{id:guid}/depthmap")]
    public async Task<IActionResult> DownloadDepthMap(Guid id)
    {
        var job = await _orchestrator.GetJobAsync(id);
        if (job == null)
            return NotFound(new { error = "Animazione non trovata" });

        if (job.DepthMapPath == null || !System.IO.File.Exists(job.DepthMapPath))
            return NotFound(new { error = "Depth map non disponibile per questa animazione" });

        var stream = new FileStream(job.DepthMapPath, FileMode.Open, FileAccess.Read);
        return File(stream, "image/png", $"{Path.GetFileNameWithoutExtension(job.SourceFileName)}_depth.png");
    }

    /// <summary>
    /// Annulla un'animazione in corso
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteJob(Guid id)
    {
        var job = await _orchestrator.GetJobAsync(id);
        if (job == null)
            return NotFound(new { error = "Animazione non trovata" });

        if (job.Status == AnimationStatus.Animating || job.Status == AnimationStatus.Encoding)
            await _orchestrator.CancelJobAsync(id);

        await _orchestrator.DeleteJobAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Tipi di animazione disponibili e loro stato
    /// </summary>
    [HttpGet("types")]
    public ActionResult GetAnimationTypes()
    {
        return Ok(new[]
        {
            new { type = "KenBurns", value = 0, description = "Pan e zoom cinematografico", requiresGpu = false, requiresAi = false },
            new { type = "Parallax", value = 1, description = "Effetto profondità 3D con depth map", requiresGpu = false, requiresAi = true },
            new { type = "Cinemagraph", value = 2, description = "Parti dell'immagine si muovono in loop", requiresGpu = false, requiresAi = false },
            new { type = "AiMotion", value = 3, description = "Animazione AI completa (Stable Video Diffusion)", requiresGpu = true, requiresAi = true },
            new { type = "KenBurnsParallax", value = 4, description = "Ken Burns + Parallax combinati", requiresGpu = false, requiresAi = true },
        });
    }
}
