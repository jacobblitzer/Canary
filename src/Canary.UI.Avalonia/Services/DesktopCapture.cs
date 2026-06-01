using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Canary.UI.Avalonia.Services;

/// <summary>
/// On-demand desktop screenshot for the test runner — captures the primary screen
/// at native resolution to a PNG. Used by the "Capture Screen" button so the
/// operator can grab warning balloons / modal toasts / external errors during
/// a live test run without leaving the Canary window.
///
/// Companion to Canary.Agent.Rhino.FullScreenCapture (Canary CPig.Kinematics
/// 4.6.E.A.2). That captures inside the Rhino process at checkpoint boundaries.
/// THIS captures from the Canary.UI process at any time the operator clicks.
/// </summary>
public static class DesktopCapture
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsInt(int nIndex);

    private const int SM_XVIRTUALSCREEN  = 76;
    private const int SM_YVIRTUALSCREEN  = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    /// <summary>
    /// Captures the entire virtual desktop (all monitors) to a PNG.
    /// Returns (filePath, width, height).
    /// </summary>
    public static (string Path, int Width, int Height) Capture(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("outputPath is empty", nameof(outputPath));

        var dir = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Use the *virtual* screen rect — covers all monitors on a multi-display
        // setup. The Canary.Agent.Rhino sibling uses Screen.PrimaryScreen only;
        // here we go wider because the operator may have Rhino on a secondary
        // and Canary.UI on the primary, and we want the warning to show up
        // regardless of which monitor it lives on.
        int x = (int)GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = (int)GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = (int)GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int h = (int)GetSystemMetrics(SM_CYVIRTUALSCREEN);

        // Fallbacks for cases where the virtual-screen metrics return 0
        // (rare; happens when running headless or in unusual session states).
        if (w <= 0 || h <= 0) { x = 0; y = 0; w = 1920; h = 1080; }

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(new Point(x, y), Point.Empty, new Size(w, h), CopyPixelOperation.SourceCopy);
        }
        bmp.Save(outputPath, ImageFormat.Png);
        return (outputPath, w, h);
    }

    /// <summary>
    /// Default capture directory: %APPDATA%\Canary\captures.
    /// Created if missing.
    /// </summary>
    public static string DefaultCaptureDir()
    {
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = System.IO.Path.Combine(appdata, "Canary", "captures");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Generate a fresh capture path under the default capture directory,
    /// timestamped with seconds precision.
    /// </summary>
    public static string NewCapturePath(string? label = null)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safeLabel = string.IsNullOrEmpty(label) ? "capture" : SanitizeForFileName(label!);
        return System.IO.Path.Combine(DefaultCaptureDir(), $"{stamp}-{safeLabel}.png");
    }

    private static string SanitizeForFileName(string s)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }
}
