using VideoForge.Core.Models;

namespace VideoForge.Core.Interfaces;

public interface IVideoGenerationService
{
    Task<VideoProject> CreateProjectAsync(string name, VideoSettings settings);
    Task<VideoProject?> GetProjectAsync(Guid id);
    Task<List<VideoProject>> GetAllProjectsAsync();
    Task<VideoProject> AddImagesAsync(Guid projectId, List<ImageEntry> images);
    Task GenerateVideoAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<bool> CancelProjectAsync(Guid projectId);
    Task<bool> DeleteProjectAsync(Guid projectId);
}
