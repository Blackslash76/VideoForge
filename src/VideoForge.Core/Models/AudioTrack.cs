using VideoForge.Core.Enums;

namespace VideoForge.Core.Models;

public class AudioTrack
{
    public int Order { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public double FadeInSeconds { get; set; } = 2.0;
    public double FadeOutSeconds { get; set; } = 3.0;
    public double Volume { get; set; } = 1.0;

    /// <summary>Tipo di layer: Music, Voiceover, SoundEffect.</summary>
    public AudioLayerType LayerType { get; set; } = AudioLayerType.Music;

    /// <summary>Crossfade in secondi con la traccia musicale successiva (solo per Music).</summary>
    public double CrossfadeSeconds { get; set; } = 3.0;

    /// <summary>Offset in secondi dall'inizio del video (per SoundEffect/Voiceover).</summary>
    public double StartOffsetSeconds { get; set; }

    /// <summary>Quanto abbassare la musica quando questo voiceover è attivo (0.0-1.0). Default: la musica scende al 20%.</summary>
    public double DuckingLevel { get; set; } = 0.2;
}
