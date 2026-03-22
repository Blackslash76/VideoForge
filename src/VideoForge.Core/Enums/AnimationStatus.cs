namespace VideoForge.Core.Enums;

public enum AnimationStatus
{
    Queued = 0,
    AnalyzingImage = 1,
    GeneratingDepthMap = 2,
    Animating = 3,
    Encoding = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7
}
