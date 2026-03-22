using VideoForge.Core.Models;

namespace VideoForge.Core.Interfaces;

public interface IImageValidationService
{
    Task<ImageEntry> ValidateAndProcessAsync(Stream imageStream, string fileName, int order);
    bool IsSupportedFormat(string fileName);
    string[] SupportedExtensions { get; }
}
