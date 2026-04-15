using System.Runtime.InteropServices;

namespace Canary.Input;

/// <summary>
/// Positions the target application and Canary UI to deterministic,
/// non-overlapping locations for consistent recording and replay.
/// Target window is 2/3 of primary screen width and height, placed at top-left.
/// </summary>
public static class WindowPositioner
{
    /// <summary>Default screen X for the target application window.</summary>
    public const int TargetX = 0;

    /// <summary>Default screen Y for the target application window.</summary>
    public const int TargetY = 0;

    /// <summary>Gap in pixels between target window and Canary UI.</summary>
    public const int Gap = 8;

    /// <summary>Target window width: 2/3 of primary screen width.</summary>
    public static int TargetWidth => ViewportLocator.GetPrimaryScreenWidth() * 2 / 3;

    /// <summary>Target window height: 2/3 of primary screen height.</summary>
    public static int TargetHeight => ViewportLocator.GetPrimaryScreenHeight() * 2 / 3;

    /// <summary>
    /// Position the target application window at the deterministic location (top-left, 2/3 screen).
    /// </summary>
    public static bool PositionTargetWindow(IntPtr targetHwnd)
    {
        return ViewportLocator.PositionWindow(targetHwnd, TargetX, TargetY, TargetWidth, TargetHeight);
    }

    /// <summary>
    /// Position the Canary UI to the right of the target app window.
    /// </summary>
    public static void PositionCanaryWindow(IntPtr canaryHwnd, int canaryWidth, int canaryHeight)
    {
        int x = TargetX + TargetWidth + Gap;
        int screenWidth = ViewportLocator.GetPrimaryScreenWidth();
        int availableWidth = screenWidth - x;
        int finalWidth = Math.Min(canaryWidth, Math.Max(400, availableWidth));

        ViewportLocator.PositionWindow(canaryHwnd, x, TargetY, finalWidth, canaryHeight);
    }

    /// <summary>
    /// Get the position for the abort overlay (top-right of target area, inset 8px).
    /// </summary>
    public static (int x, int y) GetOverlayPosition(int overlayWidth, int overlayHeight)
    {
        int x = TargetX + TargetWidth - overlayWidth - 8;
        int y = TargetY + 8;
        return (Math.Max(0, x), Math.Max(0, y));
    }

    /// <summary>
    /// Move the cursor to the center of the given viewport bounds.
    /// Call before recording or replay so the mouse always starts at the same spot.
    /// </summary>
    public static void MoveCursorToHome(ViewportBounds viewportBounds)
    {
        int cx = viewportBounds.X + viewportBounds.Width / 2;
        int cy = viewportBounds.Y + viewportBounds.Height / 2;
        SetCursorPos(cx, cy);
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
}
