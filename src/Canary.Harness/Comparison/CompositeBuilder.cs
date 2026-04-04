using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Canary.Comparison;

/// <summary>
/// Builds a composite review image from checkpoint comparison results.
/// Each checkpoint is rendered as a horizontal strip: [baseline | candidate | diff]
/// with a label bar above, stacked vertically.
/// </summary>
public sealed class CompositeBuilder
{
    private const int LabelHeight = 24;
    private const int ImageGap = 2;
    private const int StripGap = 4;

    /// <summary>
    /// Build a composite image from checkpoint comparisons.
    /// Returns null if no checkpoints are provided.
    /// </summary>
    public Image<Rgba32>? Build(IReadOnlyList<CheckpointComparison> checkpoints)
    {
        if (checkpoints.Count == 0)
            return null;

        // Calculate dimensions
        int maxImageWidth = 0;
        int maxImageHeight = 0;
        foreach (var cp in checkpoints)
        {
            if (cp.Baseline.Width > maxImageWidth) maxImageWidth = cp.Baseline.Width;
            if (cp.Baseline.Height > maxImageHeight) maxImageHeight = cp.Baseline.Height;
        }

        // Strip width = 3 images side by side + 2 gaps
        int stripWidth = (maxImageWidth * 3) + (ImageGap * 2);

        // Total height = sum of all strips (label + image + gap between strips)
        int totalHeight = 0;
        for (int i = 0; i < checkpoints.Count; i++)
        {
            totalHeight += LabelHeight + checkpoints[i].Baseline.Height;
            if (i < checkpoints.Count - 1)
                totalHeight += StripGap;
        }

        var composite = new Image<Rgba32>(stripWidth, totalHeight);

        // Fill background with dark gray
        composite.Mutate(ctx => ctx.BackgroundColor(new Rgba32(48, 48, 48, 255)));

        int currentY = 0;
        foreach (var cp in checkpoints)
        {
            // Draw label bar
            DrawLabelBar(composite, currentY, stripWidth, cp);
            currentY += LabelHeight;

            // Draw baseline | candidate | diff
            int imgW = cp.Baseline.Width;
            composite.Mutate(ctx =>
            {
                ctx.DrawImage(cp.Baseline, new Point(0, currentY), 1f);
                ctx.DrawImage(cp.Candidate, new Point(imgW + ImageGap, currentY), 1f);
                ctx.DrawImage(cp.DiffImage, new Point((imgW + ImageGap) * 2, currentY), 1f);
            });

            currentY += cp.Baseline.Height + StripGap;
        }

        return composite;
    }

    /// <summary>
    /// Build and save the composite to a file.
    /// </summary>
    public async Task SaveAsync(IReadOnlyList<CheckpointComparison> checkpoints, string outputPath)
    {
        using var composite = Build(checkpoints);
        if (composite != null)
            await composite.SaveAsPngAsync(outputPath).ConfigureAwait(false);
    }

    private static void DrawLabelBar(Image<Rgba32> composite, int y, int width, CheckpointComparison cp)
    {
        var barColor = cp.Passed
            ? new Rgba32(34, 139, 34, 255)   // forest green for pass
            : new Rgba32(220, 20, 60, 255);  // crimson for fail

        // Fill the label bar region
        composite.ProcessPixelRows(accessor =>
        {
            for (int row = y; row < y + LabelHeight && row < accessor.Height; row++)
            {
                var span = accessor.GetRowSpan(row);
                for (int x = 0; x < width && x < accessor.Width; x++)
                    span[x] = barColor;
            }
        });

        // Note: Text rendering requires SixLabors.Fonts, which adds complexity.
        // The label bar color (green/red) conveys pass/fail status visually.
        // Full text labels will be added in Phase 5 when we add the HTML report.
    }
}
