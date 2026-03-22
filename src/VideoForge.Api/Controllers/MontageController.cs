using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using VideoForge.Api.DTOs;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;
using VideoForge.Infrastructure.Services;

namespace VideoForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MontageController : ControllerBase
{
    private readonly IMontageService _montageService;
    private readonly IImageValidationService _imageValidation;

    public MontageController(IMontageService montageService, IImageValidationService imageValidation)
    {
        _montageService = montageService;
        _imageValidation = imageValidation;
    }

    /// <summary>
    /// Crea un montaggio video: upload batch di foto + tracce audio.
    /// Le foto vengono animate, assemblate con transizioni e sincronizzate con la musica.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(1_000_000_000)] // 1 GB
    public async Task<ActionResult<MontageResponse>> CreateMontage(
        [FromForm] List<IFormFile> photos,
        [FromForm] List<IFormFile>? audio,
        [FromForm] string? name,
        [FromForm] MontageStyle style = MontageStyle.Cinematic,
        [FromForm] int outputWidth = 1920,
        [FromForm] int outputHeight = 1080,
        [FromForm] int fps = 30,
        [FromForm] int bitrateKbps = 12000,
        [FromForm] bool useHardwareAcceleration = true,
        [FromForm] double photoDuration = 5.0,
        [FromForm] string? introText = null,
        [FromForm] string? outroText = null,
        [FromForm] double audioFadeIn = 2.0,
        [FromForm] double audioFadeOut = 3.0,
        // Cinematic settings
        [FromForm] ColorGrade colorGrade = ColorGrade.WarmMemory,
        [FromForm] bool vignette = true,
        [FromForm] bool filmGrain = true,
        [FromForm] double filmGrainIntensity = 0.15,
        [FromForm] bool letterbox = false,
        [FromForm] ParticleEffect particleEffect = ParticleEffect.None,
        [FromForm] bool memoryFlash = true,
        // Intro/Outro cinematico
        [FromForm] string? introTitle = null,
        [FromForm] string? introSubtitle = null,
        [FromForm] string? introDate = null,
        [FromForm] double introDuration = 6.0,
        [FromForm] string? outroTitle = null,
        [FromForm] string? outroMessage = null,
        [FromForm] string? outroCredits = null,
        [FromForm] double outroDuration = 8.0,
        // Didascalie (JSON array: [{"index":0,"text":"Prima foto"},{"index":2,"text":"Al mare"}])
        [FromForm] string? captions = null)
    {
        if (photos == null || photos.Count == 0)
            return BadRequest(new { error = "Nessuna foto fornita" });

        if (photos.Count > 200)
            return BadRequest(new { error = "Massimo 200 foto per montaggio" });

        // Validate images
        foreach (var photo in photos)
        {
            if (!_imageValidation.IsSupportedFormat(photo.FileName))
                return BadRequest(new { error = $"Formato non supportato: {photo.FileName}" });
        }

        var project = new MontageProject
        {
            OutputWidth = Math.Clamp(outputWidth, 640, 3840),
            OutputHeight = Math.Clamp(outputHeight, 480, 2160),
            Fps = Math.Clamp(fps, 24, 60),
            BitrateKbps = Math.Clamp(bitrateKbps, 4000, 50000),
            UseHardwareAcceleration = useHardwareAcceleration,
            IntroText = introText,
            OutroText = outroText,
            Cinematic = new CinematicSettings
            {
                ColorGrade = colorGrade,
                Vignette = vignette,
                VignetteIntensity = 0.4,
                FilmGrain = filmGrain,
                FilmGrainIntensity = Math.Clamp(filmGrainIntensity, 0, 0.5),
                Letterbox = letterbox,
                ParticleEffect = particleEffect,
                MemoryFlash = memoryFlash,
                CinematicIntro = !string.IsNullOrWhiteSpace(introTitle),
                IntroTitle = introTitle,
                IntroSubtitle = introSubtitle,
                IntroDate = introDate,
                IntroDuration = Math.Clamp(introDuration, 3, 10),
                CinematicOutro = !string.IsNullOrWhiteSpace(outroTitle),
                OutroTitle = outroTitle,
                OutroMessage = outroMessage,
                OutroCredits = outroCredits,
                OutroDuration = Math.Clamp(outroDuration, 4, 15)
            }
        };

        // Storage
        var storageService = (MontageService)_montageService;
        var projectDir = storageService.GetProjectStoragePath(project.Id);

        // Save and validate photos
        for (int i = 0; i < photos.Count; i++)
        {
            var file = photos[i];
            var filePath = Path.Combine(projectDir, $"{i:D3}_{file.FileName}");

            using (var fs = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fs);
            }

            // Get dimensions
            int w = 1920, h = 1080;
            try
            {
                using var imgStream = file.OpenReadStream();
                using var img = await Image.LoadAsync(imgStream);
                w = img.Width;
                h = img.Height;
            }
            catch { }

            project.Photos.Add(new MontagePhoto
            {
                Order = i,
                FileName = file.FileName,
                FilePath = filePath,
                Width = w,
                Height = h,
                DisplayDuration = photoDuration
            });
        }

