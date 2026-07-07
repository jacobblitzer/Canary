using System.Diagnostics;
using Canary.Agent;
using Canary.Config;
using Canary.Orchestration;
using Canary.Telemetry;

namespace Canary.Harness.Session;

/// <summary>
/// Adapter that wraps a launched Rhino process + its named-pipe <see cref="HarnessClient"/>
/// behind the <see cref="ICanaryAgent"/> contract so the existing
/// <see cref="Canary.Session.SupervisedSession"/> + REPL works against Rhino the same way
/// it does against the Qualia/Penumbra in-process bridges.
///
/// v1 scope (2026-06-02):
/// - Launches Rhino via <see cref="AppLauncher.Launch"/> and tracks the process for cleanup.
/// - Connects a <see cref="HarnessClient"/> to <c>canary-rhino-&lt;pid&gt;</c>.
/// - Forwards Execute / CaptureScreenshot / Heartbeat to the client.
/// - On <see cref="IAsyncDisposable.DisposeAsync"/>, disposes the pipe client + kills the Rhino
///   process (matching the test-runner cleanup pattern).
/// - No telemetry source yet (Rhino command-line + Slop log tail deferred to v2).
/// </summary>
internal sealed class RhinoSessionAgent : ICanaryAgent, ITelemetryAware, IProcessBackedAgent, IAsyncDisposable
{
    private readonly Process _process;
    private readonly HarnessClient _client;
    private PenumbraPreviewTelemetryTail? _penumbraTail;

    // Death certificate (flight-recorder Phase A): recorded by the Exited handler so the session
    // can distinguish "Rhino died/was killed mid-session" from our own tear-down kill. Volatile
    // int flag because the Exited event fires on a threadpool thread.
    private volatile bool _exited;
    private int _exitCode;
    private DateTime _exitUtc;

