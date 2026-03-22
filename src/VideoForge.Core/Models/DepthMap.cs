namespace VideoForge.Core.Models;

public class DepthMap
{
    public string ImagePath { get; set; } = string.Empty;
    public string DepthMapPath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public float MinDepth { get; set; }
    public float MaxDepth { get; set; }
}
