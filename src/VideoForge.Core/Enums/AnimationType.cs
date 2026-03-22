namespace VideoForge.Core.Enums;

public enum AnimationType
{
    /// <summary>Pan e zoom cinematografico sull'immagine</summary>
    KenBurns = 0,

    /// <summary>Separazione livelli con effetto profondità 3D</summary>
    Parallax = 1,

    /// <summary>Parti dell'immagine si muovono in loop (acqua, cielo, capelli)</summary>
    Cinemagraph = 2,

    /// <summary>Animazione AI - l'intera foto prende vita</summary>
    AiMotion = 3,

    /// <summary>Ken Burns + Parallax combinati</summary>
    KenBurnsParallax = 4
}
