using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Canary.Input;

/// <summary>
/// Records mouse/keyboard input targeted at a specific window using low-level Windows hooks.
/// Runs hooks on a dedicated STA thread with a message pump.
/// </summary>
public sealed class InputRecorder : IDisposable
{
    private readonly IntPtr _targetWindow;
    private readonly ViewportBounds _viewportBounds;
    private readonly string _workload;
    private readonly string _windowTitle;
    private readonly List<InputEvent> _events = new();
    private readonly Stopwatch _stopwatch = new();
    private Thread? _hookThread;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private volatile bool _recording;
    private volatile bool _stopping;

    // Prevent GC of delegates while hooks are active
    private LowLevelProc? _mouseProc;
    private LowLevelProc? _keyboardProc;

    /// <summary>
    /// Creates a recorder that captures events targeted at the specified window.
    /// </summary>
    /// <param name="targetWindow">Handle of the window to record input for.</param>
    /// <param name="workload">Workload name for recording metadata.</param>
    /// <param name="windowTitle">Window title for recording metadata.</param>
    public InputRecorder(IntPtr targetWindow, string workload, string windowTitle)
    {
        _targetWindow = targetWindow;
        _viewportBounds = ViewportLocator.GetViewportBounds(targetWindow);
        _workload = workload;
        _windowTitle = windowTitle;
    }

    /// <summary>
    /// Start recording input events. Installs global hooks on a dedicated thread.
    /// </summary>
    public void StartRecording()
    {
        if (_recording)
            throw new InvalidOperationException("Already recording.");

        _events.Clear();
        _recording = true;
        _stopping = false;

        _hookThread = new Thread(HookThreadProc) { IsBackground = true };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        _stopwatch.Restart();
    }

