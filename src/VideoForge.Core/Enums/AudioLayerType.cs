namespace VideoForge.Core.Enums;

public enum AudioLayerType
{
    /// <summary>Musica di sottofondo — viene abbassata automaticamente se c'è un voiceover.</summary>
    Music = 0,

    /// <summary>Voce narrante — ha priorità sul mix, attiva il ducking sulla musica.</summary>
    Voiceover = 1,

    /// <summary>Effetti sonori — mixati sopra tutto con il proprio volume.</summary>
    SoundEffect = 2
}
