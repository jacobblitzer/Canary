using System.Text.Json;
using Canary.Cdp;
using Canary.Telemetry;

namespace Canary.Agent.Qualia;

/// <summary>
/// Bridge agent that drives the Qualia browser app via Chrome DevTools Protocol.
/// Same architectural pattern as <see cref="Canary.Agent.Penumbra.PenumbraBridgeAgent"/>:
/// run as an external process, control Chrome via CDP, translate Canary
/// commands into JS evaluations against <c>window.__canary*</c> hooks.
///
/// Trimmed surface — only the actions Qualia tests currently need:
///   <list type="bullet">
///     <item><c>RunCommand</c> — evaluate a JS expression (the catch-all).</item>
///     <item><c>WaitForReady</c> — poll <c>__canaryWaitForReady</c>.</item>
///     <item><c>WaitForStable</c> — sleep N ms.</item>
///     <item><c>Reload</c> — re-navigate to the current Vite URL + re-wait
///         for <c>__canaryHooksReady</c>. Used by tests that mutate
///         localStorage mid-test and need React to re-mount against the
///         new state (e.g., eager-L3 cold-launch / warm-launch / provider-swap
///         fixtures). Mirrors the navigation+ready flow of
///         <see cref="InitializeAsync"/> but preserves localStorage so the
///         test's setup.commands seed survives.</item>
///     <item><c>SetCanvasSize</c> — resize browser window.</item>
///     <item><c>HideUI</c> — call <c>__canaryHideUI(bool)</c>.</item>
///     <item><c>ApplyProfile</c> — call <c>__canaryApplyProfile(name)</c>.</item>
///     <item><c>SetModuleEnabled</c> — call <c>__canarySetPersonaEnabled(id, bool)</c> (action name preserved
///         for backwards compat; JS hook renamed from <c>__canarySetModuleEnabled</c> in Qualia Phase 7.2,
///         2026-05-12, with a deprecated alias maintained for one release).</item>
///     <item><c>ShowLandingScreen</c> / <c>CloseLandingScreen</c>.</item>
///     <item><c>ClickProfilePill</c> — click a pill by name.</item>
///     <item><c>ToggleLandingModule</c> — toggle a persona checkbox in the modal (action name preserved
///         for backwards compat; JS hook renamed from <c>__canaryToggleLandingModule</c> in Qualia Phase 7.2,
///         2026-05-12, with a deprecated alias maintained for one release).</item>
///     <item><c>ClickLandingApply</c> / <c>ClickLandingCancel</c>.</item>
///     <item><c>ClearStorage</c> — clear localStorage for the dev origin.</item>
///   </list>
/// </summary>
public sealed class QualiaBridgeAgent : ICanaryAgent, ITelemetryAware, IDisposable
{
    private readonly QualiaConfig _config;
    private ViteManager? _vite;
    private ChromeLaunchResult? _chrome;
    private CdpClient? _cdp;
    private bool _initialized;
    private bool _disposed;

    private int _canvasWidth;
    private int _canvasHeight;

    // Phase 2 / §C1: registered by TestRunner before InitializeAsync.
    private ITelemetrySink _telemetrySink = NullTelemetrySink.Instance;
    private IDisposable? _telemetrySubscriptions;

    public void RegisterTelemetrySink(ITelemetrySink sink)
    {
        _telemetrySink = sink ?? NullTelemetrySink.Instance;
    }

