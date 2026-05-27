using System.Runtime.InteropServices;

namespace Canary.UI.Avalonia.Hotkeys;

// Win32 global Pause-key abort hotkey against the Avalonia main
// window's HWND. Mirrors Canary.UI.Services.AbortHotkey (WinForms);
// uses Comctl32 window-subclassing to intercept WM_HOTKEY because
// Avalonia doesn't expose a WndProc message filter on Windows.
//
// Pattern + subclass plumbing is the same shape as
// Canary.UI.Avalonia.Hotkeys.SessionHotkeyHook — kept separate
// because the two hotkeys have different lifecycles (Session hook
// is armed while a supervised session runs; Abort hook is armed
// while a TestRunner suite runs).
internal sealed class AbortHotkey : IDisposable
{
    private const int HOTKEY_ID = 0x0042;
    private const int VK_PAUSE = 0x13;
    private const int MOD_NONE = 0x0000;
    private const uint WM_HOTKEY = 0x0312;

    private readonly IntPtr _hwnd;
    private readonly SubclassProc _subclassProc;
    private readonly UIntPtr _subclassId = (UIntPtr)0x4200_4242;
    private bool _registered;
    private bool _subclassed;

    public event Action? AbortRequested;

    public AbortHotkey(IntPtr hwnd)
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
        _registered = RegisterHotKey(_hwnd, HOTKEY_ID, MOD_NONE, VK_PAUSE);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_hwnd, HOTKEY_ID);
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
        if (uMsg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            try { AbortRequested?.Invoke(); } catch { }
            return IntPtr.Zero;
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
