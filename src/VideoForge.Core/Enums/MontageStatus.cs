namespace VideoForge.Core.Enums;

public enum MontageStatus
{
    Created = 0,
    AnimatingPhotos = 1,
    AssemblingVideo = 2,
    MixingAudio = 3,
    FinalEncoding = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7
}
