using Canary.Comparison;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Canary.Tests.Comparison;

[Trait("Category", "Unit")]
public class PixelDiffComparerTests
{
    private readonly PixelDiffComparer _comparer = new();

    private static Image<Rgba32> CreateSolidImage(int width, int height, Rgba32 color)
    {
        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                    row[x] = color;
            }
        });
        return image;
    }

    [Fact]
    public void PixelDiffComparer_IdenticalImages_ReturnsZeroDiff()
    {
        using var baseline = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        using var candidate = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));

        using var result = _comparer.Compare(baseline, candidate, colorThreshold: 3, tolerance: 0.01);

        Assert.Equal(0.0, result.DiffPercentage);
        Assert.Equal(0, result.ChangedPixels);
        Assert.True(result.Passed);
    }

    [Fact]
    public void PixelDiffComparer_SinglePixelDiff_ReturnsCorrectCount()
    {
        using var baseline = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        using var candidate = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        candidate[50, 50] = new Rgba32(0, 0, 255, 255);

        using var result = _comparer.Compare(baseline, candidate, colorThreshold: 3);

        Assert.Equal(1, result.ChangedPixels);
        Assert.Equal(1.0 / 10000.0, result.DiffPercentage, 6);
    }

    [Fact]
    public void PixelDiffComparer_TenPercentNoise_ReturnsApproxTenPercent()
    {
        using var baseline = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        using var candidate = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));

        var rng = new Random(42);
        var changedIndices = new HashSet<int>();
        while (changedIndices.Count < 1000) // 10% of 10000
            changedIndices.Add(rng.Next(10000));

        foreach (var idx in changedIndices)
        {
            int x = idx % 100;
            int y = idx / 100;
            candidate[x, y] = new Rgba32(0, 0, 255, 255);
        }

        using var result = _comparer.Compare(baseline, candidate, colorThreshold: 0);

        Assert.InRange(result.DiffPercentage, 0.08, 0.12);
    }

    [Fact]
    public void PixelDiffComparer_BelowThreshold_CountsAsSame()
    {
        using var baseline = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        using var candidate = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        // Diff of 2 per channel — within threshold of 3
        candidate[50, 50] = new Rgba32(253, 0, 0, 255);

        using var result = _comparer.Compare(baseline, candidate, colorThreshold: 3);

        Assert.Equal(0, result.ChangedPixels);
    }

    [Fact]
    public void PixelDiffComparer_AboveThreshold_CountsAsDifferent()
    {
        using var baseline = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        using var candidate = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        candidate[50, 50] = new Rgba32(253, 0, 0, 255);

        using var result = _comparer.Compare(baseline, candidate, colorThreshold: 1);

        Assert.Equal(1, result.ChangedPixels);
    }

    [Fact]
    public void PixelDiffComparer_DimensionMismatch_ThrowsArgumentException()
    {
        using var baseline = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        using var candidate = CreateSolidImage(200, 200, new Rgba32(255, 0, 0, 255));

        var ex = Assert.Throws<ArgumentException>(() => _comparer.Compare(baseline, candidate));
        Assert.Contains("dimensions", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PixelDiffComparer_DiffImage_HighlightsChanges()
    {
        using var baseline = CreateSolidImage(10, 10, new Rgba32(255, 0, 0, 255));
        using var candidate = CreateSolidImage(10, 10, new Rgba32(255, 0, 0, 255));
        candidate[0, 0] = new Rgba32(0, 0, 255, 255);

        using var result = _comparer.Compare(baseline, candidate, colorThreshold: 0);

        Assert.NotNull(result.DiffImage);
        // Changed pixel should be magenta
        var changedPixel = result.DiffImage[0, 0];
        Assert.Equal(255, changedPixel.R);
        Assert.Equal(0, changedPixel.G);
        Assert.Equal(255, changedPixel.B);
        Assert.Equal(255, changedPixel.A);

        // Unchanged pixel should be dim/semi-transparent
        var unchangedPixel = result.DiffImage[5, 5];
        Assert.True(unchangedPixel.A < 128, "Unchanged pixel should be semi-transparent");
    }

    [Fact]
    public void PixelDiffComparer_ToleranceGate_PassesWhenBelowTolerance()
    {
        using var baseline = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        using var candidate = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));

        // Change 1% of pixels
        for (int i = 0; i < 100; i++)
            candidate[i, 0] = new Rgba32(0, 0, 255, 255);

        using var result = _comparer.Compare(baseline, candidate, colorThreshold: 0, tolerance: 0.02);

        Assert.True(result.Passed);
    }

    [Fact]
    public void PixelDiffComparer_ToleranceGate_FailsWhenAboveTolerance()
    {
        using var baseline = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));
        using var candidate = CreateSolidImage(100, 100, new Rgba32(255, 0, 0, 255));

        // Change 5% of pixels
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 100; x++)
                candidate[x, y] = new Rgba32(0, 0, 255, 255);

        using var result = _comparer.Compare(baseline, candidate, colorThreshold: 0, tolerance: 0.02);

        Assert.False(result.Passed);
    }
}
