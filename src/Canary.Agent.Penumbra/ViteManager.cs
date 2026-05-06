using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Canary.Agent.Penumbra;

/// <summary>
/// Manages the Vite dev server process for Penumbra's test harness.
/// Starts `npm run dev`, monitors stdout for the ready signal, and
/// cleanly kills on shutdown.
/// </summary>
public sealed partial class ViteManager : IDisposable
{
    private Process? _process;
    private readonly string _projectDir;
    private readonly int _port;
    private bool _disposed;

    /// <summary>
    /// Creates a new ViteManager.
    /// </summary>
    /// <param name="projectDir">Path to the Penumbra monorepo root.</param>
    /// <param name="port">Port for the Vite dev server (default 3000).</param>
    public ViteManager(string projectDir, int port = 3000)
    {
        _projectDir = projectDir;
        _port = port;
    }

    /// <summary>
    /// Start the Vite dev server and wait for it to be ready.
    /// </summary>
    /// <param name="timeout">How long to wait for Vite to start.</param>
    /// <param name="ct">Cancellation token — kills Vite if cancelled.</param>
    public async Task StartAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (_process != null)
            throw new InvalidOperationException("Vite is already running.");

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);

        // 2026-05-06 — Penumbra E5 Phase 7b/3 bring-up surfaced a
        // silent test correctness bug: a previous test run could
        // leave an orphaned node.exe listening on the dev port (the
        // child process spawned by `cmd /c npm run dev` can survive
        // its parent's kill on Windows). The new Vite instance hits
        // EADDRINUSE — but in some race conditions the harness reads
        // the prior server's still-buffered "ready" line BEFORE the
        // EADDRINUSE error arrives, treats startup as successful, and
        // then quietly serves files from the WRONG projectDir for
        // the rest of the test. Symptom: changes to the workload's
        // projectDir don't take effect; stale shader code is served.
        //
        // Fix: actively check the port before starting and kill any
        // process holding it. Aggressive but safe in CI/automation
        // contexts (no user-owned dev servers to preserve). The
        // warning log makes it visible when this fires so a manual
        // run noticing it can investigate.
        await KillStaleListenerAsync(_port, ct).ConfigureAwait(false);

        // Use cmd /c on Windows to run npm (npm is a .cmd file)
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

        // Penumbra Bug 0031 — suppress Vite's auto-open of the user's
        // default browser. Penumbra's vite.config.ts honors this env
        // var (open: !process.env.PENUMBRA_NO_AUTO_OPEN). Without it,
        // every Canary run launches TWO browser instances: the
        // auto-opened user-default-browser tab + Canary's own Chrome,
        // both connected to the dev server, both writing to the
        // shared penumbra-startup.log, both compiling Dawn pipelines
        // in parallel. See Penumbra docs/bugs/0031-*.md +
        // docs/research/2026-05-06-canary-vite-auto-open-fix-plan.md.
        psi.Environment["PENUMBRA_NO_AUTO_OPEN"] = "1";

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Vite dev server.");

        // Watch stdout for the ready signal
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            // Strip ANSI escape codes — Vite embeds them inside the port number
            // (e.g., "localhost:\x1b[1m3000\x1b[22m/"), breaking plain Contains().
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
            // Port already in use
            if (clean.Contains("EADDRINUSE") || clean.Contains("port is already in use"))
            {
                ready.TrySetException(new InvalidOperationException(
                    $"Port {_port} is already in use. Kill existing Vite process or choose a different port."));
            }
        };

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // Also detect early exit
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

    /// <summary>
    /// Stop the Vite dev server.
    /// </summary>
    public void Stop()
    {
        StopInternal();
    }

    /// <summary>
    /// The URL where the Vite dev server is running.
    /// </summary>
    public string Url => $"http://localhost:{_port}";

    /// <summary>
    /// Whether the Vite process is currently running.
    /// </summary>
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
        }
        catch { /* best effort */ }
        _process.Dispose();
        _process = null;
    }

    /// <summary>
    /// Strip ANSI escape sequences from a string.
    /// Vite embeds color codes inside port numbers, breaking string matching.
    /// </summary>
    private static string StripAnsi(string input) =>
        AnsiRegex().Replace(input, string.Empty);

    [GeneratedRegex(@"\x1B\[[0-9;]*[a-zA-Z]")]
    private static partial Regex AnsiRegex();

    /// <summary>
    /// If anything is listening on <paramref name="port"/>, kill it.
    /// Windows-specific: shells out to `netstat -ano` to find the
    /// owning PID, then `taskkill /F /PID`. No-op when the port is
    /// free.
    ///
    /// Used by <see cref="StartAsync"/> to scrub orphaned processes
    /// from prior runs so the new Vite always serves files from the
    /// expected projectDir. See the comment in StartAsync for the
    /// failure mode this prevents.
    /// </summary>
    private static async Task KillStaleListenerAsync(int port, CancellationToken ct)
    {
        var pid = FindListenerPid(port);
        if (pid is null) return;

        Console.WriteLine($"[ViteManager] Port {port} held by PID {pid} — killing stale listener before starting fresh dev server.");

        var killPsi = new ProcessStartInfo
        {
            FileName = "taskkill.exe",
            Arguments = $"/F /T /PID {pid.Value}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        try
        {
            using var killProc = Process.Start(killPsi);
            if (killProc is not null)
            {
                await killProc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ViteManager] taskkill failed: {ex.Message}. Vite startup may EADDRINUSE.");
            return;
        }

        // Brief wait for the OS to release the socket. TIME_WAIT can
        // hang for tens of seconds for a normal close but a forcibly-
        // killed process drops the socket immediately on Windows.
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false);

        // Confirm. If still occupied, log + continue — Vite's own
        // EADDRINUSE detection becomes the next line of defense.
        var stillPid = FindListenerPid(port);
        if (stillPid is not null)
        {
            Console.WriteLine($"[ViteManager] WARNING: Port {port} still held by PID {stillPid} after taskkill. Vite startup will likely fail with EADDRINUSE.");
        }
    }

    /// <summary>
    /// Returns the PID of the process listening on <paramref name="port"/>,
    /// or null if the port is free. Parses `netstat -ano` output —
    /// Windows-specific. Looks only at LISTENING sockets (ignores
    /// ESTABLISHED / TIME_WAIT entries).
    /// </summary>
    private static int? FindListenerPid(int port)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netstat.exe",
            Arguments = "-ano",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            // Lines look like:
            //   TCP    0.0.0.0:3000           0.0.0.0:0              LISTENING       12345
            //   TCP    [::]:3000              [::]:0                 LISTENING       12345
            //   TCP    [::1]:3000             [::]:0                 LISTENING       12345
            // We want the PID for any LISTENING socket on the chosen port.
            var portSuffix = $":{port}";
            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains("LISTENING")) continue;
                var trimmed = line.Trim();
                // Local address is the second field. Match if it ends with our port.
                var parts = trimmed.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                if (!parts[1].EndsWith(portSuffix)) continue;
                if (int.TryParse(parts[^1], out var pid)) return pid;
            }
        }
        catch
        {
            // Swallow — if netstat fails, fall through to "port appears free".
        }
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopInternal();
    }
}
