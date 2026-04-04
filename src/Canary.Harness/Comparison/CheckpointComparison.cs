using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Canary.Comparison;

/// <summary>
/// Data for a single checkpoint comparison, used by CompositeBuilder.
/// </summary>
public sealed class CheckpointComparison
{
    public required string Name { get; init; }
    public required Image<Rgba32> Baseline { get; init; }
    public required Image<Rgba32> Candidate { get; init; }
    public required Image<Rgba32> DiffImage { get; init; }
    public required bool Passed { get; init; }
    public required double DiffPercentage { get; init; }
    public required double Tolerance { get; init; }
}
