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
}
