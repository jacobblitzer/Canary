using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Canary.Comparison;

/// <summary>
/// Computes the Structural Similarity Index (SSIM) between two images.
/// Uses an 8x8 sliding window and standard SSIM constants.
/// </summary>
public sealed class SsimComparer
{
    // Standard SSIM constants (L=255 for 8-bit images)
    private const double K1 = 0.01;
    private const double K2 = 0.03;
    private const double L = 255.0;
    private static readonly double C1 = (K1 * L) * (K1 * L);
    private static readonly double C2 = (K2 * L) * (K2 * L);
    private const int WindowSize = 8;

    /// <summary>
    /// Compute SSIM between two images. Returns a score from 0.0 (completely different) to 1.0 (identical).
    /// </summary>
    public double ComputeSsim(Image<Rgba32> baseline, Image<Rgba32> candidate)
    {
        if (baseline.Width != candidate.Width || baseline.Height != candidate.Height)
        {
            throw new ArgumentException(
                $"Image dimensions do not match: baseline is {baseline.Width}x{baseline.Height}, " +
                $"candidate is {candidate.Width}x{candidate.Height}.");
        }

        int width = baseline.Width;
        int height = baseline.Height;

        // Convert to grayscale luminance arrays
        var baselineLum = ToGrayscale(baseline);
        var candidateLum = ToGrayscale(candidate);

        // Sliding window SSIM
        int windowsX = width - WindowSize + 1;
        int windowsY = height - WindowSize + 1;

        if (windowsX <= 0 || windowsY <= 0)
        {
            // Image too small for windowed SSIM — fall back to single-window comparison
            return ComputeWindowSsim(baselineLum, candidateLum, width, 0, 0, width, height);
        }

        double ssimSum = 0.0;
        int windowCount = 0;

        for (int wy = 0; wy < windowsY; wy++)
        {
            for (int wx = 0; wx < windowsX; wx++)
            {
                ssimSum += ComputeWindowSsim(baselineLum, candidateLum, width, wx, wy, WindowSize, WindowSize);
                windowCount++;
            }
        }

        return windowCount > 0 ? ssimSum / windowCount : 1.0;
    }

    private static double[] ToGrayscale(Image<Rgba32> image)
    {
        int width = image.Width;
        int height = image.Height;
        var pixels = new Rgba32[width * height];
        image.CopyPixelDataTo(pixels);

        var lum = new double[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            lum[i] = 0.299 * pixels[i].R + 0.587 * pixels[i].G + 0.114 * pixels[i].B;
        }
        return lum;
    }

    private static double ComputeWindowSsim(
        double[] lumA, double[] lumB, int stride,
        int startX, int startY, int winW, int winH)
    {
        int n = winW * winH;
        double sumA = 0, sumB = 0;
        double sumA2 = 0, sumB2 = 0, sumAB = 0;

        for (int dy = 0; dy < winH; dy++)
        {
            int rowOffset = (startY + dy) * stride + startX;
            for (int dx = 0; dx < winW; dx++)
            {
                double a = lumA[rowOffset + dx];
                double b = lumB[rowOffset + dx];
                sumA += a;
                sumB += b;
                sumA2 += a * a;
                sumB2 += b * b;
                sumAB += a * b;
            }
        }

        double meanA = sumA / n;
        double meanB = sumB / n;
        double varA = (sumA2 / n) - (meanA * meanA);
        double varB = (sumB2 / n) - (meanB * meanB);
        double covAB = (sumAB / n) - (meanA * meanB);

        double numerator = (2.0 * meanA * meanB + C1) * (2.0 * covAB + C2);
        double denominator = (meanA * meanA + meanB * meanB + C1) * (varA + varB + C2);

        return numerator / denominator;
    }
}
