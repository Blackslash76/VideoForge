using Microsoft.Extensions.DependencyInjection;
using VideoForge.Core.Interfaces;
using VideoForge.Infrastructure.Services;
using VideoForge.Infrastructure.Services.Animations;

namespace VideoForge.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoForgeInfrastructure(this IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IFfmpegService, FfmpegService>();
        services.AddSingleton<IImageValidationService, ImageValidationService>();
        services.AddSingleton<IVideoGenerationService, VideoGenerationService>();

        // Depth estimation (MiDaS via Python/PyTorch)
        services.AddSingleton<IDepthEstimationService, DepthEstimationService>();

        // Animation engines
        services.AddSingleton<IAnimationEngine, KenBurnsEngine>();
        services.AddSingleton<IAnimationEngine, ParallaxEngine>();
        services.AddSingleton<IAnimationEngine, CinemagraphEngine>();
        services.AddSingleton<IAnimationEngine, AiMotionEngine>();
        services.AddSingleton<IAnimationEngine, KenBurnsParallaxEngine>();

        // Animation cache (library di clip pre-renderizzati)
        services.AddSingleton<IAnimationCache, AnimationCacheService>();

        // Orchestrator
        services.AddSingleton<IAnimationOrchestrator, AnimationOrchestrator>();

        // Montage service
        services.AddSingleton<IMontageService, MontageService>();

        return services;
    }
}
