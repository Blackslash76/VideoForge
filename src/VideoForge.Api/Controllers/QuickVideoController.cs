using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using VideoForge.Api.DTOs;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;
using VideoForge.Infrastructure.Services;

namespace VideoForge.Api.Controllers;

[ApiController]
[Route("api/quick")]
public class QuickVideoController : ControllerBase
{
    private readonly IMontageService _montageService;
    private readonly IImageValidationService _imageValidation;

    public QuickVideoController(IMontageService montageService, IImageValidationService imageValidation)
    {
        _montageService = montageService;
        _imageValidation = imageValidation;
    }

    /// <summary>
    /// One-click: carica foto + musica e ottieni un video cinematico emozionale.
    /// Zero configurazione. Tutto automatico.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(1_500_000_000)]
    public async Task<ActionResult<MontageResponse>> CreateQuickVideo(
        [FromForm] List<IFormFile> photos,
        [FromForm] List<IFormFile>? audio,
        [FromForm] string? dedicatario = null,
        [FromForm] string? occasione = null,
        [FromForm] string? messaggio = null,
        [FromForm] string? da = null)
    {
        if (photos == null || photos.Count == 0)
            return BadRequest(new { error = "Carica almeno una foto!" });

        // Validate
        foreach (var p in photos)
            if (!_imageValidation.IsSupportedFormat(p.FileName))
                return BadRequest(new { error = $"Formato non supportato: {p.FileName}" });

        // Smart defaults basati sul numero di foto
        var photoCount = photos.Count;
        var photoDuration = photoCount switch
        {
            <= 10 => 6.0,   // poche foto = più tempo per ognuna
            <= 25 => 5.0,
            <= 50 => 4.5,
            <= 80 => 4.0,
            _ => 3.5
        };

        // Titolo automatico
        var nomi = dedicatario ?? "i nostri ragazzi";
        var evento = occasione ?? "Prima Comunione";
        var videoName = $"{evento} - {nomi}";

        var project = new MontageProject
        {
            OutputWidth = 1920,
            OutputHeight = 1080,
            Fps = 30,
            BitrateKbps = 12000,
            UseHardwareAcceleration = true,
            Cinematic = new CinematicSettings
            {
                // Color grading caldo emozionale
                ColorGrade = ColorGrade.WarmMemory,

                // Effetti cinema
                Vignette = true,
                VignetteIntensity = 0.35,
                FilmGrain = true,
                FilmGrainIntensity = 0.12,
                Letterbox = false,

                // Flash memoria sottile
                MemoryFlash = true,
                MemoryFlashDuration = 0.12,

                // Particelle: scintille leggere
                ParticleEffect = ParticleEffect.Sparkles,
                ParticleIntensity = 0.4,

                // Intro
                CinematicIntro = true,
                IntroTitle = evento,
                IntroSubtitle = nomi,
                IntroDate = DateTime.Now.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("it-IT")),
                IntroDuration = 6.0,
                IntroFadeIn = 2.0,

                // Outro
                CinematicOutro = true,
                OutroTitle = messaggio ?? $"Auguri {nomi}",
                OutroMessage = "Ogni momento insieme e un dono prezioso",
                OutroCredits = da != null ? $"Con tutto l'amore, {da}" : "Con tutto il nostro amore",
                OutroDuration = 8.0
            }
        };

        // Storage
        var storageService = (MontageService)_montageService;
        var projectDir = storageService.GetProjectStoragePath(project.Id);

        // Save photos
        for (int i = 0; i < photos.Count; i++)
        {
            var file = photos[i];
            var filePath = Path.Combine(projectDir, $"{i:D3}_{file.FileName}");
            using (var fs = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(fs);

            int w = 1920, h = 1080;
            try
            {
                using var imgStream = file.OpenReadStream();
                using var img = await Image.LoadAsync(imgStream);
                w = img.Width; h = img.Height;
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

        // Save audio
        if (audio != null)
        {
            var audioDir = Path.Combine(projectDir, "audio");
            Directory.CreateDirectory(audioDir);
            for (int i = 0; i < audio.Count; i++)
            {
                var file = audio[i];
                var filePath = Path.Combine(audioDir, $"{i:D2}_{file.FileName}");
                using (var fs = new FileStream(filePath, FileMode.Create))
                    await file.CopyToAsync(fs);

                project.AudioTracks.Add(new AudioTrack
                {
                    Order = i,
                    FileName = file.FileName,
                    FilePath = filePath,
                    FadeInSeconds = 2.5,
                    FadeOutSeconds = 4.0
                });
            }
        }

        var result = await _montageService.CreateProjectAsync(videoName, MontageStyle.Cinematic, project);
        return CreatedAtAction("GetMontage", "Montage", new { id = result.Id }, MontageResponse.FromProject(result));
    }
}
