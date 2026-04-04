using Canary.Comparison;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Canary.Tests.Comparison;

[Trait("Category", "Unit")]
public class SsimComparerTests
{
    private readonly SsimComparer _comparer = new();

    [Fact]
    public void SsimComparer_IdenticalImages_ReturnsOne()
    {
        // Create a 64x64 gradient image
        using var baseline = CreateGradientImage(64, 64);
        using var candidate = CreateGradientImage(64, 64);

        double ssim = _comparer.ComputeSsim(baseline, candidate);

        Assert.True(ssim > 0.999, $"SSIM should be ~1.0 for identical images, got {ssim}");
    }

    [Fact]
    public void SsimComparer_CompletelyDifferent_ReturnsLow()
    {
        using var white = new Image<Rgba32>(64, 64);
        white.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < 64; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < 64; x++)
                    row[x] = new Rgba32(255, 255, 255, 255);
            }
        });

        using var noise = new Image<Rgba32>(64, 64);
        var rng = new Random(42);
        noise.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < 64; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < 64; x++)
                {
                    byte r = (byte)rng.Next(256);
                    byte g = (byte)rng.Next(256);
                    byte b = (byte)rng.Next(256);
                    row[x] = new Rgba32(r, g, b, 255);
                }
            }
        });

        double ssim = _comparer.ComputeSsim(white, noise);

        Assert.True(ssim < 0.3, $"SSIM should be low for completely different images, got {ssim}");
    }

    [Fact]
    public void SsimComparer_SlightShift_ReturnsHigh()
    {
        using var original = CreateGradientImage(64, 64);

        // Shift by 1 pixel to the right (fill left column with black)
        using var shifted = new Image<Rgba32>(64, 64);
        var origPixels = new Rgba32[64 * 64];
        original.CopyPixelDataTo(origPixels);

        shifted.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < 64; y++)
            {
                var row = accessor.GetRowSpan(y);
                row[0] = new Rgba32(0, 0, 0, 255); // left edge filled
                for (int x = 1; x < 64; x++)
                    row[x] = origPixels[y * 64 + (x - 1)];
            }
        });

        double ssim = _comparer.ComputeSsim(original, shifted);

        Assert.True(ssim > 0.85, $"SSIM should be high for slightly shifted image, got {ssim}");
        Assert.True(ssim < 1.0, $"SSIM should be less than 1.0 for shifted image, got {ssim}");
    }

    private static Image<Rgba32> CreateGradientImage(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    byte gray = (byte)((x + y) * 255 / (width + height - 2));
                    row[x] = new Rgba32(gray, gray, gray, 255);
                }
            }
        });
        return image;
    }
}
