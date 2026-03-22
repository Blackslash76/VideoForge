using VideoForge.Core.Models;

namespace VideoForge.Core.Interfaces;

public interface IDepthEstimationService
{
    Task<DepthMap> EstimateDepthAsync(string imagePath, CancellationToken cancellationToken = default);
    bool IsAvailable();
}
