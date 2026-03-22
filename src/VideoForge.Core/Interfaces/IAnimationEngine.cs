using VideoForge.Core.Enums;
using VideoForge.Core.Models;

namespace VideoForge.Core.Interfaces;

public interface IAnimationEngine
{
    AnimationType SupportedType { get; }
    Task<string> AnimateAsync(AnimationJob job, IProgress<double> progress, CancellationToken cancellationToken = default);
    bool IsAvailable();
}
