using VideoForge.Core.Models;

namespace VideoForge.Core.Interfaces;

public interface IAnimationOrchestrator
{
    Task<AnimationJob> CreateJobAsync(Stream imageStream, string fileName, AnimationSettings settings, Core.Enums.AnimationType animationType);
    Task<AnimationJob?> GetJobAsync(Guid id);
    Task<List<AnimationJob>> GetAllJobsAsync();
    Task<bool> CancelJobAsync(Guid id);
    Task<bool> DeleteJobAsync(Guid id);
}
