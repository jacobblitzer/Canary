using Canary.Comparison;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Canary.Tests.Comparison;

[Trait("Category", "Unit")]
public class CompositeBuilderTests
{
    private readonly CompositeBuilder _builder = new();

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

    private static CheckpointComparison CreateCheckpoint(string name, int width, int height, bool passed, double diffPct)
    {
        return new CheckpointComparison
        {
            Name = name,
            Baseline = CreateSolidImage(width, height, new Rgba32(255, 0, 0, 255)),
            Candidate = CreateSolidImage(width, height, new Rgba32(0, 255, 0, 255)),
            DiffImage = CreateSolidImage(width, height, new Rgba32(255, 0, 255, 128)),
            Passed = passed,
            DiffPercentage = diffPct,
            Tolerance = 0.02
        };
    }

    [Fact]
    public void CompositeBuilder_ThreeCheckpoints_CorrectDimensions()
    {
        var checkpoints = new[]
        {
            CreateCheckpoint("cp1", 100, 100, true, 0.001),
            CreateCheckpoint("cp2", 100, 100, false, 0.05),
            CreateCheckpoint("cp3", 100, 100, true, 0.003),
        };

        using var result = _builder.Build(checkpoints);

        Assert.NotNull(result);
        // Width = 3 × 100 + 2 × 2px gap = 304
        Assert.Equal(304, result.Width);
        // Height = 3 × (24 label + 100 image) + 2 × 4px strip gap = 372 + 8 = 380
        Assert.Equal(380, result.Height);

        foreach (var cp in checkpoints)
        {
            cp.Baseline.Dispose();
            cp.Candidate.Dispose();
            cp.DiffImage.Dispose();
        }
    }

    [Fact]
    public void CompositeBuilder_ZeroCheckpoints_ReturnsNull()
    {
        var result = _builder.Build(Array.Empty<CheckpointComparison>());
        Assert.Null(result);
    }

    [Fact]
    public void CompositeBuilder_SingleCheckpoint_ProducesValidImage()
    {
        var cp = CreateCheckpoint("after_stroke", 200, 150, true, 0.003);

        using var result = _builder.Build(new[] { cp });

        Assert.NotNull(result);
        // Width = 3 × 200 + 2 × 2 = 604
        Assert.True(result.Width > 600, $"Composite should be wider than 600px, got {result.Width}");
        // Height = 24 label + 150 image = 174
        Assert.Equal(174, result.Height);

        cp.Baseline.Dispose();
        cp.Candidate.Dispose();
        cp.DiffImage.Dispose();
    }

    [Fact]
    public void CompositeBuilder_LabelsIncludeStatus()
    {
        var cpPass = CreateCheckpoint("pass_cp", 100, 100, true, 0.003);
        var cpFail = CreateCheckpoint("fail_cp", 100, 100, false, 0.05);

        using var result = _builder.Build(new[] { cpPass, cpFail });

        Assert.NotNull(result);

        // Verify pass label bar region is green-ish (forest green: 34,139,34)
        var passLabelPixel = result[10, 10]; // Top of first label bar
        Assert.Equal(34, passLabelPixel.R);
        Assert.Equal(139, passLabelPixel.G);
        Assert.Equal(34, passLabelPixel.B);

        // Verify fail label bar region is red-ish (crimson: 220,20,60)
        // Second strip starts at: 24 (label) + 100 (image) + 4 (gap) = 128
        var failLabelPixel = result[10, 128 + 5];
        Assert.Equal(220, failLabelPixel.R);
        Assert.Equal(20, failLabelPixel.G);
        Assert.Equal(60, failLabelPixel.B);

        cpPass.Baseline.Dispose();
        cpPass.Candidate.Dispose();
        cpPass.DiffImage.Dispose();
        cpFail.Baseline.Dispose();
        cpFail.Candidate.Dispose();
        cpFail.DiffImage.Dispose();
    }
}
