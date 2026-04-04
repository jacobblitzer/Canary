using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Canary.Comparison;

/// <summary>
/// Result of comparing two images.
/// </summary>
public sealed class ComparisonResult : IDisposable
{
    /// <summary>Fraction of pixels that differ (0.0 to 1.0).</summary>
    public double DiffPercentage { get; init; }

    /// <summary>Whether the diff is within the specified tolerance.</summary>
    public bool Passed { get; init; }

    /// <summary>Visualization: changed pixels in magenta, unchanged semi-transparent.</summary>
    public Image<Rgba32>? DiffImage { get; init; }

    /// <summary>Total number of pixels compared.</summary>
    public int TotalPixels { get; init; }

    /// <summary>Number of pixels exceeding the color threshold.</summary>
    public int ChangedPixels { get; init; }

    public void Dispose()
    {
        DiffImage?.Dispose();
    }
}