    public QualiaBridgeAgent(QualiaConfig config)
    {
        _config = config;
        _canvasWidth = config.DefaultCanvasWidth;
        _canvasHeight = config.DefaultCanvasHeight;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            throw new InvalidOperationException("Bridge agent is already initialized.");

        // 1. Start Vite
        _vite = new ViteManager(_config.ProjectDir, _config.VitePort);
        await _vite.StartAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        // 2. Launch Chrome with CDP
        var chromeOpts = new ChromeOptions
        {
            ChromePath = _config.ChromePath,
            CdpPort = _config.CdpPort,
            WindowWidth = _canvasWidth + 50,
            WindowHeight = _canvasHeight + 150,
            ExtraFlags = _config.ChromeFlags
        };
        _chrome = await ChromeLauncher.LaunchAsync(chromeOpts, ct).ConfigureAwait(false);

        // 3. Connect CDP. 60s ceiling — Qualia doesn't have Penumbra's atlas-build
        //    multi-second sync work, but the registry has a localStorage round-trip
        //    on persist + a few React renders that can stall briefly under WSL/CI.
        _cdp = new CdpClient(TimeSpan.FromSeconds(60));
        await _cdp.ConnectAsync(_chrome.WebSocketUrl, ct).ConfigureAwait(false);
        await _cdp.EnableDomainAsync("Page", ct).ConfigureAwait(false);
        await _cdp.EnableDomainAsync("Runtime", ct).ConfigureAwait(false);

        // Phase 2 / §C1: telemetry stream (console + log + network → sink).
        _telemetrySubscriptions = await CdpTelemetryStream.EnableAndSubscribeAsync(
            _cdp, _telemetrySink, source: "qualia", ct).ConfigureAwait(false);

        // 4. Optional clean-state: clear localStorage for the dev origin BEFORE the
        //    page loads so first-launch behavior (LandingScreen visible, default
        //    profile) is reproducible. Done by navigating to about:blank first,
        //    then to the dev URL with `?canaryReset=1`.
        if (_config.ClearLocalStorageOnInit)
        {
            await _cdp.NavigateAsync("about:blank", TimeSpan.FromSeconds(5), ct)
                .ConfigureAwait(false);
        }

        // 5. Navigate to Qualia. The `?canaryReset=1` query param is a hook the app
        //    can use to clear storage before mounting; today it's a no-op marker.
        var url = _config.ClearLocalStorageOnInit
            ? $"{_vite.Url}?canaryReset=1"
            : _vite.Url;
        await _cdp.NavigateAsync(url, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);

        // 6. If reset requested, clear storage AFTER navigate (so we have a same-origin
        //    document) then reload so React mounts against fresh storage.
        if (_config.ClearLocalStorageOnInit)
        {
            await _cdp.EvaluateAsync(
                "(() => { try { localStorage.clear(); sessionStorage.clear(); return true; } catch(e) { return false; } })()",
                ct).ConfigureAwait(false);
            await _cdp.NavigateAsync(_vite.Url, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        }

        // 7. Wait for app readiness (initial demo data loaded + renderer attached).
        await WaitForReadyInternalAsync(_config.ReadyTimeoutSec * 1000, ct).ConfigureAwait(false);

        _initialized = true;
    }

    public async Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters)
    {
        EnsureInitialized();

        return action switch
        {
            "RunCommand"           => await RunCommandAsync(parameters).ConfigureAwait(false),
            "WaitForReady"         => await WaitForReadyAsync(parameters).ConfigureAwait(false),
            "WaitForStable"        => await WaitForStableAsync(parameters).ConfigureAwait(false),
            "Reload"               => await ReloadAsync().ConfigureAwait(false),
            "SetCanvasSize"        => await SetCanvasSizeAsync(parameters).ConfigureAwait(false),
            "HideUI"               => await HideUiAsync(parameters).ConfigureAwait(false),
            "ApplyProfile"         => await ApplyProfileAsync(parameters).ConfigureAwait(false),
            "SetModuleEnabled"     => await SetModuleEnabledAsync(parameters).ConfigureAwait(false),
            "ShowLandingScreen"    => await EvaluateOkAsync("window.__canaryShowLandingScreen()").ConfigureAwait(false),
            "CloseLandingScreen"   => await EvaluateOkAsync("window.__canaryCloseLandingScreen()").ConfigureAwait(false),
            "ClickProfilePill"     => await ClickProfilePillAsync(parameters).ConfigureAwait(false),
            "ToggleLandingModule"  => await ToggleLandingModuleAsync(parameters).ConfigureAwait(false),
            "ClickLandingApply"    => await EvaluateOkAsync("window.__canaryClickLandingApply()").ConfigureAwait(false),
            "ClickLandingCancel"   => await EvaluateOkAsync("window.__canaryClickLandingCancel()").ConfigureAwait(false),
            "ClearStorage"         => await EvaluateOkAsync("(()=>{localStorage.clear();sessionStorage.clear();return true;})()").ConfigureAwait(false),
            "PlaygroundOpen"       => await EvaluateOkAsync("window.__canaryPlaygroundOpen()").ConfigureAwait(false),
            "PlaygroundClose"      => await EvaluateOkAsync("window.__canaryPlaygroundClose()").ConfigureAwait(false),
            "PlaygroundLoadScenario" => await PlaygroundLoadScenarioAsync(parameters).ConfigureAwait(false),
            "PlaygroundSetParam"   => await PlaygroundSetParamAsync(parameters).ConfigureAwait(false),
            "PlaygroundSaveSnapshot" => await PlaygroundSaveSnapshotAsync(parameters).ConfigureAwait(false),
            "PlaygroundRestoreSnapshot" => await PlaygroundRestoreSnapshotAsync(parameters).ConfigureAwait(false),
            "PlaygroundDeleteSnapshot" => await PlaygroundDeleteSnapshotAsync(parameters).ConfigureAwait(false),
            "PlaygroundListSnapshots" => await EvaluateOkAsync("window.__canaryPlaygroundListSnapshots()").ConfigureAwait(false),
            "PlaygroundGetState"   => await EvaluateOkAsync("window.__canaryPlaygroundGetState()").ConfigureAwait(false),
            // Debug-info hook expansion wave 1a (2026-05-28): camera + planar
            // dispatch + state setters. See
            // docs/plans/2026-05-28-canary-hooks-expansion-agent.md.
            "DispatchZoom"         => await DispatchZoomAsync(parameters).ConfigureAwait(false),
            "DispatchPan"          => await DispatchPanAsync(parameters).ConfigureAwait(false),
            "DispatchOrbit"        => await DispatchOrbitAsync(parameters).ConfigureAwait(false),
            "AimAtFacet"           => await AimAtFacetAsync(parameters).ConfigureAwait(false),
            "SetCameraState"       => await SetCameraStateAsync(parameters).ConfigureAwait(false),
            "FitToView"            => await FitToViewAsync(parameters).ConfigureAwait(false),
            "SetPlanarSettings"    => await SetPlanarSettingsAsync(parameters).ConfigureAwait(false),
            _ => Fail(
                $"Unknown action: {action}. Supported: RunCommand, WaitForReady, WaitForStable, " +
                "Reload, SetCanvasSize, HideUI, ApplyProfile, SetModuleEnabled, ShowLandingScreen, " +
                "CloseLandingScreen, ClickProfilePill, ToggleLandingModule, ClickLandingApply, " +
                "ClickLandingCancel, ClearStorage, PlaygroundOpen, PlaygroundClose, " +
                "PlaygroundLoadScenario, PlaygroundSetParam, PlaygroundSaveSnapshot, " +
                "PlaygroundRestoreSnapshot, PlaygroundDeleteSnapshot, PlaygroundListSnapshots, " +
                "PlaygroundGetState, DispatchZoom, DispatchPan, DispatchOrbit, AimAtFacet, " +
                "SetCameraState, FitToView, SetPlanarSettings")
        };
    }

