using System.Runtime.InteropServices;
using System.Text;

namespace Canary.Input;

/// <summary>
/// Screen-space bounds of a viewport's client area.
/// </summary>
public readonly struct ViewportBounds
{
    /// <summary>Screen X of the top-left corner.</summary>
    public int X { get; }

    /// <summary>Screen Y of the top-left corner.</summary>
    public int Y { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Creates a new viewport bounds rectangle.
    /// </summary>
    public ViewportBounds(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Finds target windows and converts between screen and viewport-relative coordinates.
/// </summary>
public static class ViewportLocator
{
    /// <summary>
    /// Normalize a screen-space point to viewport-relative [0,1] coordinates.
    /// </summary>
    /// <param name="screenX">Screen X coordinate.</param>
    /// <param name="screenY">Screen Y coordinate.</param>
    /// <param name="bounds">Viewport bounds in screen space.</param>
    /// <returns>Normalized (vx, vy) in [0.0, 1.0].</returns>
    public static (double vx, double vy) NormalizeCoord(int screenX, int screenY, ViewportBounds bounds)
    {
        double vx = bounds.Width > 0 ? (double)(screenX - bounds.X) / bounds.Width : 0.0;
        double vy = bounds.Height > 0 ? (double)(screenY - bounds.Y) / bounds.Height : 0.0;
        return (vx, vy);
    }

    /// <summary>
    /// Denormalize viewport-relative [0,1] coordinates to screen-space pixel coordinates.
    /// </summary>
    /// <param name="vx">Normalized X [0.0, 1.0].</param>
    /// <param name="vy">Normalized Y [0.0, 1.0].</param>
    /// <param name="bounds">Viewport bounds in screen space.</param>
    /// <returns>Screen-space (x, y) pixel coordinates.</returns>
    public static (int screenX, int screenY) DenormalizeCoord(double vx, double vy, ViewportBounds bounds)
    {
        int screenX = bounds.X + (int)Math.Round(vx * bounds.Width);
        int screenY = bounds.Y + (int)Math.Round(vy * bounds.Height);
        return (screenX, screenY);
    }

    /// <summary>
    /// Convert screen pixel coordinates to SendInput absolute coordinates (0..65535).
    /// </summary>
    /// <param name="screenX">Screen X in pixels.</param>
    /// <param name="screenY">Screen Y in pixels.</param>
    /// <returns>Absolute (absX, absY) in 0..65535 range.</returns>
    public static (int absX, int absY) ScreenToAbsolute(int screenX, int screenY)
    {
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);

        int absX = screenWidth > 0 ? (screenX * 65535) / (screenWidth - 1) : 0;
        int absY = screenHeight > 0 ? (screenY * 65535) / (screenHeight - 1) : 0;
        return (absX, absY);
    }

    /// <summary>
    /// Find a window by title substring. Returns IntPtr.Zero if not found.
    /// </summary>
    /// <param name="titleSubstring">Substring to match in the window title.</param>
    /// <returns>Window handle, or IntPtr.Zero if not found.</returns>
    public static IntPtr FindWindowByTitle(string titleSubstring)
    {
        IntPtr result = IntPtr.Zero;
        var sb = new StringBuilder(256);

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
            {
                result = hWnd;
                return false; // stop enumeration
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>
    /// Get the screen-space bounds of a window's client area.
    /// </summary>
    /// <param name="hWnd">Window handle.</param>
    /// <returns>Viewport bounds, or a zero-size bounds if the handle is invalid.</returns>
    public static ViewportBounds GetViewportBounds(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return new ViewportBounds(0, 0, 0, 0);

        RECT clientRect;
        if (!GetClientRect(hWnd, out clientRect))
            return new ViewportBounds(0, 0, 0, 0);

        POINT topLeft = new() { X = 0, Y = 0 };
        ClientToScreen(hWnd, ref topLeft);

        return new ViewportBounds(
            topLeft.X,
            topLeft.Y,
            clientRect.Right - clientRect.Left,
            clientRect.Bottom - clientRect.Top);
    }

    /// <summary>
    /// Check whether a window handle refers to a visible, foreground-capable window.
    /// </summary>
    public static bool IsValidTarget(IntPtr hWnd)
    {
        return hWnd != IntPtr.Zero && IsWindowVisible(hWnd);
    }

    /// <summary>
    /// Move and resize a window to the specified position and outer size.
    /// </summary>
    public static bool PositionWindow(IntPtr hWnd, int x, int y, int width, int height)
    {
        if (hWnd == IntPtr.Zero) return false;
        return MoveWindow(hWnd, x, y, width, height, bRepaint: true);
    }

    /// <summary>Get the width of the primary screen in pixels.</summary>
    public static int GetPrimaryScreenWidth() => GetSystemMetrics(SM_CXSCREEN);

    /// <summary>Get the height of the primary screen in pixels.</summary>
    public static int GetPrimaryScreenHeight() => GetSystemMetrics(SM_CYSCREEN);

    #region P/Invoke

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    #endregion
}
