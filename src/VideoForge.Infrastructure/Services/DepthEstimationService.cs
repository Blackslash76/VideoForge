using System.Diagnostics;
using VideoForge.Core.Interfaces;
using VideoForge.Core.Models;

namespace VideoForge.Infrastructure.Services;

/// <summary>
/// Genera depth map usando MiDaS (Monocular Depth Estimation) via Python/PyTorch.
/// Richiede: Python 3, torch, torchvision, timm, opencv-python installati.
/// Utilizza la GPU NVIDIA per inferenza veloce.
/// </summary>
public class DepthEstimationService : IDepthEstimationService
{
    private readonly string _pythonPath;

    public DepthEstimationService(string? pythonPath = null)
    {
        _pythonPath = pythonPath ?? "python";
    }

    public bool IsAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = "-c \"import torch; print(torch.cuda.is_available())\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd()?.Trim();
            p?.WaitForExit(10000);
            return output == "True";
        }
        catch { return false; }
    }

    public async Task<DepthMap> EstimateDepthAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var outputPath = Path.Combine(
            Path.GetDirectoryName(imagePath)!,
            Path.GetFileNameWithoutExtension(imagePath) + "_depth.png");

        var script = GenerateMidasScript(imagePath, outputPath);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"midas_{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = $"\"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Impossibile avviare Python per depth estimation");

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Depth estimation fallita: {stderr}");

            // Parse dimensioni dall'output
            var parts = stdout.Trim().Split(',');
            var width = int.Parse(parts.Length > 0 ? parts[0] : "0");
            var height = int.Parse(parts.Length > 1 ? parts[1] : "0");

            return new DepthMap
            {
                ImagePath = imagePath,
                DepthMapPath = outputPath,
                Width = width,
                Height = height,
                MinDepth = 0,
                MaxDepth = 1
            };
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static string GenerateMidasScript(string inputPath, string outputPath)
    {
        // Escape backslashes for Windows paths in Python string
        var input = inputPath.Replace("\\", "\\\\");
        var output = outputPath.Replace("\\", "\\\\");

        return $$"""
import torch
import cv2
import numpy as np

# Carica MiDaS
model_type = "DPT_Large"
midas = torch.hub.load("intel-isl/MiDaS", model_type)
device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
midas.to(device)
midas.eval()

# Transform
midas_transforms = torch.hub.load("intel-isl/MiDaS", "transforms")
transform = midas_transforms.dpt_transform

# Carica immagine
img = cv2.imread("{{input}}")
img_rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
h, w = img.shape[:2]

# Inferenza
input_batch = transform(img_rgb).to(device)
with torch.no_grad():
    prediction = midas(input_batch)
    prediction = torch.nn.functional.interpolate(
        prediction.unsqueeze(1), size=(h, w), mode="bicubic", align_corners=False
    ).squeeze()

# Normalizza e salva
depth = prediction.cpu().numpy()
depth = (depth - depth.min()) / (depth.max() - depth.min()) * 255
depth = depth.astype(np.uint8)
cv2.imwrite("{{output}}", depth)

print(f"{w},{h}")
""";
    }
}
