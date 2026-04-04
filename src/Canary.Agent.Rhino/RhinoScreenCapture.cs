using System;
using System.Drawing;
using System.Drawing.Imaging;
using Rhino;
using Rhino.Display;

namespace Canary.Agent.Rhino;

/// <summary>
/// Captures screenshots from Rhino viewports using <see cref="ViewCapture"/>.
/// </summary>
public sealed class RhinoScreenCapture
{
    /// <summary>
    /// Captures a screenshot of the active Rhino viewport at the requested dimensions.
    /// </summary>
    /// <param name="settings">Capture settings including dimensions and output path.</param>
    /// <returns>Screenshot result with file path and metadata.</returns>
    /// <exception cref="InvalidOperationException">No active viewport available.</exception>
    public ScreenshotResult Capture(CaptureSettings settings)
    {
        var view = RhinoDoc.ActiveDoc?.Views.ActiveView;
        if (view == null)
            throw new InvalidOperationException("No active viewport. Cannot capture screenshot.");

        var size = new Size(settings.Width, settings.Height);
        if (size.Width <= 0 || size.Height <= 0)
            throw new ArgumentException($"Invalid capture dimensions: {size.Width}x{size.Height}");

        var captureSettings = new ViewCaptureSettings(view, size, 72);
        using var bitmap = ViewCapture.CaptureToBitmap(captureSettings);

        if (bitmap == null)
            throw new InvalidOperationException("ViewCapture.CaptureToBitmap returned null. The viewport may not be visible.");

        // Ensure output directory exists
        var dir = System.IO.Path.GetDirectoryName(settings.OutputPath);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);

        bitmap.Save(settings.OutputPath, ImageFormat.Png);

        return new ScreenshotResult
        {
            FilePath = settings.OutputPath,
            Width = bitmap.Width,
            Height = bitmap.Height,
            CapturedAt = DateTime.UtcNow
        };
    }
}
