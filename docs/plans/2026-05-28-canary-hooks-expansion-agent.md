---
date: 2026-05-28
tags: [plan, canary, agent, qualia]
status: proposed
project: canary
component: agent-qualia
---

# Canary hooks expansion — agent side (Qualia bridge)

Companion to `Qualia/docs/plans/2026-05-28-canary-hooks-expansion.md`.
Defines which of the ~50 new `__canary*` hooks get dedicated
`ExecuteAsync` actions in `Canary.Agent.Qualia.QualiaBridgeAgent`, which
stay JS-only (called through the existing `RunCommand` catch-all from
test JSON), and the appendix that goes into
`workloads/qualia/AGENT_NOTES.md`.

## Design principle: don't 50x the action switch

`ExecuteAsync` currently dispatches ~25 named actions. The Qualia-side
expansion adds ~50 hooks. Wiring every hook to its own C# action would
double the agent without buying anything — `RunCommand` already
evaluates arbitrary JS, and a typical test step is just:

```json
{
  "action": "RunCommand",
  "params": { "command": "JSON.stringify(window.__canaryGetCameraState())" }
}
```

That works fine for pure readers. **Add a named action only when one of
these is true:**

1. The hook takes structured parameters that benefit from C#-side
   coercion / validation (e.g. `DispatchPan({ dx, dy })`).
2. The hook is called often enough that the named action makes test
   JSON dramatically more readable than a raw `RunCommand`.
3. The agent needs to post-process the result before returning (e.g.
   binary screenshot extraction, multi-step orchestration).

Otherwise: leave the hook JS-only, document it in `AGENT_NOTES.md`, and
let tests call it through `RunCommand`.

## Named actions to add (12 total)

| Action | Hook(s) called | Parameters | Why named |
|---|---|---|---|
| `DispatchZoom` | `__canaryDispatchZoom` | `deltaY: int` | Common in zoom regression tests; reads cleaner as `DispatchZoom(120)` than as a `RunCommand` blob. |
| `DispatchPan` | `__canaryDispatchPan` | `dx: int, dy: int` | Same. Two-arg form benefits from C# parsing. |
| `DispatchOrbit` | `__canaryDispatchOrbit` | `dPhi: number, dTheta: number` | Same. |
| `DispatchClick` | `__canaryDispatchClick` | `x: int, y: int, button?: 'left'\|'middle'\|'right'` | Test step `DispatchClick(640, 360)` reads better than a JS expression. |
| `DispatchDrag` | `__canaryDispatchDrag` | `fromX, fromY, toX, toY, button?` | Five-arg form benefits from named keys. |
| `DispatchKey` | `__canaryDispatchKey` | `key: string, ctrl?: bool, shift?: bool, alt?: bool, meta?: bool` | Hotkey tests (Tab, bracket keys, arrows). |
| `AimAtFacet` | `__canaryAimAtFacet` | `axisX, axisY, axisZ, originX, originY, originZ, duration?: number` | Three-vector params; type-checking on the C# side catches typos before the JS runs. |
| `SetCameraState` | `__canarySetCameraState` | `posX, posY, posZ, targetX, targetY, targetZ, upX?, upY?, upZ?, fov?, duration?` | Same — many params, easy to fat-finger. |
| `FitToView` | `__canaryFitToView` | `duration?: number` | One-liner; named for symmetry with the existing `ResetAndRepel` action style. |
| `SetPlanarSettings` | `__canarySetPlanarSettings` | `paramsJson: string` | Partial-object shape; pass through as JSON. |
| `SimStep` | `__canarySimStep` | `dt?: number` | One-arg, but stepping a sim deterministically is core to behavior tests. |
| `LoadPenumbraPreset` | `__canaryLoadPenumbraPreset` | `name: string` | Mirrors the existing Penumbra-workload `LoadDisplayPreset` action. |

