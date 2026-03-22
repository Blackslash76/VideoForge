using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VideoForge.Core.Interfaces;

namespace VideoForge.Infrastructure.Services;

public class AnimationCacheService : IAnimationCache
{
    private readonly string _cacheDir;
    private readonly ILogger<AnimationCacheService> _logger;
    private int _hits;
    private int _misses;

    public AnimationCacheService(ILogger<AnimationCacheService> logger)
    {
        _logger = logger;
        _cacheDir = Path.Combine(Path.GetTempPath(), "VideoForge", "animation-cache");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<string?> GetCachedClipAsync(string imagePath, string settingsFingerprint, CancellationToken ct = default)
    {
        var imageHash = await ComputeImageHashAsync(imagePath, ct);
        var cacheKey = $"{imageHash}_{settingsFingerprint}";
        var cachedPath = Path.Combine(_cacheDir, cacheKey, "clip.mp4");
        var metaPath = Path.Combine(_cacheDir, cacheKey, "meta.json");

        if (File.Exists(cachedPath) && File.Exists(metaPath))
        {
            // Update last access time
            File.SetLastAccessTimeUtc(metaPath, DateTime.UtcNow);
            Interlocked.Increment(ref _hits);
            _logger.LogInformation("Cache HIT: {FileName} -> {CacheKey}", Path.GetFileName(imagePath), cacheKey[..16]);
            return cachedPath;
        }

        Interlocked.Increment(ref _misses);
        return null;
    }

    public async Task<string> StoreCachedClipAsync(string imagePath, string settingsFingerprint, string renderedClipPath, CancellationToken ct = default)
    {
        var imageHash = await ComputeImageHashAsync(imagePath, ct);
        var cacheKey = $"{imageHash}_{settingsFingerprint}";
        var cacheEntryDir = Path.Combine(_cacheDir, cacheKey);
        Directory.CreateDirectory(cacheEntryDir);

        var cachedPath = Path.Combine(cacheEntryDir, "clip.mp4");
        File.Copy(renderedClipPath, cachedPath, overwrite: true);

        var meta = new
        {
            OriginalImage = Path.GetFileName(imagePath),
            CachedAt = DateTime.UtcNow,
            SizeBytes = new FileInfo(renderedClipPath).Length,
            SettingsFingerprint = settingsFingerprint
        };
        var metaPath = Path.Combine(cacheEntryDir, "meta.json");
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta), ct);

        _logger.LogInformation("Cache STORE: {FileName} -> {CacheKey} ({Size})",
            Path.GetFileName(imagePath), cacheKey[..16], FormatSize(new FileInfo(renderedClipPath).Length));

        return cachedPath;
    }

    public string ComputeSettingsFingerprint(int outputWidth, int outputHeight, int fps, int bitrateKbps,
        double duration, int animationType, int kenBurnsDirection, double zoomFactor,
        double parallaxIntensity, bool hwAccel)
    {
        var raw = $"{outputWidth}x{outputHeight}@{fps}fps_{bitrateKbps}kbps_" +
                  $"d{duration:F1}_t{animationType}_kb{kenBurnsDirection}_z{zoomFactor:F2}_" +
                  $"p{parallaxIntensity:F2}_hw{(hwAccel ? 1 : 0)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public async Task<string> ComputeImageHashAsync(string imagePath, CancellationToken ct = default)
    {
        using var stream = File.OpenRead(imagePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    public async Task<CacheStats> GetStatsAsync(CancellationToken ct = default)
    {
        var stats = new CacheStats
        {
            CacheHits = _hits,
            CacheMisses = _misses
        };

        if (!Directory.Exists(_cacheDir))
            return stats;

        await Task.Run(() =>
        {
            foreach (var dir in Directory.GetDirectories(_cacheDir))
            {
                var clipPath = Path.Combine(dir, "clip.mp4");
                if (File.Exists(clipPath))
                {
                    stats.TotalClips++;
                    stats.TotalSizeBytes += new FileInfo(clipPath).Length;
                }
            }
        }, ct);

        return stats;
    }

    public async Task ClearAsync(TimeSpan? maxAge = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(_cacheDir))
            return;

        await Task.Run(() =>
        {
            var cutoff = maxAge.HasValue ? DateTime.UtcNow - maxAge.Value : DateTime.MaxValue;

            foreach (var dir in Directory.GetDirectories(_cacheDir))
            {
                var metaPath = Path.Combine(dir, "meta.json");
                if (maxAge.HasValue && File.Exists(metaPath) && File.GetLastAccessTimeUtc(metaPath) > cutoff)
                    continue;

                try
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Cache evicted: {Dir}", Path.GetFileName(dir));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to evict cache entry: {Dir}", dir);
                }
            }
        }, ct);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1048576 => $"{bytes / 1024.0:F0} KB",
        < 1073741824 => $"{bytes / 1048576.0:F1} MB",
        _ => $"{bytes / 1073741824.0:F2} GB"
    };
}
