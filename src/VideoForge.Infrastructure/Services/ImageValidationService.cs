using SixLabors.ImageSharp;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services;

public class ImageValidationService : IImageValidationService
{
    private static readonly string[] _supportedExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };

    public string[] SupportedExtensions => _supportedExtensions;

    public bool IsSupportedFormat(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return _supportedExtensions.Contains(extension);
    }

    public async Task<ImageEntry> ValidateAndProcessAsync(Stream imageStream, string fileName, int order)
    {
        if (!IsSupportedFormat(fileName))
            throw new ArgumentException($"Formato non supportato: {Path.GetExtension(fileName)}");

        using var image = await Image.LoadAsync(imageStream);

        if (image.Width < 64 || image.Height < 64)
            throw new ArgumentException($"Immagine troppo piccola: {image.Width}x{image.Height} (minimo 64x64)");

        if (image.Width > 7680 || image.Height > 4320)
            throw new ArgumentException($"Immagine troppo grande: {image.Width}x{image.Height} (massimo 7680x4320)");

        return new ImageEntry
        {
            Order = order,
            FileName = fileName,
            Width = image.Width,
            Height = image.Height
        };
    }
}
