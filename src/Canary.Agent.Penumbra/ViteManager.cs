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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopInternal();
    }
}
