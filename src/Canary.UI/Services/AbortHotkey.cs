using System.Runtime.InteropServices;

namespace Canary.UI.Services;

/// <summary>
/// Registers the Pause key as a global abort hotkey.
/// Fires even when another app has focus.
/// </summary>
internal sealed class AbortHotkey : IDisposable
{
    private const int HOTKEY_ID = 0x0042;
    private const int VK_PAUSE = 0x13;
    private const int MOD_NONE = 0x0000;
    private const int WM_HOTKEY = 0x0312;

    private readonly Form _owner;
    private bool _registered;

    /// <summary>Fired when the Pause key is pressed globally.</summary>
    public event Action? AbortRequested;

    public AbortHotkey(Form owner)
    {
        _owner = owner;
    }

    /// <summary>Register the global Pause hotkey.</summary>
    public bool Register()
    {
        _registered = RegisterHotKey(_owner.Handle, HOTKEY_ID, MOD_NONE, VK_PAUSE);
        return _registered;
    }

    /// <summary>Unregister the global Pause hotkey.</summary>
    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_owner.Handle, HOTKEY_ID);
            _registered = false;
        }
    }

    /// <summary>
    /// Call from the owner Form's WndProc to handle WM_HOTKEY.
    /// </summary>
    public bool ProcessMessage(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
        {
            AbortRequested?.Invoke();
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        Unregister();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