Everything else — all the **reader** hooks (`GetCameraState`,
`GetPlaneDeviation`, `GetDebugStats`, `GetFrameGeometry`,
`GetSidecarState`, `GetFullSnapshot`, `ListNodes`, `ListEdges`,
`GetPersonaState`, `ListMountedPersonas`, `GetPipelinePasses`,
`GetRecentConsole`, etc.) — stay JS-only and get called via
`RunCommand`.

Test JSON convention for readers:

```json
{
  "action": "RunCommand",
  "params": {
    "command": "JSON.stringify(window.__canaryGetPlaneDeviation())"
  },
  "store": "deviation"
}
```

(The `store` field assumes a test-runner extension that captures
`RunCommand` results into named variables for later checkpoint
assertions. If that doesn't exist yet, it's a small Canary-Core
addition that pays for itself across all the new readers — file as a
follow-up if needed.)

## C# scaffolding to add

Add the cases to the switch in `ExecuteAsync` (line 137 of
`QualiaBridgeAgent.cs`):

```csharp
"DispatchZoom"        => await DispatchZoomAsync(parameters).ConfigureAwait(false),
"DispatchPan"         => await DispatchPanAsync(parameters).ConfigureAwait(false),
"DispatchOrbit"       => await DispatchOrbitAsync(parameters).ConfigureAwait(false),
"DispatchClick"       => await DispatchClickAsync(parameters).ConfigureAwait(false),
"DispatchDrag"        => await DispatchDragAsync(parameters).ConfigureAwait(false),
"DispatchKey"         => await DispatchKeyAsync(parameters).ConfigureAwait(false),
"AimAtFacet"          => await AimAtFacetAsync(parameters).ConfigureAwait(false),
"SetCameraState"      => await SetCameraStateAsync(parameters).ConfigureAwait(false),
"FitToView"           => await FitToViewAsync(parameters).ConfigureAwait(false),
"SetPlanarSettings"   => await SetPlanarSettingsAsync(parameters).ConfigureAwait(false),
"SimStep"             => await SimStepAsync(parameters).ConfigureAwait(false),
"LoadPenumbraPreset"  => await LoadPenumbraPresetAsync(parameters).ConfigureAwait(false),
```

Update the `Fail` message at the bottom of the switch to list the new
actions.

Each handler follows the existing pattern (compare against
`ApplyProfileAsync` line 316 and `ClickProfilePillAsync` line 339):

