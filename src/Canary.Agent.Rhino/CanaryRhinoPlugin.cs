using System;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using Rhino.PlugIns;

namespace Canary.Agent.Rhino;

/// <summary>
/// Rhino plugin that hosts the Canary agent server.
/// On load, starts an <see cref="AgentServer"/> on a background thread listening for
/// harness connections over a named pipe.
/// </summary>
public sealed class CanaryRhinoPlugin : PlugIn
{
    private AgentServer? _server;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private CancellationTokenSource? _popupCts;
    private Task? _popupTask;

    /// <summary>
    /// Gets the singleton plugin instance.
    /// </summary>
    public static CanaryRhinoPlugin? Instance { get; private set; }

    /// <inheritdoc/>
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    /// <inheritdoc/>
    public CanaryRhinoPlugin()
    {
        Instance = this;
    }

    /// <inheritdoc/>
    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        var pipeName = $"canary-rhino-{pid}";

        RhinoApp.WriteLine($"[Canary] Starting agent on pipe '{pipeName}'...");

        // Bypass native-crash JIT debugger dialogs (bug 0016): when a native access
        // violation fires inside a Grasshopper component (e.g. cpig_native.dll),
        // Windows shows a "choose a debugger" JIT dialog that blocks the UI thread.
        // The harness then times out with a generic "did not respond" because the
        // RPC handler (which runs on the UI thread) can't return. SEM_NOGPFAULTERRORBOX
        // + SEM_NOOPENFILEERRORBOX make the process terminate immediately on a native
        // fault instead of showing a dialog — the harness sees the pipe disconnect
        // and reports a clear error. This also disables the GPF error box so we don't
        // hang waiting for a user to dismiss it during an automated test run.
        SuppressCrashDialogs();

        _cts = new CancellationTokenSource();
        var agent = new RhinoAgent();
        _server = new AgentServer(pipeName, agent);

        _serverTask = Task.Run(async () =>
        {
            try
            {
                await _server.RunAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"[Canary] Agent server error: {ex.Message}");
            }
        });

        RhinoApp.WriteLine($"[Canary] Agent listening on pipe '{pipeName}'.");

        // Start the popup-dismisser at plugin load time so that Rhino startup
        // popups (Plug-in Load Errors, Component Loader Errors, missing-
        // assembly warnings from third-party plugins) get auto-OK'd before
        // the harness's first action even arrives. This also covers popups
        // that appear DURING test runs (e.g. a CPig native crash that
        // triggers a Rhino crash dialog).
        _popupCts = new CancellationTokenSource();
        _popupTask = Task.Run(() => RhinoAgent.PopupDismisserPublic(_popupCts.Token));

        return LoadReturnCode.Success;
    }

    /// <inheritdoc/>
    protected override void OnShutdown()
    {
        _popupCts?.Cancel();
        _cts?.Cancel();

        if (_serverTask != null)
        {
            try { _serverTask.Wait(TimeSpan.FromSeconds(3)); }
            catch (AggregateException) { }
        }
        if (_popupTask != null)
        {
            try { _popupTask.Wait(TimeSpan.FromSeconds(1)); }
            catch (AggregateException) { }
        }

        _server?.Dispose();
        _cts?.Dispose();
        _popupCts?.Dispose();
        base.OnShutdown();
    }

    // ── Native crash dialog suppression (bug 0016) ──────────────────────────

    /// <summary>
    /// Windows error mode flags — SEM_NOGPFAULTERRORBOX makes the process
    /// terminate silently on a GPF/access violation instead of showing a JIT
    /// debugger dialog that blocks the UI thread during automated test runs.
    /// </summary>
    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetErrorMode(uint uMode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetErrorMode();

    /// <summary>
    /// Suppresses Windows JIT debugger dialogs for native crashes. When a native
    /// access violation fires (e.g. in cpig_native.dll), the process terminates
    /// immediately instead of showing a "choose a debugger" dialog. This prevents
    /// the harness from hanging for the full RPC timeout waiting for a dialog no
    /// one will dismiss. The pipe disconnects, and the harness reports a clear error.
    /// </summary>
    private static void SuppressCrashDialogs()
    {
        try
        {
            // Preserve existing flags, add the two that suppress crash dialogs.
            uint existing = GetErrorMode();
            uint desired = existing | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX | SEM_FAILCRITICALERRORS;
            SetErrorMode(desired);
            RhinoApp.WriteLine($"[Canary] Crash dialogs suppressed (error mode: {desired:X}). " +
                               "Native faults will terminate instead of showing a JIT debugger dialog.");
        }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"[Canary] Warning: could not suppress crash dialogs: {ex.Message}");
        }
    }
}
