using VideoForge.Core.Enums;
using VideoForge.Core.Models;

namespace VideoForge.Core.Interfaces;

public interface IMontageService
{
    Task<MontageProject> CreateProjectAsync(string name, MontageStyle style, MontageProject settings);
    Task<MontageProject?> GetProjectAsync(Guid id);
    Task<List<MontageProject>> GetAllProjectsAsync();
    Task<bool> CancelProjectAsync(Guid id);
    Task<bool> DeleteProjectAsync(Guid id);
}