    public async Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings)
    {
        EnsureInitialized();
        // No clip — capture the full viewport. Tests that want canvas-only can
        // call HideUI(true) first.
        var pngBytes = await _cdp!.CaptureScreenshotAsync().ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath)!);
        await File.WriteAllBytesAsync(settings.OutputPath, pngBytes).ConfigureAwait(false);

        return new ScreenshotResult
        {
            FilePath = settings.OutputPath,
            Width = _canvasWidth,
            Height = _canvasHeight,
            CapturedAt = DateTime.UtcNow,
        };
    }

    public async Task<HeartbeatResult> HeartbeatAsync()
    {
        if (!_initialized || _cdp is null)
            return new HeartbeatResult { Ok = false };

        try
        {
            // Debug-info hook expansion 1a (2026-05-28): prefer the new
            // composite snapshot, which carries camera + planar + node/edge
            // counts in one read. Falls back to legacy __canaryGetAppInfo
            // for older Qualia builds during the rollout window.
            var info = await _cdp.EvaluateAsync<JsonElement?>(
                "(function(){if(window.__canaryGetFullSnapshot){var r=window.__canaryGetFullSnapshot();return r&&r.ok?r.value:r;}if(window.__canaryGetAppInfo){return window.__canaryGetAppInfo();}return null;})()"
            ).ConfigureAwait(false);

            if (info.HasValue && info.Value.ValueKind == JsonValueKind.Object)
            {
                var state = new Dictionary<string, string>();
                foreach (var prop in info.Value.EnumerateObject())
                    state[prop.Name] = prop.Value.ToString();
                return new HeartbeatResult { Ok = true, State = state };
            }
        }
        catch
        {
            // Fall through to ok=false below.
        }

        return new HeartbeatResult { Ok = false };
    }

    public Task AbortAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Action handlers
    // ──────────────────────────────────────────────────────────────────────

    private async Task<AgentResponse> RunCommandAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("command", out var command))
            return Fail("RunCommand requires 'command' parameter.");

        var result = await _cdp!.EvaluateAsync(command).ConfigureAwait(false);
        return Ok($"Command executed. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> WaitForReadyAsync(Dictionary<string, string> parameters)
    {
        var ms = _config.ReadyTimeoutSec * 1000;
        if (parameters.TryGetValue("timeoutMs", out var timeoutStr) && int.TryParse(timeoutStr, out var parsed))
            ms = parsed;

        await WaitForReadyInternalAsync(ms, default).ConfigureAwait(false);
        return Ok($"Qualia ready (waited up to {ms}ms).");
    }

    private async Task<AgentResponse> WaitForStableAsync(Dictionary<string, string> parameters)
    {
        var ms = _config.DefaultStabilizeMs;
        if (parameters.TryGetValue("ms", out var msStr) && int.TryParse(msStr, out var parsed))
            ms = parsed;
        await Task.Delay(ms).ConfigureAwait(false);
        return Ok($"Waited {ms}ms.");
    }

    /// <summary>
    /// Re-navigate to the current Vite URL and re-wait for app readiness.
    /// Preserves localStorage (intentionally — the calling test seeded it
    /// via <c>setup.commands</c> and needs the new state to survive the
    /// React re-mount). Mirrors steps 5-7 of <see cref="InitializeAsync"/>
    /// minus the storage clear.
    ///
    /// Unblocks any test that wants to re-mount after mutating
    /// localStorage (eager-L3 cold-launch / warm-launch / provider-swap
    /// fixtures; future "switch profile and re-mount" scenarios). The
    /// alternative — calling <c>window.location.reload()</c> from
    /// <c>setup.commands</c> — crashes the next <c>Runtime.evaluate</c>
    /// with "Inspected target navigated or closed" because the CDP
    /// execution context dies without an explicit wait for the new one;
    /// this action wraps the right CDP-level navigation primitives that
    /// do wait.
    /// </summary>
    private async Task<AgentResponse> ReloadAsync()
    {
        if (_vite is null) return Fail("Reload requires Vite to be running.");
        await _cdp!.NavigateAsync(_vite.Url, TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);
        await WaitForReadyInternalAsync(_config.ReadyTimeoutSec * 1000, default)
            .ConfigureAwait(false);
        return Ok($"Reloaded {_vite.Url}.");
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
        // CDP doesn't have a "resize browser" but we can override the viewport
        // metrics via Emulation.setDeviceMetricsOverride.
        await _cdp!.EvaluateAsync($@"
            (() => {{
                document.documentElement.style.width = '{w}px';
                document.documentElement.style.height = '{h}px';
                return {{ width: {w}, height: {h} }};
            }})()").ConfigureAwait(false);
        return Ok($"Canvas set to {w}x{h}.");
    }

    private async Task<AgentResponse> HideUiAsync(Dictionary<string, string> parameters)
    {
        var hidden = true;
        if (parameters.TryGetValue("hidden", out var s) && bool.TryParse(s, out var parsed))
            hidden = parsed;
        await _cdp!.EvaluateAsync($"window.__canaryHideUI({(hidden ? "true" : "false")})")
            .ConfigureAwait(false);
        return Ok($"UI hidden={hidden}.");
    }

    private async Task<AgentResponse> ApplyProfileAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return Fail("ApplyProfile requires 'name' parameter.");
        var jsName = JsonSerializer.Serialize(name);
        var result = await _cdp!.EvaluateAsync($"window.__canaryApplyProfile({jsName})")
            .ConfigureAwait(false);
        return Ok($"Applied profile '{name}'. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> SetModuleEnabledAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
            return Fail("SetModuleEnabled requires 'id' parameter.");
        if (!parameters.TryGetValue("enabled", out var s) || !bool.TryParse(s, out var enabled))
            return Fail("SetModuleEnabled requires 'enabled' boolean parameter.");
        var jsId = JsonSerializer.Serialize(id);
        var result = await _cdp!.EvaluateAsync(
            $"window.__canarySetPersonaEnabled({jsId}, {(enabled ? "true" : "false")})"
        ).ConfigureAwait(false);
        return Ok($"Set {id} = {enabled}. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> ClickProfilePillAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return Fail("ClickProfilePill requires 'name' parameter.");
        var jsName = JsonSerializer.Serialize(name);
        var result = await _cdp!.EvaluateAsync($"window.__canaryClickProfilePill({jsName})")
            .ConfigureAwait(false);
        return Ok($"Clicked pill '{name}'. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> ToggleLandingModuleAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
            return Fail("ToggleLandingModule requires 'id' parameter.");
        var jsId = JsonSerializer.Serialize(id);
        var result = await _cdp!.EvaluateAsync($"window.__canaryToggleLandingPersona({jsId})")
            .ConfigureAwait(false);
        return Ok($"Toggled {id}. Result: {result ?? "undefined"}");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Playground actions (Wave 0.B / ADR 0012 phase D)
    // ──────────────────────────────────────────────────────────────────────

    private async Task<AgentResponse> PlaygroundLoadScenarioAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
            return Fail("PlaygroundLoadScenario requires 'id' parameter (random|grid|tree|scale-free|stress-1k|stress-10k).");
        var jsId = JsonSerializer.Serialize(id);

        // Optional params object as JSON. Tests pass values one key at a time
        // via `paramsJson` (a JSON-encoded record of number values).
        var jsParams = "undefined";
        if (parameters.TryGetValue("paramsJson", out var paramsJson) && !string.IsNullOrWhiteSpace(paramsJson))
            jsParams = paramsJson;

        var result = await _cdp!.EvaluateAsync(
            $"window.__canaryPlaygroundLoadScenario({jsId}, {jsParams})"
        ).ConfigureAwait(false);
        return Ok($"Loaded scenario '{id}'. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> PlaygroundSetParamAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("key", out var key) || string.IsNullOrWhiteSpace(key))
            return Fail("PlaygroundSetParam requires 'key' parameter.");
        if (!parameters.TryGetValue("value", out var valueStr) || !double.TryParse(valueStr, out var value))
            return Fail("PlaygroundSetParam requires 'value' (number) parameter.");
        var jsKey = JsonSerializer.Serialize(key);
        var result = await _cdp!.EvaluateAsync(
            $"window.__canaryPlaygroundSetParam({jsKey}, {value})"
        ).ConfigureAwait(false);
        return Ok($"Set param {key}={value}. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> PlaygroundSaveSnapshotAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("label", out var label) || string.IsNullOrWhiteSpace(label))
            return Fail("PlaygroundSaveSnapshot requires 'label' parameter.");
        var jsLabel = JsonSerializer.Serialize(label);
        var result = await _cdp!.EvaluateAsync(
            $"window.__canaryPlaygroundSaveSnapshot({jsLabel})"
        ).ConfigureAwait(false);
        return Ok($"Saved snapshot '{label}'. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> PlaygroundRestoreSnapshotAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
            return Fail("PlaygroundRestoreSnapshot requires 'id' parameter.");
        var jsId = JsonSerializer.Serialize(id);
        var result = await _cdp!.EvaluateAsync(
            $"window.__canaryPlaygroundRestoreSnapshot({jsId})"
        ).ConfigureAwait(false);
        return Ok($"Restored snapshot '{id}'. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> PlaygroundDeleteSnapshotAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
            return Fail("PlaygroundDeleteSnapshot requires 'id' parameter.");
        var jsId = JsonSerializer.Serialize(id);
        var result = await _cdp!.EvaluateAsync(
            $"window.__canaryPlaygroundDeleteSnapshot({jsId})"
        ).ConfigureAwait(false);
        return Ok($"Deleted snapshot '{id}'. Result: {result ?? "undefined"}");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Debug-info hook expansion handlers (wave 1a, 2026-05-28)
    // ──────────────────────────────────────────────────────────────────────
    //
    // Each named action mirrors a __canary* hook installed by the wave 1a
    // hook batch (Qualia/packages/ui/src/canary-hooks.ts §A + §B). Reader
    // hooks stay JS-only — call them from test JSON via RunCommand. See
    // docs/plans/2026-05-28-canary-hooks-expansion-agent.md for the
    // design rationale ("don't 50x the action switch").

    private async Task<AgentResponse> DispatchZoomAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("deltaY", out var s) || !int.TryParse(s, out var deltaY))
            return Fail("DispatchZoom requires 'deltaY' integer parameter.");
        var result = await _cdp!.EvaluateAsync($"window.__canaryDispatchZoom({deltaY})")
            .ConfigureAwait(false);
        return Ok($"Dispatched zoom deltaY={deltaY}. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> DispatchPanAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("dx", out var dxStr) || !int.TryParse(dxStr, out var dx) ||
            !parameters.TryGetValue("dy", out var dyStr) || !int.TryParse(dyStr, out var dy))
            return Fail("DispatchPan requires 'dx' and 'dy' integer parameters.");
        var result = await _cdp!.EvaluateAsync($"window.__canaryDispatchPan({{ dx: {dx}, dy: {dy} }})")
            .ConfigureAwait(false);
        return Ok($"Dispatched pan dx={dx} dy={dy}. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> DispatchOrbitAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("dPhi", out var pStr) || !double.TryParse(pStr, System.Globalization.CultureInfo.InvariantCulture, out var dPhi) ||
            !parameters.TryGetValue("dTheta", out var tStr) || !double.TryParse(tStr, System.Globalization.CultureInfo.InvariantCulture, out var dTheta))
            return Fail("DispatchOrbit requires 'dPhi' and 'dTheta' number parameters (radians).");
        var result = await _cdp!.EvaluateAsync(
            $"window.__canaryDispatchOrbit({{ dPhi: {dPhi.ToString(System.Globalization.CultureInfo.InvariantCulture)}, dTheta: {dTheta.ToString(System.Globalization.CultureInfo.InvariantCulture)} }})"
        ).ConfigureAwait(false);
        return Ok($"Dispatched orbit dPhi={dPhi} dTheta={dTheta}. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> AimAtFacetAsync(Dictionary<string, string> parameters)
    {
        if (!TryParseVec3(parameters, "axis", out var ax, out var ay, out var az))
            return Fail("AimAtFacet requires 'axisX','axisY','axisZ' number parameters.");
        if (!TryParseVec3(parameters, "origin", out var ox, out var oy, out var oz))
            return Fail("AimAtFacet requires 'originX','originY','originZ' number parameters.");
        double duration = 0.7;
        if (parameters.TryGetValue("duration", out var dStr) && double.TryParse(dStr, System.Globalization.CultureInfo.InvariantCulture, out var d))
            duration = d;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var result = await _cdp!.EvaluateAsync(
            $"window.__canaryAimAtFacet({{ axis: [{ax.ToString(ci)},{ay.ToString(ci)},{az.ToString(ci)}], origin: [{ox.ToString(ci)},{oy.ToString(ci)},{oz.ToString(ci)}] }}, {duration.ToString(ci)})"
        ).ConfigureAwait(false);
        return Ok($"Aimed at facet axis=({ax},{ay},{az}) origin=({ox},{oy},{oz}). Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> SetCameraStateAsync(Dictionary<string, string> parameters)
    {
        if (!TryParseVec3(parameters, "pos", out var px, out var py, out var pz))
            return Fail("SetCameraState requires 'posX','posY','posZ' number parameters.");
        if (!TryParseVec3(parameters, "target", out var tx, out var ty, out var tz))
            return Fail("SetCameraState requires 'targetX','targetY','targetZ' number parameters.");
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string upClause = "";
        if (TryParseVec3(parameters, "up", out var ux, out var uy, out var uz))
            upClause = $", up: [{ux.ToString(ci)},{uy.ToString(ci)},{uz.ToString(ci)}]";
        string fovClause = "";
        if (parameters.TryGetValue("fov", out var fStr) && double.TryParse(fStr, ci, out var fov))
            fovClause = $", fov: {fov.ToString(ci)}";
        double duration = 0;
        if (parameters.TryGetValue("duration", out var dStr) && double.TryParse(dStr, ci, out var d))
            duration = d;
        var result = await _cdp!.EvaluateAsync(
            $"window.__canarySetCameraState({{ position: [{px.ToString(ci)},{py.ToString(ci)},{pz.ToString(ci)}], target: [{tx.ToString(ci)},{ty.ToString(ci)},{tz.ToString(ci)}]{upClause}{fovClause} }}, {duration.ToString(ci)})"
        ).ConfigureAwait(false);
        return Ok($"Set camera. Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> FitToViewAsync(Dictionary<string, string> parameters)
    {
        double? duration = null;
        if (parameters.TryGetValue("duration", out var dStr) && double.TryParse(dStr, System.Globalization.CultureInfo.InvariantCulture, out var d))
            duration = d;
        var arg = duration.HasValue ? duration.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        var result = await _cdp!.EvaluateAsync($"window.__canaryFitToView({arg})")
            .ConfigureAwait(false);
        return Ok($"Fit to view (duration={duration?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "default"}). Result: {result ?? "undefined"}");
    }

    private async Task<AgentResponse> SetPlanarSettingsAsync(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("paramsJson", out var paramsJson) || string.IsNullOrWhiteSpace(paramsJson))
            return Fail("SetPlanarSettings requires 'paramsJson' parameter (JSON object).");
        var result = await _cdp!.EvaluateAsync($"window.__canarySetPlanarSettings({paramsJson})")
            .ConfigureAwait(false);
        return Ok($"Set planar settings. Result: {result ?? "undefined"}");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private async Task<AgentResponse> EvaluateOkAsync(string js)
    {
        var result = await _cdp!.EvaluateAsync(js).ConfigureAwait(false);
        return Ok($"Evaluated. Result: {result ?? "undefined"}");
    }

    /**
     * Parse a vector-3 from three named keys with a shared prefix —
     * e.g. ("axis", "axisX", "axisY", "axisZ"). Used by AimAtFacet,
     * SetCameraState, etc. Returns false if any of the three keys is
     * missing or non-numeric. Invariant-culture parse so locale doesn't
     * break "0.5" reads from test JSON.
     */
    private static bool TryParseVec3(
        Dictionary<string, string> p, string prefix,
        out double x, out double y, out double z)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        x = y = z = 0;
        return p.TryGetValue($"{prefix}X", out var xs) && double.TryParse(xs, ci, out x)
            && p.TryGetValue($"{prefix}Y", out var ys) && double.TryParse(ys, ci, out y)
            && p.TryGetValue($"{prefix}Z", out var zs) && double.TryParse(zs, ci, out z);
    }

    private async Task WaitForReadyInternalAsync(int timeoutMs, CancellationToken ct)
    {
        // Prefer the in-app __canaryWaitForReady (which knows about the store +
        // renderer state). Fall back to polling __canaryGetAppInfo if hooks
        // haven't installed yet.
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var hooksReady = await _cdp!.EvaluateAsync<bool?>(
                    "window.__canaryHooksReady === true"
                ).ConfigureAwait(false);

                if (hooksReady == true)
                {
                    // Hooks are installed — let __canaryWaitForReady handle the rest.
                    var remainingMs = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    await _cdp.EvaluateAsync(
                        $"window.__canaryWaitForReady({remainingMs})", ct
                    ).ConfigureAwait(false);
                    return;
                }
            }
            catch
            {
                // Page not loaded yet — keep polling.
            }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Qualia did not become ready within {timeoutMs}ms. " +
            "Ensure window.__canaryHooksReady is true and demo data is loaded.");
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "Bridge agent not initialized. Call InitializeAsync() first.");
    }

    private static AgentResponse Ok(string message) => new() { Success = true, Message = message };
    private static AgentResponse Fail(string message) => new() { Success = false, Message = message };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _telemetrySubscriptions?.Dispose(); } catch { }
        _cdp?.Dispose();
        _chrome?.Dispose();
        _vite?.Dispose();
    }
}
