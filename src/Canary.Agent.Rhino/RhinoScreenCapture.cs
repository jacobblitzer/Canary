using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using Rhino;
using Rhino.Display;

namespace Canary.Agent.Rhino;

/// <summary>
/// Captures screenshots from Rhino viewports.
/// Uses multiple strategies to ensure the capture contains actual rendered content.
/// </summary>
public sealed class RhinoScreenCapture
{
    /// <summary>
    /// Captures a screenshot of the active Rhino viewport at the requested dimensions.
    /// </summary>
    public ScreenshotResult Capture(CaptureSettings settings)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc == null)
            throw new InvalidOperationException("No active document. Cannot capture screenshot.");

        var view = doc.Views.ActiveView;
        if (view == null)
            throw new InvalidOperationException("No active viewport. Cannot capture screenshot.");

        // Ensure output directory exists
        var dir = System.IO.Path.GetDirectoryName(settings.OutputPath);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);

        // Force a full redraw and give Rhino time to render
        doc.Views.Redraw();
        RhinoApp.Wait();
        Thread.Sleep(500);
        view.Redraw();
        RhinoApp.Wait();
        Thread.Sleep(200);

        // Strategy 1: RhinoView.CaptureToBitmap — most reliable for active viewport
        var size = new Size(settings.Width, settings.Height);
        var bitmap = view.CaptureToBitmap(size);

        if (bitmap != null)
        {
            bitmap.Save(settings.OutputPath, ImageFormat.Png);
            bitmap.Dispose();
        }
        else
        {
            // Strategy 2: ViewCapture API
            var captureSettings = new ViewCaptureSettings(view, size, 72);
            using var bitmap2 = ViewCapture.CaptureToBitmap(captureSettings);

            if (bitmap2 != null)
            {
                bitmap2.Save(settings.OutputPath, ImageFormat.Png);
            }
            else
            {
                // Strategy 3: _-ViewCaptureToFile command
                var path = settings.OutputPath.Replace("\\", "/");
                var script = $"_-ViewCaptureToFile \"{path}\" _Width={settings.Width} _Height={settings.Height} _Scale=1 _DrawGrid=No _DrawWorldAxes=No _DrawCPlaneAxes=No _TransparentBackground=No _Enter";
                RhinoApp.RunScript(script, echo: false);
            }
        }

        // Verify file was created
        if (!System.IO.File.Exists(settings.OutputPath))
            throw new InvalidOperationException($"Screenshot file was not created at: {settings.OutputPath}");

        // Read dimensions from the saved file
        using var img = Image.FromFile(settings.OutputPath);
        return new ScreenshotResult
        {
            FilePath = settings.OutputPath,
            Width = img.Width,
            Height = img.Height,
            CapturedAt = DateTime.UtcNow
        };
    }
}
