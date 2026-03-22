using Microsoft.AspNetCore.Mvc;
using VideoForge.Api.DTOs;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;
using VideoForge.Infrastructure.Services;

namespace VideoForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideoController : ControllerBase
{
    private readonly IVideoGenerationService _videoService;
    private readonly IImageValidationService _imageValidation;
    private readonly IFfmpegService _ffmpegService;

    public VideoController(
        IVideoGenerationService videoService,
        IImageValidationService imageValidation,
        IFfmpegService ffmpegService)
    {
        _videoService = videoService;
        _imageValidation = imageValidation;
        _ffmpegService = ffmpegService;
    }

    /// <summary>
    /// Upload immagini e crea un nuovo progetto video
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(500_000_000)] // 500 MB
    public async Task<ActionResult<VideoProjectResponse>> CreateVideo(
        [FromForm] List<IFormFile> images,
        [FromForm] string? name,
        [FromForm] VideoResolution resolution = VideoResolution.FullHD_1080p,
        [FromForm] int fps = 30,
        [FromForm] int bitrateKbps = 8000,
        [FromForm] bool useHardwareAcceleration = true,
        [FromForm] TransitionType defaultTransition = TransitionType.Fade,
        [FromForm] double displayDuration = 3.0,
        [FromForm] double transitionDuration = 1.0)
    {
        if (images == null || images.Count == 0)
            return BadRequest(new { error = "Nessuna immagine fornita" });

        if (images.Count > 100)
            return BadRequest(new { error = "Massimo 100 immagini per progetto" });

        // Validate all images first
        foreach (var file in images)
        {
            if (!_imageValidation.IsSupportedFormat(file.FileName))
                return BadRequest(new { error = $"Formato non supportato: {file.FileName}. Formati accettati: {string.Join(", ", _imageValidation.SupportedExtensions)}" });
        }

        var settings = new VideoSettings
        {
            Resolution = resolution,
            Fps = fps,
            BitrateKbps = bitrateKbps,
            UseHardwareAcceleration = useHardwareAcceleration,
            DefaultTransition = defaultTransition,
            DefaultDisplayDuration = displayDuration,
            DefaultTransitionDuration = transitionDuration
        };

        var project = await _videoService.CreateProjectAsync(name ?? "", settings);
        var storageService = (VideoGenerationService)_videoService;
        var projectDir = storageService.GetProjectStoragePath(project.Id);

        var imageEntries = new List<ImageEntry>();
        for (int i = 0; i < images.Count; i++)
        {
            var file = images[i];
            using var stream = file.OpenReadStream();

            var entry = await _imageValidation.ValidateAndProcessAsync(stream, file.FileName, i);

            // Save file to disk
            var filePath = Path.Combine(projectDir, $"{i:D3}_{file.FileName}");
            using var fileStream = new FileStream(filePath, FileMode.Create);
            stream.Position = 0;
            // Re-open stream since validation consumed it
            using var saveStream = file.OpenReadStream();
            await saveStream.CopyToAsync(fileStream);

            entry.FilePath = filePath;
            entry.DisplayDuration = displayDuration;
            entry.Transition = defaultTransition;
            entry.TransitionDuration = transitionDuration;
            imageEntries.Add(entry);
        }

        await _videoService.AddImagesAsync(project.Id, imageEntries);

        // Start video generation in background
        _ = Task.Run(() => _videoService.GenerateVideoAsync(project.Id));

        return CreatedAtAction(nameof(GetProject), new { id = project.Id }, VideoProjectResponse.FromProject(project));
    }

    /// <summary>
    /// Lista tutti i progetti video
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<VideoProjectResponse>>> GetAllProjects()
    {
        var projects = await _videoService.GetAllProjectsAsync();
        return Ok(projects.Select(VideoProjectResponse.FromProject).ToList());
    }

    /// <summary>
    /// Stato di un progetto video
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VideoProjectResponse>> GetProject(Guid id)
    {
        var project = await _videoService.GetProjectAsync(id);
        if (project == null)
            return NotFound(new { error = "Progetto non trovato" });

        return Ok(VideoProjectResponse.FromProject(project));
    }

    /// <summary>
    /// Scarica il video generato
    /// </summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadVideo(Guid id)
    {
        var project = await _videoService.GetProjectAsync(id);
        if (project == null)
            return NotFound(new { error = "Progetto non trovato" });

        if (project.Status != ProjectStatus.Completed || project.OutputPath == null)
            return BadRequest(new { error = "Video non ancora pronto", status = project.Status.ToString(), progress = project.Progress });

        if (!System.IO.File.Exists(project.OutputPath))
            return NotFound(new { error = "File video non trovato sul disco" });

        var stream = new FileStream(project.OutputPath, FileMode.Open, FileAccess.Read);
        return File(stream, "video/mp4", $"{project.Name}.mp4");
    }

    /// <summary>
    /// Annulla/elimina un progetto video
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteProject(Guid id)
    {
        var project = await _videoService.GetProjectAsync(id);
        if (project == null)
            return NotFound(new { error = "Progetto non trovato" });

        if (project.Status == ProjectStatus.Processing || project.Status == ProjectStatus.Encoding)
        {
            await _videoService.CancelProjectAsync(id);
        }

        await _videoService.DeleteProjectAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Informazioni sistema (GPU, FFmpeg, NVENC)
    /// </summary>
    [HttpGet("system/info")]
    public async Task<ActionResult<SystemInfoResponse>> GetSystemInfo()
    {
        var gpuInfo = await _ffmpegService.GetSystemInfoAsync();
        return Ok(new SystemInfoResponse
        {
            GpuName = gpuInfo.Name,
            Driver = gpuInfo.Driver,
            NvencAvailable = gpuInfo.NvencAvailable,
            MemoryTotal = FormatBytes(gpuInfo.MemoryTotalBytes),
            MemoryFree = FormatBytes(gpuInfo.MemoryFreeBytes),
            FfmpegVersion = gpuInfo.FfmpegVersion,
            SupportedEncoders = gpuInfo.SupportedEncoders
        });
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
