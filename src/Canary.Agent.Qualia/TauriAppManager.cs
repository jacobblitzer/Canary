using System.Diagnostics;
using System.Text.Json;

namespace Canary.Agent.Qualia;

/// <summary>
/// Launches the packaged Qualia Tauri exe for the desktop harness leg
/// (platform-foundation P1) and tears it down cleanly.
///
/// Key properties, all deliberate inversions of the web-leg helpers:
///   <list type="bullet">
///     <item><b>Isolated WebView2 profile</b> — every run gets a throwaway
///         <c>%TEMP%\canary-wv2-&lt;guid8&gt;</c> user-data-folder via
///         <c>WEBVIEW2_USER_DATA_FOLDER</c>, mirroring ChromeLauncher's temp
///         Chrome profile. The operator's daily-driver profile
///         (<c>%LOCALAPPDATA%\app.qualia.desktop\EBWebView</c>) is never
///         touched — no persona/settings clobbering, no FSA-handle
///         nondeterminism, deterministic first-boot state.</item>
///     <item><b>CDP via env var</b> — WebView2 only honors
///         <c>--remote-debugging-port</c> through
///         <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c>; the Tauri exe does
///         not parse Chromium flags on argv (and argv[1] would be
///         misread as a file to open), so the exe is launched with ZERO
///         arguments.</item>
///     <item><b>No kill-by-port, ever</b> — unlike ViteManager/ChromeLauncher,
///         a stale listener on the CDP port is a FAILURE, not a kill
///         target: the likeliest holder is the operator's own instance.</item>
///     <item><b>Single-instance pre-check</b> — tauri-plugin-single-instance
///         (identifier app.qualia.desktop) makes a second exe forward to
///         the first and exit, which would also focus-steal the
///         operator's window. Detect and fail fast with a clear message
///         instead of launching into that behavior.</item>
///   </list>
/// </summary>
public sealed class TauriAppManager : IDisposable
{
    private readonly string _exePath;
    private readonly string _projectDir;
    private readonly int _cdpPort;
    private Process? _process;
    private string? _userDataDir;
    private bool _disposed;

    /// <summary>Origin the packaged app serves from (Windows WebView2).</summary>
    public string Url => "http://tauri.localhost/";

    /// <summary>CDP page-target websocket URL, set by <see cref="StartAsync"/>.</summary>
    public string? WebSocketUrl { get; private set; }

    public TauriAppManager(string exePath, string projectDir, int cdpPort)
    {
        _exePath = exePath;
        _projectDir = projectDir;
        _cdpPort = cdpPort;
    }

    public async Task StartAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (_process != null)
            throw new InvalidOperationException("Tauri app is already running.");
        if (string.IsNullOrWhiteSpace(_exePath) || !File.Exists(_exePath))
            throw new FileNotFoundException(
                $"Qualia desktop exe not found at '{_exePath}'. Build it first: npm run tauri:build in the Qualia repo.");

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(45);

        ThrowIfQualiaAlreadyRunning();
        SweepStaleProfiles();

        _userDataDir = Path.Combine(Path.GetTempPath(), $"canary-wv2-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(_userDataDir);

        // Abort race guard: the harness registers Ctrl+C → Dispose BEFORE
        // InitializeAsync runs, and the pre-launch checks above take long
        // enough that a cancellation can land between them and
        // Process.Start. Launching after that point would orphan the exe
        // (Dispose already ran and won't run again).
        ct.ThrowIfCancellationRequested();
        if (_disposed)
            throw new ObjectDisposedException(nameof(TauriAppManager), "Disposed before launch (aborted).");

        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            // Zero arguments — argv[1] would be parsed as a file-to-open.
            Arguments = string.Empty,
            // cwd = the Qualia repo so debug_write_file observations land in
            // <repo>/debug-logs/ exactly where the dev middleware writes them
            // (derive.mjs default obs dir works unchanged on both legs).
            WorkingDirectory = _projectDir,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        psi.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = $"--remote-debugging-port={_cdpPort}";
        psi.Environment["WEBVIEW2_USER_DATA_FOLDER"] = _userDataDir;

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start Qualia desktop exe at {_exePath}.");

        Canary.Telemetry.SpawnRegistry.Default.Register(
            pid: _process.Id,
            name: Path.GetFileName(_exePath),
            commandLine: _exePath,
            workingDirectory: _projectDir,
            port: _cdpPort,
            intent: $"Qualia desktop exe (CDP {_cdpPort}, isolated WebView2 profile {_userDataDir})");

        // Close the other half of the abort race: if Dispose ran while
        // Process.Start was in flight, it saw _process == null and killed
        // nothing — kill the fresh process ourselves and bail.
        if (_disposed)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
            throw new ObjectDisposedException(nameof(TauriAppManager), "Disposed during launch (aborted).");
        }

