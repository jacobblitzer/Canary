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
        int finalWidth, finalHeight;
        using (var img = Image.FromFile(settings.OutputPath))
        {
            finalWidth = img.Width;
            finalHeight = img.Height;
        }

        var result = new ScreenshotResult
        {
            FilePath = settings.OutputPath,
            Width = finalWidth,
            Height = finalHeight,
            CapturedAt = DateTime.UtcNow
        };

        // Phase 4.6.E.A.2 full-screen sibling. Originally declared but the
        // wiring was never landed — the IncludeFullScreen flag was set by
        // TestRunner but ignored here, AND silently dropped by HarnessClient
        // (fixed in commit 684050d). With both fixed, this now actually
        // produces `{baseName}.fullscreen.png`. Done BEFORE the GIF frame loop
        // so the full-screen snapshot matches the main static PNG temporally.
        if (settings.IncludeFullScreen)
        {
            try
            {
                var fsPath = FullScreenCapture.DeriveFullScreenPath(settings.OutputPath);
                FullScreenCapture.Capture(fsPath);
                result.FullScreenPath = fsPath;
            }
            catch
            {
                // Best-effort — the viewport PNG is the authoritative artifact.
                // Failures here (display affinity, locked DC, multi-monitor edge
                // cases) shouldn't fail the checkpoint.
            }
        }

        // Phase 4.6.F Session B: GIF frame capture. After the main PNG, capture
        // N additional viewport frames at the requested interval, saving each as
        // a sibling PNG `{baseName}.frame{NN:D2}.png`. The orchestrator (TestRunner)
        // encodes them into the final animated GIF via ImageSharp's GifEncoder.
        // Frame capture must stay on the Rhino UI thread (RhinoAgent already
        // marshals this whole method via InvokeOnUiThread).
        // Bypassed when the orchestrator's per-frame scrub path is taking over
        // (Session B+) — TestRunner sets RecordGif=false in that case so this
        // branch correctly does nothing.
        if (settings.RecordGif && settings.GifFrameCount > 0)
        {
            CaptureGifFrames(view, settings, size, result);
        }

        return result;
    }

    private static void CaptureGifFrames(RhinoView view, CaptureSettings settings, Size size, ScreenshotResult result)
    {
        var dir = System.IO.Path.GetDirectoryName(settings.OutputPath) ?? string.Empty;
        var baseName = System.IO.Path.GetFileNameWithoutExtension(settings.OutputPath);
        int interval = System.Math.Max(0, settings.GifFrameIntervalMs);

        for (int i = 0; i < settings.GifFrameCount; i++)
        {
            if (interval > 0) Thread.Sleep(interval);
            view.Document?.Views.Redraw();
            RhinoApp.Wait();
            view.Redraw();
            RhinoApp.Wait();

            var framePath = System.IO.Path.Combine(dir, $"{baseName}.frame{i:D2}.png");
            using var frameBmp = view.CaptureToBitmap(size);
            if (frameBmp == null)
            {
                // Single-frame failure is non-fatal — log via the result, skip this frame.
                // The orchestrator's GIF encoder tolerates a partial frame list.
                continue;
            }
            frameBmp.Save(framePath, ImageFormat.Png);
            result.FramePaths.Add(framePath);
        }
    }
}