```csharp
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
    if (!parameters.TryGetValue("dPhi", out var pStr) || !double.TryParse(pStr, out var dPhi) ||
        !parameters.TryGetValue("dTheta", out var tStr) || !double.TryParse(tStr, out var dTheta))
        return Fail("DispatchOrbit requires 'dPhi' and 'dTheta' number parameters.");
    var result = await _cdp!.EvaluateAsync(
        $"window.__canaryDispatchOrbit({{ dPhi: {dPhi}, dTheta: {dTheta} }})"
    ).ConfigureAwait(false);
    return Ok($"Dispatched orbit dPhi={dPhi} dTheta={dTheta}. Result: {result ?? "undefined"}");
}

private async Task<AgentResponse> DispatchClickAsync(Dictionary<string, string> parameters)
{
    if (!parameters.TryGetValue("x", out var xStr) || !int.TryParse(xStr, out var x) ||
        !parameters.TryGetValue("y", out var yStr) || !int.TryParse(yStr, out var y))
        return Fail("DispatchClick requires 'x' and 'y' integer parameters.");
    parameters.TryGetValue("button", out var button);
    var jsButton = JsonSerializer.Serialize(string.IsNullOrEmpty(button) ? "left" : button);
    var result = await _cdp!.EvaluateAsync(
        $"window.__canaryDispatchClick({{ x: {x}, y: {y}, button: {jsButton} }})"
    ).ConfigureAwait(false);
    return Ok($"Dispatched click ({x},{y},{button ?? "left"}). Result: {result ?? "undefined"}");
}

private async Task<AgentResponse> DispatchDragAsync(Dictionary<string, string> parameters)
{
    if (!parameters.TryGetValue("fromX", out var fxStr) || !int.TryParse(fxStr, out var fromX) ||
        !parameters.TryGetValue("fromY", out var fyStr) || !int.TryParse(fyStr, out var fromY) ||
        !parameters.TryGetValue("toX", out var txStr) || !int.TryParse(txStr, out var toX) ||
        !parameters.TryGetValue("toY", out var tyStr) || !int.TryParse(tyStr, out var toY))
        return Fail("DispatchDrag requires 'fromX','fromY','toX','toY' integer parameters.");
    parameters.TryGetValue("button", out var button);
    var jsButton = JsonSerializer.Serialize(string.IsNullOrEmpty(button) ? "left" : button);
    var result = await _cdp!.EvaluateAsync(
        $"window.__canaryDispatchDrag({{ from: [{fromX},{fromY}], to: [{toX},{toY}], button: {jsButton} }})"
    ).ConfigureAwait(false);
    return Ok($"Dispatched drag ({fromX},{fromY})→({toX},{toY}). Result: {result ?? "undefined"}");
}

private async Task<AgentResponse> DispatchKeyAsync(Dictionary<string, string> parameters)
{
    if (!parameters.TryGetValue("key", out var key) || string.IsNullOrEmpty(key))
        return Fail("DispatchKey requires 'key' parameter.");
    bool ctrl = parameters.TryGetValue("ctrl", out var c) && bool.TryParse(c, out var cb) && cb;
    bool shift = parameters.TryGetValue("shift", out var s) && bool.TryParse(s, out var sb) && sb;
    bool alt = parameters.TryGetValue("alt", out var a) && bool.TryParse(a, out var ab) && ab;
    bool meta = parameters.TryGetValue("meta", out var m) && bool.TryParse(m, out var mb) && mb;
    var jsKey = JsonSerializer.Serialize(key);
    var result = await _cdp!.EvaluateAsync(
        $"window.__canaryDispatchKey({{ key: {jsKey}, ctrl: {ctrl.ToString().ToLowerInvariant()}, " +
        $"shift: {shift.ToString().ToLowerInvariant()}, alt: {alt.ToString().ToLowerInvariant()}, " +
        $"meta: {meta.ToString().ToLowerInvariant()} }})"
    ).ConfigureAwait(false);
    return Ok($"Dispatched key '{key}'. Result: {result ?? "undefined"}");
}

private async Task<AgentResponse> AimAtFacetAsync(Dictionary<string, string> parameters)
{
    if (!TryParseVec3(parameters, "axis", out var ax, out var ay, out var az))
        return Fail("AimAtFacet requires 'axisX','axisY','axisZ' number parameters.");
    if (!TryParseVec3(parameters, "origin", out var ox, out var oy, out var oz))
        return Fail("AimAtFacet requires 'originX','originY','originZ' number parameters.");
    double duration = 0.7;
    if (parameters.TryGetValue("duration", out var dStr) && double.TryParse(dStr, out var d))
        duration = d;
    var result = await _cdp!.EvaluateAsync(
        $"window.__canaryAimAtFacet({{ axis: [{ax},{ay},{az}], origin: [{ox},{oy},{oz}] }}, {duration})"
    ).ConfigureAwait(false);
    return Ok($"Aimed at facet axis=({ax},{ay},{az}) origin=({ox},{oy},{oz}). Result: {result ?? "undefined"}");
}

private async Task<AgentResponse> SetCameraStateAsync(Dictionary<string, string> parameters)
{
    if (!TryParseVec3(parameters, "pos", out var px, out var py, out var pz))
        return Fail("SetCameraState requires 'posX','posY','posZ' number parameters.");
    if (!TryParseVec3(parameters, "target", out var tx, out var ty, out var tz))
        return Fail("SetCameraState requires 'targetX','targetY','targetZ' number parameters.");
    string upClause = "";
    if (TryParseVec3(parameters, "up", out var ux, out var uy, out var uz))
        upClause = $", up: [{ux},{uy},{uz}]";
    string fovClause = "";
    if (parameters.TryGetValue("fov", out var fStr) && double.TryParse(fStr, out var fov))
        fovClause = $", fov: {fov}";
    double duration = 0;
    if (parameters.TryGetValue("duration", out var dStr) && double.TryParse(dStr, out var d))
        duration = d;
    var result = await _cdp!.EvaluateAsync(
        $"window.__canarySetCameraState({{ position: [{px},{py},{pz}], target: [{tx},{ty},{tz}]{upClause}{fovClause} }}, {duration})"
    ).ConfigureAwait(false);
    return Ok($"Set camera. Result: {result ?? "undefined"}");
}

private async Task<AgentResponse> FitToViewAsync(Dictionary<string, string> parameters)
{
    double? duration = null;
    if (parameters.TryGetValue("duration", out var dStr) && double.TryParse(dStr, out var d))
        duration = d;
    var arg = duration.HasValue ? duration.Value.ToString() : "";
    var result = await _cdp!.EvaluateAsync($"window.__canaryFitToView({arg})")
        .ConfigureAwait(false);
    return Ok($"Fit to view (duration={duration?.ToString() ?? "default"}). Result: {result ?? "undefined"}");
}

private async Task<AgentResponse> SetPlanarSettingsAsync(Dictionary<string, string> parameters)
{
    if (!parameters.TryGetValue("paramsJson", out var paramsJson) || string.IsNullOrWhiteSpace(paramsJson))
        return Fail("SetPlanarSettings requires 'paramsJson' parameter (JSON object).");
    var result = await _cdp!.EvaluateAsync($"window.__canarySetPlanarSettings({paramsJson})")
        .ConfigureAwait(false);
    return Ok($"Set planar settings. Result: {result ?? "undefined"}");
}

private async Task<AgentResponse> SimStepAsync(Dictionary<string, string> parameters)
{
    string arg = "";
    if (parameters.TryGetValue("dt", out var dStr) && double.TryParse(dStr, out var dt))
        arg = dt.ToString();
    var result = await _cdp!.EvaluateAsync($"window.__canarySimStep({arg})")
        .ConfigureAwait(false);
    return Ok($"Sim stepped. Result: {result ?? "undefined"}");
}

private async Task<AgentResponse> LoadPenumbraPresetAsync(Dictionary<string, string> parameters)
{
    if (!parameters.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
        return Fail("LoadPenumbraPreset requires 'name' parameter.");
    var jsName = JsonSerializer.Serialize(name);
    var result = await _cdp!.EvaluateAsync($"window.__canaryLoadPenumbraPreset({jsName})")
        .ConfigureAwait(false);
    return Ok($"Loaded Penumbra preset '{name}'. Result: {result ?? "undefined"}");
}

// — helper —
private static bool TryParseVec3(
    Dictionary<string, string> p, string prefix,
    out double x, out double y, out double z)
{
    x = y = z = 0;
    return p.TryGetValue($"{prefix}X", out var xs) && double.TryParse(xs, out x)
        && p.TryGetValue($"{prefix}Y", out var ys) && double.TryParse(ys, out y)
        && p.TryGetValue($"{prefix}Z", out var zs) && double.TryParse(zs, out z);
}
```