        // Parse and apply captions
        if (!string.IsNullOrWhiteSpace(captions))
        {
            try
            {
                var captionList = System.Text.Json.JsonSerializer.Deserialize<List<CaptionDto>>(captions);
                if (captionList != null)
                {
                    foreach (var c in captionList)
                    {
                        if (c.Index >= 0 && c.Index < project.Photos.Count && !string.IsNullOrWhiteSpace(c.Text))
                        {
                            project.Photos[c.Index].Caption = new PhotoCaption
                            {
                                Text = c.Text,
                                Position = c.Position ?? "bottom",
                                FontSize = c.FontSize > 0 ? c.FontSize : 42
                            };
                        }
                    }
                }
            }
            catch { /* ignore invalid JSON */ }
        }

        // Save audio tracks
        if (audio != null)
        {
            var audioDir = Path.Combine(projectDir, "audio");
            Directory.CreateDirectory(audioDir);

            for (int i = 0; i < audio.Count; i++)
            {
                var file = audio[i];
                var filePath = Path.Combine(audioDir, $"{i:D2}_{file.FileName}");

                using (var fs = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fs);
                }

                project.AudioTracks.Add(new AudioTrack
                {
                    Order = i,
                    FileName = file.FileName,
                    FilePath = filePath,
                    FadeInSeconds = audioFadeIn,
                    FadeOutSeconds = audioFadeOut
                });
            }
        }

        var result = await _montageService.CreateProjectAsync(name ?? "", style, project);
        return CreatedAtAction(nameof(GetMontage), new { id = result.Id }, MontageResponse.FromProject(result));
    }

    /// <summary>
    /// Lista tutti i montaggi
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<MontageResponse>>> GetAllMontages()
    {
        var projects = await _montageService.GetAllProjectsAsync();
        return Ok(projects.Select(MontageResponse.FromProject).ToList());
    }

    /// <summary>
    /// Stato montaggio con progresso dettagliato
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MontageResponse>> GetMontage(Guid id)
    {
        var project = await _montageService.GetProjectAsync(id);
        if (project == null)
            return NotFound(new { error = "Montaggio non trovato" });

        return Ok(MontageResponse.FromProject(project));
    }

    /// <summary>
    /// Scarica il video montato finale
    /// </summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadMontage(Guid id)
    {
        var project = await _montageService.GetProjectAsync(id);
        if (project == null)
            return NotFound(new { error = "Montaggio non trovato" });

        if (project.Status != MontageStatus.Completed || project.OutputPath == null)
            return BadRequest(new { error = "Video non ancora pronto", status = project.Status.ToString(), progress = project.Progress });

        if (!System.IO.File.Exists(project.OutputPath))
            return NotFound(new { error = "File non trovato sul disco" });

        var stream = new FileStream(project.OutputPath, FileMode.Open, FileAccess.Read);
        return File(stream, "video/mp4", $"{project.Name}.mp4");
    }

    /// <summary>
    /// Annulla/elimina un montaggio
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteMontage(Guid id)
    {
        var project = await _montageService.GetProjectAsync(id);
        if (project == null)
            return NotFound(new { error = "Montaggio non trovato" });

        if (project.Status == MontageStatus.AnimatingPhotos || project.Status == MontageStatus.AssemblingVideo
            || project.Status == MontageStatus.MixingAudio || project.Status == MontageStatus.FinalEncoding)
        {
            await _montageService.CancelProjectAsync(id);
        }

        await _montageService.DeleteProjectAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Stili di montaggio disponibili con descrizione
    /// </summary>
    [HttpGet("styles")]
    public ActionResult GetStyles()
    {
        return Ok(new[]
        {
            new { style = "Cinematic", value = 0, description = "Lento ed emotivo. KenBurns + Parallax alternati, crossfade lunghi. Perfetto per compleanni e ricordi.", photoDuration = 5.0, transitionDuration = 2.0 },
            new { style = "Storytelling", value = 1, description = "Ritmo medio con mix di effetti e transizioni variegate. Racconta una storia.", photoDuration = 4.0, transitionDuration = 1.5 },
            new { style = "Energetic", value = 2, description = "Dinamico e divertente! Tagli rapidi, slide e zoom energici.", photoDuration = 2.5, transitionDuration = 0.8 },
            new { style = "Elegant", value = 3, description = "Minimal e sofisticato. Movimenti lenti, dissolve morbidi, molto raffinato.", photoDuration = 6.0, transitionDuration = 2.5 },
        });
    }
}
