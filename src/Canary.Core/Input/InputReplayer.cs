using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Canary.Input;

/// <summary>
/// Callback invoked when the replayer reaches a checkpoint timestamp.
/// The replayer pauses until the callback completes.
/// </summary>
/// <param name="checkpointTimeMs">The timestamp in milliseconds at which the checkpoint was triggered.</param>
public delegate Task CheckpointCallback(long checkpointTimeMs);

/// <summary>
/// Replays a recorded input sequence by injecting events via Win32 SendInput.
/// </summary>
public sealed class InputReplayer
{
    private readonly InputRecording _recording;
    private readonly ViewportBounds _replayBounds;
    private readonly double _speedMultiplier;
    private readonly SortedSet<long> _checkpointTimesMs;
    private readonly CheckpointCallback? _onCheckpoint;
    private readonly IntPtr _targetWindow;

    /// <summary>
    /// Creates a replayer for the given recording.
    /// </summary>
    /// <param name="recording">The recorded input sequence to replay.</param>
    /// <param name="replayBounds">Current viewport bounds for coordinate denormalization.</param>
    /// <param name="speedMultiplier">Speed multiplier (1.0 = original, 2.0 = double speed).</param>
    /// <param name="checkpointTimesMs">Timestamps at which to pause and call the checkpoint callback.</param>
    /// <param name="onCheckpoint">Callback invoked at each checkpoint timestamp.</param>
    /// <param name="targetWindow">Window handle to bring to foreground before replay.</param>
    public InputReplayer(
        InputRecording recording,
        ViewportBounds replayBounds,
        double speedMultiplier = 1.0,
        IEnumerable<long>? checkpointTimesMs = null,
        CheckpointCallback? onCheckpoint = null,
        IntPtr targetWindow = default)
    {
        _recording = recording;
        _replayBounds = replayBounds;
        _speedMultiplier = speedMultiplier > 0 ? speedMultiplier : 1.0;
        _checkpointTimesMs = checkpointTimesMs != null ? new SortedSet<long>(checkpointTimesMs) : new SortedSet<long>();
        _onCheckpoint = onCheckpoint;
        _targetWindow = targetWindow;
    }

    /// <summary>
    /// Replay all events. Blocks until complete or cancelled.
    /// </summary>
    public async Task ReplayAsync(CancellationToken cancellationToken = default)
    {
        if (_recording.Events.Count == 0)
            return;

        // Bring target window to foreground so SendInput events reach it
        if (_targetWindow != IntPtr.Zero)
        {
            SetForegroundWindow(_targetWindow);
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        var stopwatch = Stopwatch.StartNew();
        var processedCheckpoints = new HashSet<long>();

        foreach (var evt in _recording.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check for checkpoints that should fire before this event
            foreach (var cpTime in _checkpointTimesMs)
            {
                if (cpTime <= evt.TimestampMs && !processedCheckpoints.Contains(cpTime))
                {
                    // Wait until the checkpoint time
                    var targetCpMs = (long)(cpTime / _speedMultiplier);
                    var remainingCp = targetCpMs - stopwatch.ElapsedMilliseconds;
                    if (remainingCp > 0)
                        await Task.Delay((int)remainingCp, cancellationToken).ConfigureAwait(false);

                    processedCheckpoints.Add(cpTime);
                    if (_onCheckpoint != null)
                        await _onCheckpoint(cpTime).ConfigureAwait(false);
                }
            }

            // Wait for the correct timestamp
            var targetMs = (long)(evt.TimestampMs / _speedMultiplier);
            var remaining = targetMs - stopwatch.ElapsedMilliseconds;
            if (remaining > 0)
                await Task.Delay((int)remaining, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            InjectEvent(evt);
        }

        // Fire any remaining checkpoints after the last event
        foreach (var cpTime in _checkpointTimesMs)
        {
            if (!processedCheckpoints.Contains(cpTime) && _onCheckpoint != null)
            {
                processedCheckpoints.Add(cpTime);
                await _onCheckpoint(cpTime).ConfigureAwait(false);
            }
        }
    }

    private void InjectEvent(InputEvent evt)
    {
        switch (evt.Type)
        {
            case InputEventType.MouseMove:
                InjectMouseMove(evt);
                break;
            case InputEventType.MouseDown:
                InjectMouseButton(evt, isDown: true);
                break;
            case InputEventType.MouseUp:
                InjectMouseButton(evt, isDown: false);
                break;
            case InputEventType.MouseWheel:
                InjectMouseWheel(evt, horizontal: false);
                break;
            case InputEventType.MouseHWheel:
                InjectMouseWheel(evt, horizontal: true);
                break;
            case InputEventType.KeyDown:
                InjectKey(evt, isDown: true);
                break;
            case InputEventType.KeyUp:
                InjectKey(evt, isDown: false);
                break;
        }
    }

    private void InjectMouseMove(InputEvent evt)
    {
        if (evt.ViewportX == null || evt.ViewportY == null) return;
        var (screenX, screenY) = ViewportLocator.DenormalizeCoord(evt.ViewportX.Value, evt.ViewportY.Value, _replayBounds);
        var (absX, absY) = ViewportLocator.ScreenToAbsolute(screenX, screenY);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void InjectMouseButton(InputEvent evt, bool isDown)
    {
        if (evt.ViewportX == null || evt.ViewportY == null) return;
        var (screenX, screenY) = ViewportLocator.DenormalizeCoord(evt.ViewportX.Value, evt.ViewportY.Value, _replayBounds);
        var (absX, absY) = ViewportLocator.ScreenToAbsolute(screenX, screenY);

        uint flags = MOUSEEVENTF_ABSOLUTE;
        switch (evt.Button)
        {
            case MouseButton.Left:
                flags |= isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
                break;
            case MouseButton.Right:
                flags |= isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
                break;
            case MouseButton.Middle:
                flags |= isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
                break;
        }

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = flags
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void InjectMouseWheel(InputEvent evt, bool horizontal)
    {
        if (evt.ViewportX == null || evt.ViewportY == null) return;
        var (screenX, screenY) = ViewportLocator.DenormalizeCoord(evt.ViewportX.Value, evt.ViewportY.Value, _replayBounds);
        var (absX, absY) = ViewportLocator.ScreenToAbsolute(screenX, screenY);

        var wheelFlag = horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL;
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    mouseData = (uint)(evt.WheelDelta ?? 0),
                    dwFlags = wheelFlag | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void InjectKey(InputEvent evt, bool isDown)
    {
        if (evt.Key == null) return;

        if (!Enum.TryParse<VirtualKey>(evt.Key, out var vk))
            return;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    dwFlags = isDown ? 0u : KEYEVENTF_KEYUP
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    #region P/Invoke

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private enum VirtualKey : ushort
    {
        Back = 0x08, Tab = 0x09, Return = 0x0D, ShiftKey = 0x10,
        ControlKey = 0x11, Menu = 0x12, Pause = 0x13, Capital = 0x14,
        Escape = 0x1B, Space = 0x20, Prior = 0x21, Next = 0x22,
        End = 0x23, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28,
        Snapshot = 0x2C, Insert = 0x2D, Delete = 0x2E,
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
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    #endregion
}