`TryParseVec3` goes in the "Helpers" region near `EvaluateOkAsync`
(line 431).

## `Heartbeat` upgrade (optional, recommended)

`HeartbeatAsync` (line 193) currently reads `__canaryGetAppInfo()` and
returns a flat `Dictionary<string, string>`. Once `__canaryGetFullSnapshot`
exists, swap the heartbeat over to it (cheap — full snapshot defaults
to excluding the big arrays). The supervised-session telemetry stream
then captures camera + planar + persona state at every heartbeat without
the test having to ask. Single-line change:

```csharp
"window.__canaryGetFullSnapshot ? window.__canaryGetFullSnapshot() : (window.__canaryGetAppInfo ? window.__canaryGetAppInfo() : null)"
```

Fall-through to `__canaryGetAppInfo` preserves heartbeat for older
Qualia builds during the rollout window.

## `AGENT_NOTES.md` appendix

Append the block below to
`C:\Repos\Canary\workloads\qualia\AGENT_NOTES.md` after the existing
"Status" section. Mirrors the structure of the qualia-v4 hooks block.

````markdown
## Debug-info hook expansion (2026-05-28)

A batched expansion of the hook surface to make Canary tests assert
specifics (camera pose, planar deviation, mounted personas, sim state,
RAG queue) instead of only screenshotting. Driven by recurring
zoom/pan/planar regressions that pixel-diff couldn't catch
deterministically.

