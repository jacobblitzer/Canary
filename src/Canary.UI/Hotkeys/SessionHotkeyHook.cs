using System.Runtime.InteropServices;

namespace Canary.UI.Hotkeys;

internal sealed class SessionHotkeyHook : IDisposable
{
    private const int HOTKEY_ID_CAPTURE = 0x4011;
    private const int HOTKEY_ID_ANNOTATE = 0x4012;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_C = 0x43;
    private const int VK_A = 0x41;
    private const int WM_HOTKEY = 0x0312;

    private readonly Form _owner;
    private bool _registered;

    public event Action? CaptureRequested;
    public event Action? AnnotateRequested;

    public SessionHotkeyHook(Form owner)
    {
        _owner = owner;
    }

    public bool Register()
    {
        if (_registered) return true;
        var ok1 = RegisterHotKey(_owner.Handle, HOTKEY_ID_CAPTURE, MOD_CONTROL | MOD_SHIFT, VK_C);
        var ok2 = RegisterHotKey(_owner.Handle, HOTKEY_ID_ANNOTATE, MOD_CONTROL | MOD_SHIFT, VK_A);
        _registered = ok1 && ok2;
        if (!_registered)
        {
            if (ok1) UnregisterHotKey(_owner.Handle, HOTKEY_ID_CAPTURE);
            if (ok2) UnregisterHotKey(_owner.Handle, HOTKEY_ID_ANNOTATE);
        }
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_owner.Handle, HOTKEY_ID_CAPTURE);
        UnregisterHotKey(_owner.Handle, HOTKEY_ID_ANNOTATE);
        _registered = false;
    }

    public bool ProcessMessage(ref Message m)
    {
        if (m.Msg != WM_HOTKEY) return false;
        var id = m.WParam.ToInt32();
        if (id == HOTKEY_ID_CAPTURE) { CaptureRequested?.Invoke(); return true; }
        if (id == HOTKEY_ID_ANNOTATE) { AnnotateRequested?.Invoke(); return true; }
        return false;
    }

    public void Dispose() => Unregister();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
