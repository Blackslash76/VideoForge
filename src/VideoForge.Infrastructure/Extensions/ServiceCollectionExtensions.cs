using Microsoft.Extensions.DependencyInjection;
using VideoForge.Core.Interfaces;
using VideoForge.Infrastructure.Services;

namespace VideoForge.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoForgeInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IFfmpegService, FfmpegService>();
        services.AddSingleton<IImageValidationService, ImageValidationService>();
        services.AddSingleton<IVideoGenerationService, VideoGenerationService>();
        return services;
    }
}