Plan docs:
- Qualia side: `Qualia/docs/plans/2026-05-28-canary-hooks-expansion.md`
- Canary side: `Canary/docs/plans/2026-05-28-canary-hooks-expansion-agent.md`

### New hook surfaces (Qualia side)

**A. Camera / viewport**
- `__canaryGetCameraState()` → `{ position, target, up, fov, aspect, distanceToTarget, projection }`.
- `__canaryGetCameraTransition()` → `{ active, durationMs, elapsedMs, fromTarget, toTarget }`.
- `__canaryGetPerspectiveLock()` → `{ engaged, axis, origin, side, distance }`.
- `__canaryFitToView(duration?)` — programmatic frame.
- `__canaryAimAtFacet({ axis, origin }, duration?)` — drives `aimAtFacet`.
- `__canarySetCameraState({ position, target, up?, fov? }, duration?)`.
- `__canaryDispatchZoom(deltaY)` / `__canaryDispatchPan({ dx, dy })` / `__canaryDispatchOrbit({ dPhi, dTheta })`
  — synthesize OrbitControls input so tests exercise the same path as the user.
- `__canaryGetControlsEnabled()` / `__canarySetControlsEnabled(enabled)`.
- `__canaryProjectToScreen([x, y, z])` → `{ x, y } | null`.

**B. Planar confinement**
- `__canaryGetPlanarSettings()` → full `PlanarSettings`.
- `__canarySetPlanarSettings(partial)`.
- `__canaryGetPlaneDeviation({ axisId? })` → `{ axis, origin, maxAbsDeviation, meanAbsDeviation, p95AbsDeviation, nodeCount, outliers[] }`
  — **the computed invariant**; first-class way to assert "the context stayed planar".
- `__canaryCaptureLevel(bandWidth?, name?)` / `__canaryUncaptureLevel(levelId)`.

**C. Renderer / debug stats / frame geometry**
- `__canaryGetDebugStats()` — drawCalls, triangles, geometries, textures, programs, memoryMB, node/edge/group counts, camera pose, activeContextId.
- `__canaryGetFrameGeometry({ nodeIds?, includeJunctions? })` — silhouettes, junction nubs, intersection circles, distance-to-camera.
- `__canaryGetViewerSettings()` / `__canarySetViewerSettings(partial)`.
- `__canaryGetPerfSettings()` / `__canaryGetTouchedPerfFields()`.
- `__canaryGetNodeDisplayMode()` / `__canaryGetEdgeShape()` / `__canaryGetEdgeRouting()`.
- `__canaryGetGridVisible()` / `__canaryGetThemeState()`.

**D. Nodes / edges / positions**
- `__canaryGetNodePosition(nodeId)` → `[x,y,z] | null`.
- `__canaryGetNodeSnapshot(nodeId)` → full per-node observable bundle.
- `__canaryListNodes({ inContext?, filterType?, limit? })`.
- `__canaryListEdges({ inContext?, sourceId?, targetId?, limit? })`.
- `__canaryGetSelectedNodeIds()`.
- `__canaryFlashEdge(edgeId, direction?)` / `__canaryPulseNode(nodeId)`.

**E. Qverse / Context state**
- `__canaryGetActiveScope()` → `{ activeQverseId, activeContextId, qverse, context }`.
- `__canaryListQverses()` / `__canaryListContexts({ scope? })`.
- `__canarySwitchQverse(qverseId | null)` — pairs with existing `__canarySwitchContext`.
- `__canaryGetContextScript(contextId)`.

