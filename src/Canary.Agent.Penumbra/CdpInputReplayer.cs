using System.Diagnostics;
using Canary.Input;

namespace Canary.Agent.Penumbra;

/// <summary>
/// Replays Canary InputRecording files via CDP mouse events instead of Win32 SendInput.
///
/// This is the Penumbra equivalent of <see cref="InputReplayer"/>. The key differences:
///   - InputReplayer: denormalize (vx,vy) → screen pixels → 65535-absolute → SendInput
///   - CdpInputReplayer: denormalize (vx,vy) → CSS canvas pixels → add page offset → CDP dispatchMouseEvent
///
/// No window sizing problems. No DPI conversion. No GetClientRect.
/// The canvas size is locked by the bridge agent and never changes during replay.
/// </summary>
public sealed class CdpInputReplayer
{
    private readonly InputRecording _recording;
    private readonly PenumbraBridgeAgent _agent;
    private readonly double _speedMultiplier;
    private readonly IReadOnlySet<long> _checkpointTimesMs;
    private readonly Func<long, Task>? _onCheckpoint;

    /// <summary>
    /// Creates a CDP input replayer.
    /// </summary>
    /// <param name="recording">The recorded input sequence.</param>
    /// <param name="agent">The bridge agent (provides DispatchCanvasMouseEventAsync).</param>
    /// <param name="speedMultiplier">Replay speed (1.0 = original, 2.0 = double speed).</param>
    /// <param name="checkpointTimesMs">Timestamps at which to pause and call the checkpoint handler.</param>
    /// <param name="onCheckpoint">Called at each checkpoint timestamp.</param>
    public CdpInputReplayer(
        InputRecording recording,
        PenumbraBridgeAgent agent,
        double speedMultiplier = 1.0,
        IReadOnlySet<long>? checkpointTimesMs = null,
        Func<long, Task>? onCheckpoint = null)
    {
        _recording = recording;
        _agent = agent;
        _speedMultiplier = speedMultiplier;
        _checkpointTimesMs = checkpointTimesMs ?? new HashSet<long>();
        _onCheckpoint = onCheckpoint;
    }

    /// <summary>
    /// Replay all recorded events via CDP mouse/keyboard events.
    /// Follows the same timing and checkpoint logic as InputReplayer.ReplayAsync.
    /// </summary>
    /// <param name="ct">Cancellation token — stops replay immediately.</param>
    public async Task ReplayAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var processedCheckpoints = new HashSet<long>();

        // Move cursor to canvas center before replay (same as MoveCursorToHome)
        await _agent.DispatchCanvasMouseEventAsync("mouseMoved", 0.5, 0.5, ct: ct).ConfigureAwait(false);
        await Task.Delay(100, ct).ConfigureAwait(false);

        foreach (var evt in _recording.Events)
        {
            ct.ThrowIfCancellationRequested();

            // Check for checkpoints before this event
            foreach (var cpTime in _checkpointTimesMs)
            {
                if (!processedCheckpoints.Contains(cpTime) &&
                    cpTime <= evt.TimestampMs &&
                    _onCheckpoint != null)
                {
                    processedCheckpoints.Add(cpTime);

                    // Wait for correct time
                    var cpTargetMs = (long)(cpTime / _speedMultiplier);
                    var cpRemaining = cpTargetMs - stopwatch.ElapsedMilliseconds;
                    if (cpRemaining > 0)
                        await Task.Delay((int)cpRemaining, ct).ConfigureAwait(false);

                    await _onCheckpoint(cpTime).ConfigureAwait(false);
                }
            }

            // Wait for correct timestamp
            var targetMs = (long)(evt.TimestampMs / _speedMultiplier);
            var remaining = targetMs - stopwatch.ElapsedMilliseconds;
            if (remaining > 0)
                await Task.Delay((int)remaining, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            await DispatchEventAsync(evt, ct).ConfigureAwait(false);
        }

        // Fire remaining checkpoints after last event
        foreach (var cpTime in _checkpointTimesMs)
        {
            if (!processedCheckpoints.Contains(cpTime) && _onCheckpoint != null)
            {
                processedCheckpoints.Add(cpTime);
                await _onCheckpoint(cpTime).ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchEventAsync(InputEvent evt, CancellationToken ct)
    {
        switch (evt.Type)
        {
            case InputEventType.MouseMove:
                if (evt.ViewportX.HasValue && evt.ViewportY.HasValue)
                {
                    await _agent.DispatchCanvasMouseEventAsync(
                        "mouseMoved", evt.ViewportX.Value, evt.ViewportY.Value, ct: ct
                    ).ConfigureAwait(false);
                }
                break;

            case InputEventType.MouseDown:
                if (evt.ViewportX.HasValue && evt.ViewportY.HasValue)
                {
                    var button = MapButton(evt.Button);
                    await _agent.DispatchCanvasMouseEventAsync(
                        "mousePressed", evt.ViewportX.Value, evt.ViewportY.Value,
                        button, clickCount: 1, ct: ct
                    ).ConfigureAwait(false);
                }
                break;

            case InputEventType.MouseUp:
                if (evt.ViewportX.HasValue && evt.ViewportY.HasValue)
                {
                    var button = MapButton(evt.Button);
                    await _agent.DispatchCanvasMouseEventAsync(
                        "mouseReleased", evt.ViewportX.Value, evt.ViewportY.Value,
                        button, ct: ct
                    ).ConfigureAwait(false);
                }
                break;

            case InputEventType.MouseWheel:
                // CDP mouseWheel not yet implemented in bridge agent — skip for now
                // TODO: Add wheel event support via Input.dispatchMouseEvent with deltaX/deltaY
                break;

            case InputEventType.KeyDown:
            case InputEventType.KeyUp:
                // Keyboard events via CDP Input.dispatchKeyEvent
                // TODO: Implement key event mapping (.NET Keys → CDP key descriptors)
                break;
        }
    }

    private static string MapButton(MouseButton? button) => button switch
    {
        MouseButton.Left => "left",
        MouseButton.Right => "right",
        MouseButton.Middle => "middle",
        _ => "left"
    };
}
