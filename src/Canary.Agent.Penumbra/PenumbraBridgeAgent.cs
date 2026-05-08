using System.Text.Json;
using Canary.Cdp;

namespace Canary.Agent.Penumbra;

/// <summary>
/// Bridge agent that drives Penumbra's browser-based test harness via Chrome DevTools Protocol.
/// Implements ICanaryAgent so the Canary harness talks to it like any other agent.
///
/// Instead of running inside the target app (like the Rhino agent), this agent runs as
/// an external process that controls Chrome via CDP. It translates Canary commands into:
///   - Runtime.evaluate → call Penumbra's JS APIs (scene loading, camera control)
///   - Page.captureScreenshot → pixel-perfect canvas capture
///   - Input.dispatchMouseEvent → mouse input in CSS coordinates (no SendInput needed)
/// </summary>
public sealed class PenumbraBridgeAgent : ICanaryAgent, IDisposable
{
    private readonly PenumbraConfig _config;
    private ViteManager? _vite;
    private ChromeLaunchResult? _chrome;
    private CdpClient? _cdp;
    private string? _externalViteUrl;
    private bool _initialized;
    private bool _disposed;

    // Canvas position within the page — measured once after page load
    private double _canvasOffsetX;
    private double _canvasOffsetY;
    private int _canvasWidth;
    private int _canvasHeight;

    /// <summary>
    /// Creates a new Penumbra bridge agent.
    /// </summary>
    /// <param name="config">Penumbra workload configuration.</param>
    public PenumbraBridgeAgent(PenumbraConfig config)
    {
        _config = config;
        _canvasWidth = config.DefaultCanvasWidth;
        _canvasHeight = config.DefaultCanvasHeight;
    }

    /// <summary>
    /// Initialize the bridge: start Vite, launch Chrome, navigate to Penumbra, lock canvas size.
    /// Call this before any ICanaryAgent methods.
    /// </summary>
    /// <param name="ct">Cancellation token — cleans up on cancellation.</param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            throw new InvalidOperationException("Bridge agent is already initialized.");

        // 1. Start Vite dev server
        _vite = new ViteManager(_config.ProjectDir, _config.VitePort);
        await _vite.StartAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        // 2. Launch Chrome with CDP
        var chromeOpts = new ChromeOptions
        {
            ChromePath = _config.ChromePath,
            CdpPort = _config.CdpPort,
            WindowWidth = _canvasWidth + 50,   // Extra for scrollbar/border
            WindowHeight = _canvasHeight + 150, // Extra for browser chrome
            ExtraFlags = _config.ChromeFlags
        };
        _chrome = await ChromeLauncher.LaunchAsync(chromeOpts, ct).ConfigureAwait(false);

        // 3. Connect CDP
        // 60s (was 15s) — Phase 11 atlas tests fire `setDisplayState` calls
        // that can trigger a 10–15s synchronous brick-build + Dawn pipeline
        // cross-compile on the JS main thread. With a 15s per-eval ceiling,
        // every atlas-bearing test would time out the heartbeat and the
        // watchdog (2s interval × 3 misses = ~6s grace) declared the agent
        // dead. The longer ceiling lets the work finish and report ok.
        // 2026-05-06 raised 60s → 180s; 2026-05-06 (later) raised
        // 180s → 240s. Penumbra exposes
        // window.__canaryWaitForAtlasPipelineReady(timeoutMs) (default
        // 120s) and window.__canaryRunComputeMarcherSmoke(timeoutMs)
        // (180s for E5 Phase 7b/3 — Dawn's
        // createComputePipelineAsync also takes ~90s on this hardware
        // because the compute shader is the same WGSL stack as the
        // fragment marcher). The await-promise round-trip is bounded
        // by the helper's own timeout, so the CDP layer must allow
        // headroom over it. 240s = 1.33× the longest helper timeout —
        // accommodates timing variance on the ~50s atlas pipeline
        // build + ~90s compute pipeline build without false-positive
        // CDP timeouts.
        _cdp = new CdpClient(TimeSpan.FromSeconds(240));
        await _cdp.ConnectAsync(_chrome.WebSocketUrl, ct).ConfigureAwait(false);

        // Enable required domains
        await _cdp.EnableDomainAsync("Page", ct).ConfigureAwait(false);
        await _cdp.EnableDomainAsync("Runtime", ct).ConfigureAwait(false);

        // 4. Navigate to Penumbra test harness
        var url = $"{_vite.Url}?autostart=true&backend={_config.DefaultBackend}";
        await _cdp.NavigateAsync(url, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);

