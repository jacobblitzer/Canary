using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Canary.Agent.Qualia;

/// <summary>
/// Manages the Vite dev server process for the Qualia app.
/// Starts <c>npm run dev</c>, monitors stdout for the ready signal, and
/// cleanly kills on shutdown. Mirrors <see cref="Canary.Agent.Penumbra.ViteManager"/>;
/// kept separate so each workload's process management can diverge as
/// each app's quirks emerge (Penumbra has the auto-open env-var quirk
/// that Qualia doesn't).
/// </summary>
public sealed partial class ViteManager : IDisposable
{
    private Process? _process;
    private readonly string _projectDir;
    private readonly int _port;
    private bool _disposed;

    public ViteManager(string projectDir, int port = 5173)
    {
        _projectDir = projectDir;
        _port = port;
    }

    public async Task StartAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (_process != null)
            throw new InvalidOperationException("Vite is already running.");

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);

        // Same race as the Penumbra workload — an orphaned node.exe from a
        // prior run can still hold the dev port. Kill it first so the new
        // Vite always serves from the expected projectDir.
        await KillStaleListenerAsync(_port, ct).ConfigureAwait(false);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c npm run dev -- --port {_port} --strictPort",
            WorkingDirectory = _projectDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Vite dev server.");

        // Phase 6 / §C7 Tier 2 — voluntary spawn registration.
        Canary.Telemetry.SpawnRegistry.Default.Register(
            pid: _process.Id,
            name: "node.exe",
            commandLine: $"{psi.FileName} {psi.Arguments}",
            workingDirectory: _projectDir,
            port: _port,
            intent: $"Qualia Vite dev server (port {_port}, projectDir={_projectDir})");

        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            var clean = StripAnsi(e.Data);
            if (clean.Contains($"localhost:{_port}") || clean.Contains($"127.0.0.1:{_port}") ||
                clean.Contains("ready in"))
            {
                ready.TrySetResult(true);
            }
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            var clean = StripAnsi(e.Data);
            if (clean.Contains($"localhost:{_port}") || clean.Contains($"127.0.0.1:{_port}") ||
                clean.Contains("ready in"))
            {
                ready.TrySetResult(true);
            }
            if (clean.Contains("EADDRINUSE") || clean.Contains("port is already in use"))
            {
                ready.TrySetException(new InvalidOperationException(
                    $"Port {_port} is already in use. Kill existing Vite process or choose a different port."));
            }
        };

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        _ = Task.Run(async () =>
        {
            await _process.WaitForExitAsync(ct).ConfigureAwait(false);
            ready.TrySetException(new InvalidOperationException(
                $"Vite process exited unexpectedly with code {_process.ExitCode}"));
        }, ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            await ready.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            StopInternal();
            throw new TimeoutException(
                $"Vite dev server did not start within {effectiveTimeout.TotalSeconds}s. " +
                $"Check that 'npm run dev' works in {_projectDir}");
        }
        catch
        {
            StopInternal();
            throw;
        }
    }

    public void Stop() => StopInternal();

    public string Url => $"http://localhost:{_port}";

    public bool IsRunning => _process != null && !_process.HasExited;

    private void StopInternal()
    {
        if (_process == null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
            Canary.Telemetry.SpawnRegistry.Default.Unregister(_process.Id);
        }
        catch { /* best effort */ }
        _process.Dispose();
        _process = null;
    }

    private static string StripAnsi(string input) => AnsiRegex().Replace(input, string.Empty);

    [GeneratedRegex(@"\x1B\[[0-9;]*[a-zA-Z]")]
    private static partial Regex AnsiRegex();

    // Delegates to LocalhostManager.KillByPortAsync per Phase 4 / §C7 Tier 1
    // (previously duplicated between Penumbra + Qualia ViteManagers).
    private static async Task KillStaleListenerAsync(int port, CancellationToken ct)
    {
        var manager = new Canary.Localhost.LocalhostManager();
        await manager.KillByPortAsync(port, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopInternal();
    }
}
