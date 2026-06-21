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
internal sealed class RhinoSessionAgent : ICanaryAgent, ITelemetryAware, IAsyncDisposable
{
    private readonly Process _process;
    private readonly HarnessClient _client;
    private PenumbraPreviewTelemetryTail? _penumbraTail;

    private RhinoSessionAgent(Process process, HarnessClient client)
    {
        _process = process;
        _client = client;
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

    public static async Task<RhinoSessionAgent> CreateAsync(WorkloadConfig workload, CancellationToken ct)
    {
        Process? launched = null;
        HarnessClient? client = null;
        try
        {
            launched = AppLauncher.Launch(workload);
            var pipeName = $"{workload.PipeName}-{launched.Id}";
            client = new HarnessClient(pipeName, TimeSpan.FromSeconds(120));
            await client.ConnectAsync(workload.StartupTimeoutMs, ct).ConfigureAwait(false);
            var hb = await client.HeartbeatAsync(ct).ConfigureAwait(false);
            if (!hb.Ok)
                throw new InvalidOperationException("Rhino agent heartbeat returned ok=false after pipe connect.");
            var agent = new RhinoSessionAgent(launched, client);
            launched = null; client = null; // ownership transferred
            return agent;
        }
        finally
        {
            client?.Dispose();
            if (launched != null && !launched.HasExited)
            {
                try { launched.Kill(entireProcessTree: true); } catch { }
                launched.Dispose();
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
            try { _process.Kill(entireProcessTree: true); } catch { }
            try { await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        }
        _process.Dispose();
    }
}
