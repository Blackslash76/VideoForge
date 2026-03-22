using System.Collections.Concurrent;
using VideoForge.Core.Enums;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services;

public class VideoGenerationService : IVideoGenerationService
{
    private readonly ConcurrentDictionary<Guid, VideoProject> _projects = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationTokens = new();
    private readonly IFfmpegService _ffmpegService;
    private readonly IImageValidationService _imageValidationService;
    private readonly string _storagePath;

    public VideoGenerationService(IFfmpegService ffmpegService, IImageValidationService imageValidationService)
    {
        _ffmpegService = ffmpegService;
        _imageValidationService = imageValidationService;
        _storagePath = Path.Combine(Path.GetTempPath(), "VideoForge");
        Directory.CreateDirectory(_storagePath);
    }

    public Task<VideoProject> CreateProjectAsync(string name, VideoSettings settings)
    {
        var project = new VideoProject
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Progetto_{DateTime.UtcNow:yyyyMMdd_HHmmss}" : name,
            Settings = settings
        };

        var projectDir = Path.Combine(_storagePath, project.Id.ToString());
        Directory.CreateDirectory(projectDir);

        _projects[project.Id] = project;
        return Task.FromResult(project);
    }

    public Task<VideoProject?> GetProjectAsync(Guid id)
    {
        _projects.TryGetValue(id, out var project);
        return Task.FromResult(project);
    }

    public Task<List<VideoProject>> GetAllProjectsAsync()
    {
        return Task.FromResult(_projects.Values.OrderByDescending(p => p.CreatedAt).ToList());
    }

    public Task<VideoProject> AddImagesAsync(Guid projectId, List<ImageEntry> images)
    {
        if (!_projects.TryGetValue(projectId, out var project))
            throw new KeyNotFoundException($"Progetto {projectId} non trovato");

        project.Images.AddRange(images);
        return Task.FromResult(project);
    }

    public async Task GenerateVideoAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!_projects.TryGetValue(projectId, out var project))
            throw new KeyNotFoundException($"Progetto {projectId} non trovato");

        if (project.Images.Count == 0)
            throw new InvalidOperationException("Nessuna immagine nel progetto");

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokens[projectId] = cts;

        try
        {
            project.Status = ProjectStatus.Processing;
            project.Progress = 0;

            var progress = new Progress<double>(pct =>
            {
                project.Progress = pct;
                if (pct > 50) project.Status = ProjectStatus.Encoding;
            });

            project.Status = ProjectStatus.Encoding;
            var outputPath = await _ffmpegService.GenerateVideoAsync(project, progress, cts.Token);

            project.OutputPath = outputPath;
            project.Status = ProjectStatus.Completed;
            project.Progress = 100;
            project.CompletedAt = DateTime.UtcNow;

            if (File.Exists(outputPath))
            {
                project.FileSizeBytes = new FileInfo(outputPath).Length;
            }
        }
        catch (OperationCanceledException)
        {
            project.Status = ProjectStatus.Cancelled;
            project.ErrorMessage = "Generazione annullata dall'utente";
        }
        catch (Exception ex)
        {
            project.Status = ProjectStatus.Failed;
            project.ErrorMessage = ex.Message;
        }
        finally
        {
            _cancellationTokens.TryRemove(projectId, out _);
        }
    }

    public Task<bool> CancelProjectAsync(Guid projectId)
    {
        if (_cancellationTokens.TryGetValue(projectId, out var cts))
        {
            cts.Cancel();
            return Task.FromResult(true);
        }

        if (_projects.TryGetValue(projectId, out var project))
        {
            project.Status = ProjectStatus.Cancelled;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<bool> DeleteProjectAsync(Guid projectId)
    {
        if (!_projects.TryRemove(projectId, out var project))
            return Task.FromResult(false);

        _cancellationTokens.TryRemove(projectId, out var cts);
        cts?.Cancel();

        var projectDir = Path.Combine(_storagePath, projectId.ToString());
        if (Directory.Exists(projectDir))
        {
            try { Directory.Delete(projectDir, true); }
            catch { /* best effort cleanup */ }
        }

        if (project.OutputPath != null && File.Exists(project.OutputPath))
        {
            try { File.Delete(project.OutputPath); }
            catch { /* best effort cleanup */ }
        }

        return Task.FromResult(true);
    }

    public string GetProjectStoragePath(Guid projectId)
    {
        return Path.Combine(_storagePath, projectId.ToString());
    }
}
