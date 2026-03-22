namespace VideoForge.Core.Interfaces;

/// <summary>
/// Cache per clip animati. Stessa foto + stessi parametri = skip rendering.
/// La chiave è un hash di (contenuto immagine + settings di animazione).
/// </summary>
public interface IAnimationCache
{
    /// <summary>
    /// Cerca un clip già renderizzato per questa combinazione immagine+settings.
    /// Restituisce il path del clip cachato, o null se non esiste.
    /// </summary>
    Task<string?> GetCachedClipAsync(string imagePath, string settingsFingerprint, CancellationToken ct = default);

    /// <summary>
    /// Salva un clip renderizzato nella cache.
    /// </summary>
    Task<string> StoreCachedClipAsync(string imagePath, string settingsFingerprint, string renderedClipPath, CancellationToken ct = default);

    /// <summary>
    /// Genera un fingerprint univoco dei parametri di animazione.
    /// </summary>
    string ComputeSettingsFingerprint(int outputWidth, int outputHeight, int fps, int bitrateKbps,
        double duration, int animationType, int kenBurnsDirection, double zoomFactor,
        double parallaxIntensity, bool hwAccel);

    /// <summary>
    /// Calcola l'hash SHA256 del contenuto dell'immagine.
    /// </summary>
    Task<string> ComputeImageHashAsync(string imagePath, CancellationToken ct = default);

    /// <summary>
    /// Info sulla cache: quanti clip, dimensione totale.
    /// </summary>
    Task<CacheStats> GetStatsAsync(CancellationToken ct = default);

    /// <summary>
    /// Svuota la cache (o clip più vecchi di maxAge).
    /// </summary>
    Task ClearAsync(TimeSpan? maxAge = null, CancellationToken ct = default);
}

public class CacheStats
{
    public int TotalClips { get; set; }
    public long TotalSizeBytes { get; set; }
    public string TotalSizeFormatted => TotalSizeBytes switch
    {
        < 1024 => $"{TotalSizeBytes} B",
        < 1048576 => $"{TotalSizeBytes / 1024.0:F1} KB",
        < 1073741824 => $"{TotalSizeBytes / 1048576.0:F1} MB",
        _ => $"{TotalSizeBytes / 1073741824.0:F2} GB"
    };
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public double HitRate => CacheHits + CacheMisses == 0 ? 0 : (double)CacheHits / (CacheHits + CacheMisses) * 100;
}