**F. Persona registry / mounted host**
- `__canaryGetProfileState()` → `{ active, isCustom, definition }`.
- `__canaryListProfiles()`.
- `__canaryGetPersonaState(id)` → `{ descriptor, isEnabled, capabilityMet, isMounted, mountError }`.
- `__canaryListMountedPersonas()` — catches "enabled but didn't mount" drift.
- `__canaryListToolbarButtons()` / `__canaryClickToolbarButton(id)`.
- `__canaryGetPipelinePasses()` → registered `pipeline.*` passes, in order.

**G. Simulation**
- `__canaryGetSimState()` → `{ isRunning, isPaused, tickRate, currentTick, lastTick }`.
- `__canarySimStep(dt?)` — single deterministic tick.
- `__canarySimStart(tickRate?)` / `__canarySimStop()` / `__canarySimSetPaused(paused)`.
- `__canaryGetNodeBehavior(nodeId)` → `{ behaviorId, source, scenario, state }`.

**H. RAG sidecar / extraction**
- `__canaryGetSidecarState()` → `{ available, fsRoot, fileCount, l2Count, l3Count, modelState }`.
- `__canaryGetExtractionQueue()` / `__canaryGetRecentExtractions(limit?)`.
- `__canaryGetProviderConfig()`.
- `__canaryTriggerL3Extraction(pointerId?)` / `__canarySetExtractionProvider(provider, opts)`.

**I. Event / console / recorder**
- `__canaryGetRecentConsole({ levels?, limit? })`.
- `__canaryGetTelemetryHistory({ limit? })`.
- `__canaryTakeDebugSnapshot(label)`.
- `__canaryGetRecorderState()`.

**J. Input dispatch (non-camera)**
- `__canaryDispatchClick({ x, y, button? })` / `__canaryDispatchDoubleClick({ x, y })` / `__canaryDispatchHover({ x, y })`.
- `__canaryDispatchDrag({ from, to, button? })`.
- `__canaryDispatchKey({ key, ctrl?, shift?, alt?, meta? })`.

**K. DOM / panel readers**
- `__canaryGetPerfPanelState()` / `__canaryGetTagsPanelState()` / `__canaryGetToolbarState()`
  / `__canaryGetSidebarState()` / `__canaryGetPropertiesPanelState()`.

**L. Penumbra bridge**
- `__canaryGetPenumbraState()` → `{ attached, displayState, presets, activePreset }`.
- `__canaryLoadPenumbraPreset(name)`.
- `__canaryExportPenumbraMesh(format)` — metadata only.

**M. Composite reader**
- `__canaryGetFullSnapshot({ include? })` — orchestrator that calls every reader with sane defaults. Used by the supervised-session capture path: each screenshot can save a `snapshot.json` sidecar next to the PNG.

### New named agent actions

| Action | Hook | Notes |
|---|---|---|
| `DispatchZoom` | `__canaryDispatchZoom` | `deltaY: int`. |
| `DispatchPan` | `__canaryDispatchPan` | `dx: int, dy: int`. |
| `DispatchOrbit` | `__canaryDispatchOrbit` | `dPhi: number, dTheta: number`. |
| `DispatchClick` | `__canaryDispatchClick` | `x, y, button?`. |
| `DispatchDrag` | `__canaryDispatchDrag` | `fromX, fromY, toX, toY, button?`. |
| `DispatchKey` | `__canaryDispatchKey` | `key, ctrl?, shift?, alt?, meta?`. |
| `AimAtFacet` | `__canaryAimAtFacet` | `axisX/Y/Z, originX/Y/Z, duration?`. |
| `SetCameraState` | `__canarySetCameraState` | `posX/Y/Z, targetX/Y/Z, upX/Y/Z?, fov?, duration?`. |
| `FitToView` | `__canaryFitToView` | `duration?`. |
| `SetPlanarSettings` | `__canarySetPlanarSettings` | `paramsJson` (partial). |
| `SimStep` | `__canarySimStep` | `dt?`. |
| `LoadPenumbraPreset` | `__canaryLoadPenumbraPreset` | `name`. |

