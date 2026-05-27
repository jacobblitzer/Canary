using System.Runtime.InteropServices;

namespace Canary.UI.Avalonia.Hotkeys;

// Win32 global hotkey hook against the Avalonia main window's HWND.
// Functional parity with Canary.UI.Hotkeys.SessionHotkeyHook (WinForms);
// uses Comctl32 window-subclassing to intercept WM_HOTKEY because
// Avalonia doesn't expose a WndProc message-filter on Windows.
internal sealed class SessionHotkeyHook : IDisposable
{
    private const int HOTKEY_ID_CAPTURE = 0x4011;
    private const int HOTKEY_ID_ANNOTATE = 0x4012;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_C = 0x43;
    private const int VK_A = 0x41;
    private const uint WM_HOTKEY = 0x0312;

    private readonly IntPtr _hwnd;
    private readonly SubclassProc _subclassProc;
    private readonly UIntPtr _subclassId = (UIntPtr)0x4310_4310;
    private bool _registered;
    private bool _subclassed;

    public event Action? CaptureRequested;
    public event Action? AnnotateRequested;

    public SessionHotkeyHook(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _subclassProc = WindowSubclassProc;
    }

    public bool Register()
    {
        if (_registered) return true;
        if (_hwnd == IntPtr.Zero) return false;

        if (!_subclassed)
        {
            _subclassed = SetWindowSubclass(_hwnd, _subclassProc, _subclassId, IntPtr.Zero);
            if (!_subclassed) return false;
        }

        var ok1 = RegisterHotKey(_hwnd, HOTKEY_ID_CAPTURE, MOD_CONTROL | MOD_SHIFT, VK_C);
        var ok2 = RegisterHotKey(_hwnd, HOTKEY_ID_ANNOTATE, MOD_CONTROL | MOD_SHIFT, VK_A);
        _registered = ok1 && ok2;
        if (!_registered)
        {
            if (ok1) UnregisterHotKey(_hwnd, HOTKEY_ID_CAPTURE);
            if (ok2) UnregisterHotKey(_hwnd, HOTKEY_ID_ANNOTATE);
        }
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_hwnd, HOTKEY_ID_CAPTURE);
        UnregisterHotKey(_hwnd, HOTKEY_ID_ANNOTATE);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        if (_subclassed)
        {
            RemoveWindowSubclass(_hwnd, _subclassProc, _subclassId);
            _subclassed = false;
        }
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == HOTKEY_ID_CAPTURE)
            {
                try { CaptureRequested?.Invoke(); } catch { }
                return IntPtr.Zero;
            }
            if (id == HOTKEY_ID_ANNOTATE)
            {
                try { AnnotateRequested?.Invoke(); } catch { }
                return IntPtr.Zero;
            }
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