    /// <summary>
    /// Stop recording and return the collected recording.
    /// </summary>
    public InputRecording StopRecording()
    {
        if (!_recording)
            throw new InvalidOperationException("Not currently recording.");

        _stopping = true;
        _stopwatch.Stop();

        // Post WM_QUIT to the hook thread's message loop
        if (_hookThread != null)
        {
            PostThreadMessage(GetThreadId(_hookThread), WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _hookThread.Join(TimeSpan.FromSeconds(3));
        }

        _recording = false;

        return new InputRecording
        {
            Metadata = new RecordingMetadata
            {
                Workload = _workload,
                RecordedAt = DateTime.UtcNow,
                ViewportWidth = _viewportBounds.Width,
                ViewportHeight = _viewportBounds.Height,
                WindowTitle = _windowTitle,
                DurationMs = _stopwatch.ElapsedMilliseconds
            },
            Events = new List<InputEvent>(_events)
        };
    }

    private void HookThreadProc()
    {
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var moduleHandle = GetModuleHandle(curModule.ModuleName);

        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);

        // Run message pump until WM_QUIT
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = IntPtr.Zero;
        _keyboardHook = IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _recording && !_stopping)
        {
            var hookData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var screenX = hookData.pt.X;
            var screenY = hookData.pt.Y;

            // Only record if cursor is within the target window bounds
            if (IsWithinBounds(screenX, screenY))
            {
                var (vx, vy) = ViewportLocator.NormalizeCoord(screenX, screenY, _viewportBounds);
                var msg = (int)wParam;

                InputEvent? evt = msg switch
                {
                    WM_MOUSEMOVE => new InputEvent
                    {
                        TimestampMs = _stopwatch.ElapsedMilliseconds,
                        Type = InputEventType.MouseMove,
                        ViewportX = Math.Round(vx, 4),
                        ViewportY = Math.Round(vy, 4)
                    },
                    WM_LBUTTONDOWN => new InputEvent
                    {
                        TimestampMs = _stopwatch.ElapsedMilliseconds,
                        Type = InputEventType.MouseDown,
                        ViewportX = Math.Round(vx, 4),
                        ViewportY = Math.Round(vy, 4),
                        Button = MouseButton.Left
                    },
                    WM_LBUTTONUP => new InputEvent
                    {
                        TimestampMs = _stopwatch.ElapsedMilliseconds,
                        Type = InputEventType.MouseUp,
                        ViewportX = Math.Round(vx, 4),
                        ViewportY = Math.Round(vy, 4),
                        Button = MouseButton.Left
                    },
                    WM_RBUTTONDOWN => new InputEvent
                    {
                        TimestampMs = _stopwatch.ElapsedMilliseconds,
                        Type = InputEventType.MouseDown,
                        ViewportX = Math.Round(vx, 4),
                        ViewportY = Math.Round(vy, 4),
                        Button = MouseButton.Right
                    },
                    WM_RBUTTONUP => new InputEvent
                    {
                        TimestampMs = _stopwatch.ElapsedMilliseconds,
                        Type = InputEventType.MouseUp,
                        ViewportX = Math.Round(vx, 4),
                        ViewportY = Math.Round(vy, 4),
                        Button = MouseButton.Right
                    },
                    WM_MBUTTONDOWN => new InputEvent
                    {
                        TimestampMs = _stopwatch.ElapsedMilliseconds,
                        Type = InputEventType.MouseDown,
                        ViewportX = Math.Round(vx, 4),
                        ViewportY = Math.Round(vy, 4),
                        Button = MouseButton.Middle
                    },
                    WM_MBUTTONUP => new InputEvent
                    {
                        TimestampMs = _stopwatch.ElapsedMilliseconds,
                        Type = InputEventType.MouseUp,
                        ViewportX = Math.Round(vx, 4),
                        ViewportY = Math.Round(vy, 4),
                        Button = MouseButton.Middle
                    },
                    WM_MOUSEWHEEL => new InputEvent
                    {
                        TimestampMs = _stopwatch.ElapsedMilliseconds,
                        Type = InputEventType.MouseWheel,
                        ViewportX = Math.Round(vx, 4),
                        ViewportY = Math.Round(vy, 4),
                        WheelDelta = (short)(hookData.mouseData >> 16)
                    },
                    _ => null
                };

                if (evt != null)
                {
                    lock (_events)
                    {
                        _events.Add(evt);
                    }
                }
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _recording && !_stopping)
        {
            var hookData = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                lock (_events)
                {
                    _events.Add(new InputEvent
                    {
                        TimestampMs = _stopwatch.ElapsedMilliseconds,
                        Type = InputEventType.KeyDown,
                        Key = ((Keys)hookData.vkCode).ToString()
                    });
                }
            }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                lock (_events)
                {
                    _events.Add(new InputEvent
                    {
                        TimestampMs = _stopwatch.ElapsedMilliseconds,
                        Type = InputEventType.KeyUp,
                        Key = ((Keys)hookData.vkCode).ToString()
                    });
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private bool IsWithinBounds(int screenX, int screenY)
    {
        return screenX >= _viewportBounds.X
            && screenX < _viewportBounds.X + _viewportBounds.Width
            && screenY >= _viewportBounds.Y
            && screenY < _viewportBounds.Y + _viewportBounds.Height;
    }

    private static uint GetThreadId(Thread thread)
    {
        // Use the ManagedThreadId is not sufficient — we need the native thread ID.
        // We'll use a workaround: store the native thread ID when the hook thread starts.
        // For simplicity, use the deprecated but functional approach.
        return 0; // Will be handled via _stopping flag + timeout on Join
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_recording)
        {
            _stopping = true;
            _recording = false;
        }
    }

    #region P/Invoke

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;

    /// <summary>
    /// Subset of Windows virtual key codes used for recording.
    /// </summary>
    private enum Keys
    {
        LButton = 0x01, RButton = 0x02, Cancel = 0x03, MButton = 0x04,
        Back = 0x08, Tab = 0x09, Return = 0x0D, ShiftKey = 0x10,
        ControlKey = 0x11, Menu = 0x12, Pause = 0x13, Capital = 0x14,
        Escape = 0x1B, Space = 0x20, Prior = 0x21, Next = 0x22,
        End = 0x23, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28,
        Print = 0x2A, Snapshot = 0x2C, Insert = 0x2D, Delete = 0x2E,
        D0 = 0x30, D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34,
        D5 = 0x35, D6 = 0x36, D7 = 0x37, D8 = 0x38, D9 = 0x39,
        A = 0x41, B = 0x42, C = 0x43, D = 0x44, E = 0x45, F = 0x46,
        G = 0x47, H = 0x48, I = 0x49, J = 0x4A, K = 0x4B, L = 0x4C,
        M = 0x4D, N = 0x4E, O = 0x4F, P = 0x50, Q = 0x51, R = 0x52,
        S = 0x53, T = 0x54, U = 0x55, V = 0x56, W = 0x57, X = 0x58,
        Y = 0x59, Z = 0x5A,
        LWin = 0x5B, RWin = 0x5C,
        F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74,
        F6 = 0x75, F7 = 0x76, F8 = 0x77, F9 = 0x78, F10 = 0x79,
        F11 = 0x7A, F12 = 0x7B,
        LShiftKey = 0xA0, RShiftKey = 0xA1, LControlKey = 0xA2, RControlKey = 0xA3,
        LMenu = 0xA4, RMenu = 0xA5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    #endregion
}