Every other new hook is **JS-only** — call from `setup.commands` /
checkpoint commands or via the `RunCommand` action:

```json
{
  "action": "RunCommand",
  "params": { "command": "JSON.stringify(window.__canaryGetPlaneDeviation())" }
}
```

### Heartbeat upgrade

`HeartbeatAsync` prefers `__canaryGetFullSnapshot()` when present and
falls back to `__canaryGetAppInfo()`. Supervised-session telemetry
streams the full snapshot at every heartbeat, so camera + planar +
persona state is captured passively even when no checkpoint asks.

### Test plan

A single new test `workloads/qualia/tests/diag-context-zoom-pan-planar.json`
covers the headline regression — load minimal sample → switch into the
misbehaving perspective context → assert lock engaged + plane deviation
within ε → `DispatchZoom` → assert distance changed → `DispatchPan` →
assert target changed → re-assert plane deviation unchanged. Run with
`--mode both` so Gemma also vets the visual.
````

## Cross-repo log entry

After both repos land their changes, append to
`C:\Repos\MultiVerse\BUILD_LOG.md`:

```
2026-05-28 | cross-repo | Qualia → Canary | ~50 new __canary* debug-info hooks (camera/planar/frame/personas/sim/RAG/DOM); 12 new agent actions; heartbeat upgraded to GetFullSnapshot
```

## Sequencing

1. **Qualia first.** Land the JS hooks plus `__canaryHooksReady` stays
   the gate. Old agent calls still work (everything new is additive).
2. **Canary next.** Add the 12 named actions + `TryParseVec3` helper.
   Update `AGENT_NOTES.md`. Optionally upgrade `HeartbeatAsync`.
3. **First test.** `diag-context-zoom-pan-planar.json` exercises the
   headline path under `--mode both`.
4. **Backfill.** Move existing diag-* tests off `dumpDiagnostics` onto
   the typed readers where it makes their assertions sharper.

## Wave 1a resolution (2026-05-28)

Landed as a 1a/1b split (see `MultiVerse/prompts/canary-hooks-expansion-2026-05-28.md`). Wave 1a adds 7 of 12 planned named actions — the camera + planar set whose Qualia hooks land in 1a (`DispatchZoom`, `DispatchPan`, `DispatchOrbit`, `AimAtFacet`, `SetCameraState`, `FitToView`, `SetPlanarSettings`). Wave 1b will add the remaining 5 once the matching Qualia hooks land: `DispatchClick`, `DispatchDoubleClick`, `DispatchHover`, `DispatchDrag`, `DispatchKey` (waiting on §J), `SimStep` (waiting on §G), `LoadPenumbraPreset` (waiting on §L).

`TryParseVec3` helper landed in the Helpers region (`InvariantCulture` parse so locale-sensitive test JSON reads correctly). `HeartbeatAsync` upgrade landed and unwraps `__canaryGetFullSnapshot()`'s `{ok,value}` envelope before consuming the inner object — the bare `r.value ?? r` form sketched in the plan doc would have flat-stringified the envelope into telemetry; the actual landed form is `(function(){if(window.__canaryGetFullSnapshot){var r=window.__canaryGetFullSnapshot();return r&&r.ok?r.value:r;}if(window.__canaryGetAppInfo){return window.__canaryGetAppInfo();}return null;})()`.

Landed commits:
- Canary agent (7 actions + heartbeat + AGENT_NOTES appendix): `38d0528`
- Canary smoke test `diag-canary-hooks-1a-smoke`: `c490e9f`

The first test as authored is a **smoke test**, not the headline `diag-context-zoom-pan-planar` regression test. The regression test defers to wave 1b — it needs a perspective-context fixture, which neither the demo workspace nor the minimal sample carries and which `__canarySpawnChildContext` doesn't yet build (a 1b `facet` extension to that hook is the cleanest path).

Status stays `proposed` because 5 actions remain unbuilt and the headline test is deferred. See the Qualia-side plan doc's Wave 1a resolution section for the full 1a/1b split.