    private RhinoSessionAgent(Process process, HarnessClient client, Canary.Session.SessionLaunchInfo launchInfo)
    {
        _process = process;
        _client = client;
        LaunchInfo = launchInfo;
        try
        {
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) =>
            {
                try
                {
                    _exitCode = _process.ExitCode;
                    _exitUtc = DateTime.UtcNow;
                }
                catch { _exitCode = int.MinValue; _exitUtc = DateTime.UtcNow; }
                _exited = true;
            };
            // Race guard: if it exited between Start and the handler hookup, record now.
            if (_process.HasExited && !_exited)
            {
                try { _exitCode = _process.ExitCode; } catch { _exitCode = int.MinValue; }
                _exitUtc = DateTime.UtcNow;
                _exited = true;
            }
        }
        catch { }
    }

    /// <summary>Launch facts (pid, app path, applied env incl. the injected PENUMBRA_SESSION_REF).</summary>
    public Canary.Session.SessionLaunchInfo LaunchInfo { get; }

    public int ProcessId => LaunchInfo.ProcessId ?? -1;
    public bool ProcessHasExited => _exited;

    public bool TryGetProcessExit(out int exitCode, out DateTime exitUtc)
    {
        // Volatile acquire-read of the flag FIRST: the Exited handler publishes data then flag,
        // so flag-then-data here can never observe half-written exit facts (a torn read would
        // misclassify a kill as a clean exit-0 close — the exact record this exists to get right).
        if (!_exited)
        {
            exitCode = 0;
            exitUtc = default;
            return false;
        }
        exitCode = _exitCode;
        exitUtc = _exitUtc;
        return true;
    }

    /// <summary>v2 telemetry source: tail Penumbra's in-Rhino preview NDJSON into the session sink so the
    /// SESSION_REPORT captures scene.loaded (+tape/+grid, bounds), gl.field.transform (gumball moves),
    /// rep.live (display-rep switches), frame.real, and render.error — the Rhino analogue of the CDP Console
    /// stream the Qualia/Penumbra agents register. Tails from the file's current end (this session's events
    /// only). No-op + harmless if the Penumbra plug-in isn't loaded / never renders (empty telemetry = signal).</summary>
    public void RegisterTelemetrySink(ITelemetrySink sink)
    {
        try { _penumbraTail = PenumbraPreviewTelemetryTail.Start(sink); } catch { }
    }

    public static async Task<RhinoSessionAgent> CreateAsync(
        WorkloadConfig workload, Canary.Session.SessionLaunchContext? context, CancellationToken ct)
    {
        Process? launched = null;
        HarnessClient? client = null;
        try
        {
            // Per-spawn correlation ref rides the launcher's injected-env path — NOT the
            // PENUMBRA_* auto-forward, which strips process-only vars (verifier finding).
            Dictionary<string, string>? extraEnv = context == null
                ? null
                : new Dictionary<string, string> { ["PENUMBRA_SESSION_REF"] = context.SessionRef };
            var launch = AppLauncher.LaunchWithEnv(workload, extraEnv);
            launched = launch.Process;
            var launchInfo = new Canary.Session.SessionLaunchInfo
            {
                AppPath = workload.AppPath,
                ProcessId = launched.Id,
                AppliedEnv = launch.AppliedEnv,
            };
            var pipeName = $"{workload.PipeName}-{launched.Id}";
            client = new HarnessClient(pipeName, TimeSpan.FromMilliseconds(workload.ExecuteTimeoutMs))
            {
                // Pass the target PID so breakpoint detection can fire on timeout (bug 0016).
                TargetProcessId = launched.Id
            };
            await client.ConnectAsync(workload.StartupTimeoutMs, ct).ConfigureAwait(false);
            var hb = await client.HeartbeatAsync(ct).ConfigureAwait(false);
            if (!hb.Ok)
                throw new InvalidOperationException("Rhino agent heartbeat returned ok=false after pipe connect.");
            var agent = new RhinoSessionAgent(launched, client, launchInfo);
            launched = null; client = null; // ownership transferred
            return agent;
        }
        finally
        {
            client?.Dispose();
            if (launched != null && !launched.HasExited)
            {
                // 2026-06-23 — error-path: agent setup failed. Kill node.exe children of the
                // half-launched Rhino BEFORE killing it; sweep any orphans afterward.
                try { Canary.Orchestration.OrphanNodeCleaner.KillChildrenOf(launched.Id, "create-error-path"); } catch { }
                try { launched.Kill(entireProcessTree: true); } catch { }
                launched.Dispose();
                try { Canary.Orchestration.OrphanNodeCleaner.KillOrphans("create-error-path"); } catch { }
            }
        }
    }

    public Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters)
        => _client.ExecuteAsync(action, parameters);

    public Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings)
        => _client.CaptureScreenshotAsync(settings);

    public async Task<HeartbeatResult> HeartbeatAsync()
        => await _client.HeartbeatAsync(CancellationToken.None).ConfigureAwait(false);

    public Task AbortAsync()
    {
        // Rhino agent doesn't have a dedicated abort surface today. Dispose
        // tears the process down; that's the only graceful exit.
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try { _penumbraTail?.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }
        if (!_process.HasExited)
        {
            // 2026-06-23 — kill node.exe children of THIS Rhino BEFORE killing it, so
            // they die with their parent rather than orphan. Operator opt-out:
            // CANARY_DISABLE_ORPHAN_KILL=1.
            try { Canary.Orchestration.OrphanNodeCleaner.KillChildrenOf(_process.Id, "session-dispose"); } catch { }
            try { _process.Kill(entireProcessTree: true); } catch { }
            try { await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        }
        _process.Dispose();
        // Post-kill sweep — catches anything still orphaned (e.g., the Rhino crashed
        // earlier before we could call KillChildrenOf).
        try { Canary.Orchestration.OrphanNodeCleaner.KillOrphans("session-dispose"); } catch { }
    }
}
