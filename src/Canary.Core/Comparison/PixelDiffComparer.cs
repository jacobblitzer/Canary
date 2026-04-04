using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Canary.Comparison;

/// <summary>
/// Compares two images pixel-by-pixel with a configurable per-channel color threshold.
/// </summary>
public sealed class PixelDiffComparer
{
    private static readonly Rgba32 MagentaPixel = new(255, 0, 255, 255);
    private static readonly Rgba32 DimPixel = new(0, 0, 0, 40);

    /// <summary>
    /// Compare two images pixel-by-pixel.
    /// </summary>
    /// <param name="baseline">The baseline (expected) image.</param>
    /// <param name="candidate">The candidate (actual) image.</param>
    /// <param name="colorThreshold">Max per-channel difference to consider "same" (0-255, default 3).</param>
    /// <param name="tolerance">Maximum acceptable diff fraction (0.0-1.0). Used to set Passed.</param>
    public ComparisonResult Compare(
        Image<Rgba32> baseline,
        Image<Rgba32> candidate,
        int colorThreshold = 3,
        double tolerance = 0.0)
    {
        if (baseline.Width != candidate.Width || baseline.Height != candidate.Height)
        {
            throw new ArgumentException(
                $"Image dimensions do not match: baseline is {baseline.Width}x{baseline.Height}, " +
                $"candidate is {candidate.Width}x{candidate.Height}.");
        }

        int width = baseline.Width;
        int height = baseline.Height;
        int totalPixels = width * height;
        int changedPixels = 0;

        // Copy pixel data into arrays for fast comparison
        var baselinePixels = new Rgba32[totalPixels];
        var candidatePixels = new Rgba32[totalPixels];
        baseline.CopyPixelDataTo(baselinePixels);
        candidate.CopyPixelDataTo(candidatePixels);

        var diffPixels = new Rgba32[totalPixels];

        for (int i = 0; i < totalPixels; i++)
        {
            var bp = baselinePixels[i];
            var cp = candidatePixels[i];

            bool isDifferent =
                Math.Abs(bp.R - cp.R) > colorThreshold ||
                Math.Abs(bp.G - cp.G) > colorThreshold ||
                Math.Abs(bp.B - cp.B) > colorThreshold ||
                Math.Abs(bp.A - cp.A) > colorThreshold;

            if (isDifferent)
            {
                changedPixels++;
                diffPixels[i] = MagentaPixel;
            }
            else
            {
                diffPixels[i] = DimPixel;
            }
        }

        var diffImage = Image.LoadPixelData<Rgba32>(diffPixels, width, height);

        double diffPercentage = totalPixels > 0 ? (double)changedPixels / totalPixels : 0.0;

        return new ComparisonResult
        {
            DiffPercentage = diffPercentage,
            Passed = diffPercentage <= tolerance,
            DiffImage = diffImage,
            TotalPixels = totalPixels,
            ChangedPixels = changedPixels
        };
    }
}
