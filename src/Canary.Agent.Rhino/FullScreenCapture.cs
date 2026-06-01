using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Canary.Agent.Rhino;

/// <summary>
/// Captures the entire desktop (primary monitor at native resolution) to a PNG file.
/// Used alongside <see cref="RhinoScreenCapture"/> when a test wants to also see
/// warning balloons, modal dialogs, or off-viewport errors that the viewport-only
/// path misses.
///
/// Companion to ADR-XXX (Phase 4.6.E.A.2 of CPig.Kinematics): operator-reported
/// that GUID-conflict warnings + post-warning toasts were invisible in viewport
/// screenshots and needed manual screenshot capture via Windows Snip Tool.
/// </summary>
public static class FullScreenCapture
{
    /// <summary>
    /// Captures the primary screen's full bounds to <paramref name="outputPath"/>.
    /// Returns the dimensions actually written.
    /// </summary>
    public static (int Width, int Height) Capture(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("outputPath is empty", nameof(outputPath));

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Primary screen bounds — covers the monitor Rhino is on (typically the
        // primary). Multi-monitor + secondary-displays-with-warning-toasts is a
        // future enhancement (Screen.AllScreens enumeration).
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        }
        bmp.Save(outputPath, ImageFormat.Png);
        return (bounds.Width, bounds.Height);
    }

    /// <summary>
    /// Derive the conventional full-screen-capture path from a viewport-capture path
    /// — appends <c>.fullscreen.png</c> before the file extension.
    ///
    /// e.g. <c>"runs/foo/cpig-kin-08.png"</c> → <c>"runs/foo/cpig-kin-08.fullscreen.png"</c>.
    /// </summary>
    public static string DeriveFullScreenPath(string viewportPath)
    {
        if (string.IsNullOrEmpty(viewportPath)) return string.Empty;
        var dir = Path.GetDirectoryName(viewportPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(viewportPath);
        return Path.Combine(dir, baseName + ".fullscreen.png");
    }
}