        // 5. Wait for Penumbra to initialize (renderer ready)
        await WaitForPenumbraReadyAsync(ct).ConfigureAwait(false);

        // 6. Lock canvas to deterministic size
        await LockCanvasSizeAsync(_canvasWidth, _canvasHeight, ct).ConfigureAwait(false);

        // 7. Measure canvas position within the page
        await MeasureCanvasOffsetAsync(ct).ConfigureAwait(false);

        _initialized = true;
    }

    /// <summary>
    /// Initialize the bridge by connecting to an already-running Penumbra instance.
    /// Skips Vite start and Chrome launch — connects directly to the given CDP page WebSocket.
    /// </summary>
    /// <param name="pageWsUrl">WebSocket URL of the CDP page target (from /json/list).</param>
    /// <param name="viteUrl">URL of the already-running Vite dev server (e.g., http://localhost:3000).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeFromExistingAsync(string pageWsUrl, string viteUrl, CancellationToken ct = default)
    {
        if (_initialized)
            throw new InvalidOperationException("Bridge agent is already initialized.");

        _externalViteUrl = viteUrl;

        // Connect CDP to the existing page
        // 60s (was 15s) — Phase 11 atlas tests fire `setDisplayState` calls
        // that can trigger a 10–15s synchronous brick-build + Dawn pipeline
        // cross-compile on the JS main thread. With a 15s per-eval ceiling,
        // every atlas-bearing test would time out the heartbeat and the
        // watchdog (2s interval × 3 misses = ~6s grace) declared the agent
        // dead. The longer ceiling lets the work finish and report ok.
        // 2026-05-06 raised 60s → 180s; 2026-05-06 (later) raised
        // 180s → 240s. Penumbra exposes
        // window.__canaryWaitForAtlasPipelineReady(timeoutMs) (default
        // 120s) and window.__canaryRunComputeMarcherSmoke(timeoutMs)
        // (180s for E5 Phase 7b/3 — Dawn's
        // createComputePipelineAsync also takes ~90s on this hardware
        // because the compute shader is the same WGSL stack as the
        // fragment marcher). The await-promise round-trip is bounded
        // by the helper's own timeout, so the CDP layer must allow
        // headroom over it. 240s = 1.33× the longest helper timeout —
        // accommodates timing variance on the ~50s atlas pipeline
        // build + ~90s compute pipeline build without false-positive
        // CDP timeouts.
        _cdp = new CdpClient(TimeSpan.FromSeconds(240));
        await _cdp.ConnectAsync(pageWsUrl, ct).ConfigureAwait(false);

        await _cdp.EnableDomainAsync("Page", ct).ConfigureAwait(false);
        await _cdp.EnableDomainAsync("Runtime", ct).ConfigureAwait(false);

        // Reuse existing page — wait for harness, lock canvas, measure offset
        await WaitForPenumbraReadyAsync(ct).ConfigureAwait(false);
        await LockCanvasSizeAsync(_canvasWidth, _canvasHeight, ct).ConfigureAwait(false);
        await MeasureCanvasOffsetAsync(ct).ConfigureAwait(false);

        _initialized = true;
    }

    /// <inheritdoc />
    public async Task<HeartbeatResult> HeartbeatAsync()
    {
        EnsureInitialized();

        try
        {
            var info = await _cdp!.EvaluateAsync<Dictionary<string, JsonElement>>(
                "window.__canaryGetRendererInfo ? window.__canaryGetRendererInfo() : { ok: false }"
            ).ConfigureAwait(false);

            var state = new Dictionary<string, string>();
            if (info != null)
            {
                foreach (var kvp in info)
                {
                    state[kvp.Key] = kvp.Value.ToString();
                }
            }

            return new HeartbeatResult
            {
                Ok = true,
                State = state
            };
        }
        catch (Exception ex)
        {
            return new HeartbeatResult
            {
                Ok = false,
                State = new Dictionary<string, string> { ["error"] = ex.Message }
            };
        }
    }

    /// <inheritdoc />
    public async Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters)
    {
        EnsureInitialized();

        return action switch
        {
            "LoadScene" => await LoadSceneAsync(parameters).ConfigureAwait(false),
            "LoadSceneByName" => await LoadSceneByNameAsync(parameters).ConfigureAwait(false),
            "SetCamera" => await SetCameraAsync(parameters).ConfigureAwait(false),
            "SetCanvasSize" => await SetCanvasSizeAsync(parameters).ConfigureAwait(false),
            "WaitForStable" => await WaitForStableAsync(parameters).ConfigureAwait(false),
            "SetBackend" => await SetBackendAsync(parameters).ConfigureAwait(false),
            "RunCommand" => await RunCommandAsync(parameters).ConfigureAwait(false),
            "LoadDisplayPreset" => await LoadDisplayPresetAsync(parameters).ConfigureAwait(false),
            _ => new AgentResponse
            {
                Success = false,
                Message = $"Unknown action: {action}. Supported: LoadScene, LoadSceneByName, SetCamera, SetCanvasSize, WaitForStable, SetBackend, RunCommand, LoadDisplayPreset"
            }
        };
    }

    /// <inheritdoc />
    public async Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings)
    {
        EnsureInitialized();

        // Re-measure canvas offset in case layout changed
        await MeasureCanvasOffsetAsync().ConfigureAwait(false);

        var clip = new CdpClipRect
        {
            X = _canvasOffsetX,
            Y = _canvasOffsetY,
            Width = _canvasWidth,
            Height = _canvasHeight
        };

        var pngBytes = await _cdp!.CaptureScreenshotAsync(clip).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath)!);
        await File.WriteAllBytesAsync(settings.OutputPath, pngBytes).ConfigureAwait(false);

        return new ScreenshotResult
        {
            FilePath = settings.OutputPath,
            Width = _canvasWidth,
            Height = _canvasHeight,
            CapturedAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public Task AbortAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Action handlers
    // ──────────────────────────────────────────────────────────────────────

    private async Task<AgentResponse> LoadSceneAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("index", out var indexStr) || !int.TryParse(indexStr, out var index))
            return Fail("LoadScene requires 'index' parameter (integer).");

        await _cdp!.EvaluateAsync($"window.__canarySetScene({index})").ConfigureAwait(false);

        // Wait for atlas build to complete if applicable
        await WaitForAtlasIfNeededAsync().ConfigureAwait(false);

        return Ok($"Scene {index} loaded.");
    }

    private async Task<AgentResponse> LoadSceneByNameAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return Fail("LoadSceneByName requires 'name' parameter (non-empty string).");

        // JSON-encode the name so any quotes/backslashes in it don't break the JS expression.
        var jsLiteral = JsonSerializer.Serialize(name);
        await _cdp!.EvaluateAsync($"window.__canarySetSceneByName({jsLiteral})").ConfigureAwait(false);

        // Wait for atlas build to complete if applicable
        await WaitForAtlasIfNeededAsync().ConfigureAwait(false);

        return Ok($"Scene matching \"{name}\" loaded.");
    }

    private async Task<AgentResponse> SetCameraAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("azimuth", out var azStr) ||
            !parameters.TryGetValue("elevation", out var elStr) ||
            !parameters.TryGetValue("distance", out var distStr))
        {
            return Fail("SetCamera requires 'azimuth', 'elevation', 'distance' parameters.");
        }

        if (!double.TryParse(azStr, out var az) ||
            !double.TryParse(elStr, out var el) ||
            !double.TryParse(distStr, out var dist))
        {
            return Fail("SetCamera parameters must be valid numbers.");
        }

        await _cdp!.EvaluateAsync(
            $"window.__canarySetCamera({az}, {el}, {dist})"
        ).ConfigureAwait(false);

        // Wait for render to stabilize
        var stabilizeMs = _config.DefaultStabilizeMs;
        if (parameters.TryGetValue("stabilizeMs", out var stabStr) && int.TryParse(stabStr, out var stab))
            stabilizeMs = stab;

        await Task.Delay(stabilizeMs).ConfigureAwait(false);

        return Ok($"Camera set to azimuth={az}, elevation={el}, distance={dist}.");
    }

    private async Task<AgentResponse> SetCanvasSizeAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("width", out var wStr) || !int.TryParse(wStr, out var w) ||
            !parameters.TryGetValue("height", out var hStr) || !int.TryParse(hStr, out var h))
        {
            return Fail("SetCanvasSize requires 'width' and 'height' integer parameters.");
        }

        _canvasWidth = w;
        _canvasHeight = h;
        await LockCanvasSizeAsync(w, h).ConfigureAwait(false);
        await MeasureCanvasOffsetAsync().ConfigureAwait(false);

        return Ok($"Canvas locked to {w}x{h}.");
    }

    private async Task<AgentResponse> WaitForStableAsync(Dictionary<string, string> parameters)
    {
        var ms = _config.DefaultStabilizeMs;
        if (parameters.TryGetValue("ms", out var msStr) && int.TryParse(msStr, out var parsed))
            ms = parsed;

        await WaitForAtlasIfNeededAsync().ConfigureAwait(false);
        await Task.Delay(ms).ConfigureAwait(false);

        return Ok($"Waited {ms}ms for stabilization.");
    }

    private async Task<AgentResponse> SetBackendAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("backend", out var backend))
            return Fail("SetBackend requires 'backend' parameter ('webgpu' or 'webgl2').");

        var baseUrl = _externalViteUrl ?? _vite!.Url;
        var url = $"{baseUrl}?autostart=true&backend={backend}";
        await _cdp!.NavigateAsync(url, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        await WaitForPenumbraReadyAsync().ConfigureAwait(false);
        await LockCanvasSizeAsync(_canvasWidth, _canvasHeight).ConfigureAwait(false);
        await MeasureCanvasOffsetAsync().ConfigureAwait(false);

        return Ok($"Backend switched to {backend}.");
    }

    private async Task<AgentResponse> RunCommandAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("command", out var command))
            return Fail("RunCommand requires 'command' parameter.");

        var result = await _cdp!.EvaluateAsync(command).ConfigureAwait(false);
        return Ok($"Command executed. Result: {result ?? "undefined"}");
    }

    /// <summary>
    /// Load a Penumbra display preset by name. Resolves through the renderer's
    /// shipped catalog (<see href="../../Penumbra/packages/runtime/src/display-presets/"/>).
    /// Unknown names log a warning + no-op renderer-side; this method records
    /// the requested name and the resulting <c>getDisplayState().displayMode</c>
    /// in the response message for the run log. See Penumbra ADR 0011 + the
    /// PENUMBRA_WORKLOAD spec.
    /// </summary>
    private async Task<AgentResponse> LoadDisplayPresetAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return Fail("LoadDisplayPreset requires 'name' parameter (non-empty string).");

        // The Studio test harness exposes window.__canaryRenderer (the renderer
        // instance) and window.__canaryPass (the PenumbraPass). For Studio's
        // direct-renderer setup we go through the renderer; for adapter-based
        // setups (Qualia, future hosts) we go through the Pass.
        var script = $@"
            (function() {{
                var target = window.__canaryPass || window.__canaryRenderer;
                if (!target || typeof target.loadDisplayPreset !== 'function') {{
                    return JSON.stringify({{ ok: false, error: 'loadDisplayPreset unavailable' }});
                }}
                try {{
                    target.loadDisplayPreset({JsonSerializer.Serialize(name)});
                    var state = typeof target.getDisplayState === 'function' ? target.getDisplayState() : null;
                    return JSON.stringify({{
                        ok: true,
                        displayMode: state ? state.displayMode : null,
                        atomMode: state ? state.atomMode : null,
                        vizMode: state ? state.vizMode : null,
                    }});
                }} catch (e) {{
                    return JSON.stringify({{ ok: false, error: String(e) }});
                }}
            }})()";

        var raw = await _cdp!.EvaluateAsync(script).ConfigureAwait(false);
        var rawText = raw?.ToString() ?? "{}";
        try
        {
            using var doc = JsonDocument.Parse(rawText);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var displayMode = root.TryGetProperty("displayMode", out var dm) ? dm.GetString() : null;
                var atomMode = root.TryGetProperty("atomMode", out var am) ? am.GetString() : null;
                var vizMode = root.TryGetProperty("vizMode", out var vm) ? vm.GetString() : null;
                return Ok($"Preset '{name}' applied. displayMode={displayMode}, atomMode={atomMode}, vizMode={vizMode}");
            }
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
            return Fail($"LoadDisplayPreset('{name}') failed: {error}");
        }
        catch (JsonException)
        {
            return Fail($"LoadDisplayPreset('{name}') returned non-JSON: {rawText}");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Internal helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wait for Penumbra's test harness to initialize (renderer created, first scene loaded).
    /// Polls window.__canaryGetRendererInfo until it returns a valid response.
    /// </summary>
    private async Task WaitForPenumbraReadyAsync(CancellationToken ct = default)
    {
        // 2026-05-08: bumped 30 → 120s. Penumbra's `installCanaryHooks`
        // runs deep inside mainWebGPU (~line 4284) AFTER the atlas
        // pipeline build, which on Intel iGPU takes 30-45s on a cold
        // Dawn pipeline cache (28% warm-cache hit per C6 spike). The
        // prior 30s ceiling timed out before the harness could expose
        // __canaryGetRendererInfo. 120s gives 2-3× headroom for cold
        // loads while still failing fast on real init bugs.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await _cdp!.EvaluateAsync<JsonElement?>(
                    "window.__canaryGetRendererInfo ? window.__canaryGetRendererInfo() : null"
                ).ConfigureAwait(false);

                if (result.HasValue && result.Value.ValueKind == JsonValueKind.Object)
                    return;
            }
            catch
            {
                // Not ready yet
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "Penumbra test harness did not initialize within 30s. " +
            "Ensure window.__canaryGetRendererInfo is exposed in test/main.ts");
    }

    /// <summary>
    /// Lock the canvas to a fixed pixel size and disable the resize observer.
    /// </summary>
    private async Task LockCanvasSizeAsync(int width, int height, CancellationToken ct = default)
    {
        await _cdp!.EvaluateAsync($@"
            (() => {{
                const canvas = document.querySelector('canvas');
                if (!canvas) throw new Error('No canvas element found');
                canvas.width = {width};
                canvas.height = {height};
                canvas.style.width = '{width}px';
                canvas.style.height = '{height}px';
                window.__canaryLockSize = true;
                return true;
            }})()
        ", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Measure the canvas element's position within the page (for screenshot clipping
    /// and mouse coordinate mapping).
    /// </summary>
    private async Task MeasureCanvasOffsetAsync(CancellationToken ct = default)
    {
        var info = await _cdp!.EvaluateAsync<CanvasInfo>(@"
            (() => {
                const c = document.querySelector('canvas');
                if (!c) return { offsetX: 0, offsetY: 0, width: 0, height: 0 };
                const r = c.getBoundingClientRect();
                return { offsetX: r.left, offsetY: r.top, width: r.width, height: r.height };
            })()
        ", ct).ConfigureAwait(false);

        if (info != null)
        {
            _canvasOffsetX = info.OffsetX;
            _canvasOffsetY = info.OffsetY;
        }
    }

    /// <summary>
    /// Wait for atlas build to complete if any fields use atlas evaluation.
    /// Polls up to 10 seconds.
    /// </summary>
    private async Task WaitForAtlasIfNeededAsync(CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var complete = await _cdp!.EvaluateAsync<bool?>(@"
                (() => {
                    if (!window.__canaryGetRendererInfo) return true;
                    const info = window.__canaryGetRendererInfo();
                    // If no atlas fields, we're done
                    if (!info.hasAtlasFields) return true;
                    return info.atlasBuildComplete === true;
                })()
            ", ct).ConfigureAwait(false);

            if (complete == true) return;

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        // Don't throw — atlas might just be slow. Capture what we have.
    }

    /// <summary>
    /// Dispatch a mouse event to the canvas using CDP Input.dispatchMouseEvent.
    /// Translates normalized viewport coordinates (0-1) to page-relative CSS pixels.
    /// This is the Penumbra equivalent of InputReplayer.InjectMouseMove — but instead of
    /// SendInput → 65535-normalized screen coords, we use CDP → CSS pixel page coords.
    /// </summary>
    /// <param name="type">CDP event type: "mouseMoved", "mousePressed", "mouseReleased".</param>
    /// <param name="vx">Normalized viewport X [0.0, 1.0].</param>
    /// <param name="vy">Normalized viewport Y [0.0, 1.0].</param>
    /// <param name="button">Mouse button: "none", "left", "middle", "right".</param>
    /// <param name="clickCount">Click count.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task DispatchCanvasMouseEventAsync(
        string type, double vx, double vy,
        string button = "none", int clickCount = 0,
        CancellationToken ct = default)
    {
        EnsureInitialized();

        // Denormalize from (vx, vy) to CSS canvas pixels, then add canvas page offset
        double pageX = _canvasOffsetX + (vx * _canvasWidth);
        double pageY = _canvasOffsetY + (vy * _canvasHeight);

        return _cdp!.DispatchMouseEventAsync(type, pageX, pageY, button, clickCount, ct: ct);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "Bridge agent not initialized. Call InitializeAsync() first.");
    }

    private static AgentResponse Ok(string message) => new()
    {
        Success = true,
        Message = message
    };

    private static AgentResponse Fail(string message) => new()
    {
        Success = false,
        Message = message
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cdp?.Dispose();
        _chrome?.Dispose();
        _vite?.Dispose();
    }

    /// <summary>
    /// Internal model for canvas position measurement.
    /// </summary>
    private sealed class CanvasInfo
    {
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