        WebSocketUrl = await PollForPageTargetAsync(effectiveTimeout, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Poll <c>http://127.0.0.1:{port}/json</c> until a page target on the
    /// tauri.localhost origin appears. URL-filtered (not first-page-wins)
    /// because a WebView2 host can expose worker/devtools targets too.
    /// </summary>
    private async Task<string> PollForPageTargetAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (_process is { HasExited: true })
                throw new InvalidOperationException(
                    $"Qualia desktop exe exited immediately (code {_process.ExitCode}). " +
                    "Most likely another Qualia instance is running — the single-instance " +
                    "plugin forwards and exits. Close the other instance and re-run.");

            try
            {
                var json = await http.GetStringAsync($"http://127.0.0.1:{_cdpPort}/json", ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                foreach (var target in doc.RootElement.EnumerateArray())
                {
                    var type = target.TryGetProperty("type", out var t) ? t.GetString() : null;
                    var url = target.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (type == "page" && url != null && url.StartsWith("http://tauri.localhost", StringComparison.OrdinalIgnoreCase))
                    {
                        var ws = target.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() : null;
                        if (!string.IsNullOrEmpty(ws)) return ws!;
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Per-request timeout — endpoint not up yet.
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"No tauri.localhost CDP page target on port {_cdpPort} within {timeout.TotalSeconds:F0}s. " +
            $"Last probe error: {last?.Message ?? "(none — endpoint answered but no matching target)"}");
    }

    /// <summary>
    /// Fail fast if any Qualia exe is already running — launching would
    /// trigger the single-instance forward-and-exit AND focus-steal the
    /// operator's window. Never kills anything.
    /// </summary>
    private static void ThrowIfQualiaAlreadyRunning()
    {
        var offenders = new List<string>();
        foreach (var name in new[] { "app", "Qualia" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = p.MainModule?.FileName ?? string.Empty;
                    if (path.Contains("Qualia", StringComparison.OrdinalIgnoreCase))
                        offenders.Add($"{Path.GetFileName(path)} (PID {p.Id}, {path})");
                }
                catch { /* access denied on foreign processes — not ours */ }
                finally { p.Dispose(); }
            }
        }
        if (offenders.Count > 0)
            throw new InvalidOperationException(
                "A Qualia instance is already running: " + string.Join("; ", offenders) +
                ". The single-instance plugin would make the harness launch forward-and-exit " +
                "(and focus-steal that window). Close it and re-run — the harness never kills it for you.");
    }

    /// <summary>Best-effort sweep of throwaway profiles older than 2h (crash leftovers).</summary>
    private static void SweepStaleProfiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(2);
            foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), "canary-wv2-*"))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(dir) < cutoff)
                        Directory.Delete(dir, recursive: true);
                }
                catch { /* held by a live webview — skip */ }
            }
        }
        catch { /* temp dir enumeration failed — non-fatal */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
                Canary.Telemetry.SpawnRegistry.Default.Unregister(_process.Id);
            }
            catch { /* best effort */ }
            _process.Dispose();
            _process = null;
        }

        // The WebView2 browser processes release the profile lock a beat
        // after the host dies — retry the delete briefly, then leave it to
        // the next run's stale sweep.
        if (_userDataDir != null)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try { Directory.Delete(_userDataDir, recursive: true); break; }
                catch { Thread.Sleep(400); }
            }
            _userDataDir = null;
        }
    }
}
