---
title: "Canary ↔ Penumbra integration audit — impact of Penumbra major architectural changes (native C++ DLL + embedded QuickJS, FSM + cascade scheduler → TS kernel, Jint removal)"
date: 2026-06-22
tags:
  - research
  - penumbra
  - canary
  - migration
  - audit
status: completed
project: canary
component: agent.penumbra, agent.rhino, core.telemetry, core.orchestration, harness.session
related-bugs: []
related-decisions: []
---

# Canary ↔ Penumbra integration audit

## Research question

The Penumbra runtime is undergoing major architectural change:

1. **Scene compiler → native C++ DLL with embedded QuickJS** (graduating the
   TS-side compilation pipeline into a per-host loadable `.dll` whose JS
   surface is QuickJS, not V8/Dawn).
2. **FSM + cascade scheduler → TS kernel** (the existing imperative
   `installCanaryHooks` / `pass.loadDisplayPreset` / `setDisplayState`
   surface gets refactored behind a small TS scheduling kernel).
3. **Eventually remove Jint** — Penumbra's C# Rhino plug-in currently uses
   Jint to host the scene-compiler JS in-process for the GL studio path.
   Replacing it with the new native + QuickJS host changes how Bridge
   verbs are dispatched.

Which Penumbra changes would **force** Canary changes (test invalidation,
agent surface changes, telemetry format breaks)? Canary must stay green
throughout the overhaul.

## Context

Canary is Penumbra's only automated visual-regression + correctness
harness. Two integration surfaces are in use today, plus a third one as
side-channel:

| Surface | Driver | Lives in |
|---|---|---|
| **Web (Vite + CDP)** — browser-hosted Penumbra | `Canary.Agent.Penumbra.PenumbraBridgeAgent` (ICanaryAgent over CDP) | `src/Canary.Agent.Penumbra/`, `workloads/penumbra/` |
| **In-Rhino preview** — Penumbra.Rhino plug-in | `Canary.Agent.Rhino.RhinoAgent.HandleWaitForPenumbraFrame` (reflection into `Penumbra.Bridge.PenumbraBridge.GetFrameState`) | `src/Canary.Agent.Rhino/`, `workloads/rhino/tests/penumbra-*`, `workloads/rhino/tests/cpig-*` |
| **NDJSON preview telemetry tail** — diagnostic-only | `Canary.Telemetry.PenumbraPreviewTelemetryTail` (file-tail of `%LocalAppData%\Penumbra\preview\telemetry.ndjson`) | `src/Canary.Core/Telemetry/PenumbraPreviewTelemetryTail.cs`, used by `RhinoSessionAgent` |

Penumbra has driven >50 cross-repo asks through Canary over the last 6
weeks (per `CLAUDE.md` "Active Penumbra integration initiatives"). The
contract risk surface is large.

## Sources consulted

- `C:\Repos\Canary\CLAUDE.md` (full operator + agent contract)
- `C:\Repos\Canary\workloads\penumbra\workload.json` + 136 test JSONs in `workloads/penumbra/tests/`
- `C:\Repos\Canary\workloads\penumbra\suites\*.json` (25 suites)
- `C:\Repos\Canary\workloads\rhino\suites\penumbra.json`, `penumbra-glsl.json`, `cpig-fieldops.json`, `cpig-bool-refactor.json`
- `C:\Repos\Canary\workloads\rhino\tests\penumbra-rhino-00-smoke.json`, `penumbra-rhino-01-gyroid.json`, `penumbra-glsl-00-sphere.json`, `penumbra-glsl-01-cpig-gyroid.json`, `cpig-fieldops-00-create-copy-delete.json`
- `C:\Repos\Canary\src\Canary.Agent.Penumbra\PenumbraBridgeAgent.cs`, `PenumbraConfig.cs`, `PenumbraInstanceProbe.cs`, `ViteManager.cs`, `CdpInputReplayer.cs`
- `C:\Repos\Canary\src\Canary.Agent.Rhino\RhinoAgent.cs`, `CanaryRhinoPlugin.cs`
- `C:\Repos\Canary\src\Canary.Core\Telemetry\PenumbraPreviewTelemetryTail.cs`
- `C:\Repos\Canary\src\Canary.Core\Cdp\CdpTelemetryStream.cs`
- `C:\Repos\Canary\src\Canary.Core\Orchestration\TestRunner.cs` (Penumbra-relevant lines 678–1010)
- `C:\Repos\Canary\src\Canary.Core\Config\TestDefinition.cs` (Penumbra-shaped fields: SceneSetup, CanvasSetup, CameraPosition, DisplayPreset)
- `C:\Repos\Canary\src\Canary.Harness\Cli\RunCommand.cs` (`RunPenumbraSuiteAsync`, agentType dispatch)
- `C:\Repos\Canary\src\Canary.Harness\Session\RhinoSessionAgent.cs`, `SessionAgentFactory.cs`
- `C:\Repos\Canary\src\Canary.UI.Avalonia\ViewModels\TestRunnerViewModel.cs` (RunPenumbraAsync flow with instance-probe reuse)
- `C:\Repos\Canary\canary-penumbra\` (handoff source-of-truth that originally landed the integration: `PENUMBRA_CANARY_SPEC.md`, `INTEGRATION_GUIDE.md`, `penumbra-hooks/canary-hooks.ts`)
- `C:\Repos\Canary\docs\features\rhino-session.md`
- Session telemetry samples: `workloads/penumbra/sessions/20260622-002107-c474/telemetry.ndjson`, `workloads/rhino/sessions/20260617-162450-9ebb/telemetry.ndjson`

## Findings

### 1. Penumbra workloads in Canary

Two workload directories, plus a "side-channel" use embedded in the rhino workload:

#### `workloads/penumbra/` — browser CDP workload (`agentType: "penumbra-cdp"`)

- **`workload.json`** — `appPath: "npm"`, `appArgs: "run dev -- --port 3000 --strictPort"`, `agentType: "penumbra-cdp"`, plus `penumbraConfig` block (projectDir, cdpPort, vitePort, defaultBackend, defaultCanvasWidth/Height, defaultStabilizeMs, pageLoadTimeoutMs=180000, chromeFlags).
- **136 test JSONs** in `tests/`. Categories observable from filenames:
  - **Scene + camera orbits (smoke)** — `tape-csg-orbit`, `atlas-blob-orbit`, `multi-field-orbit`, `stress-test-orbit`, `hybrid-default`, `cornell-box-default`, `teapot-default`, `terrain-default`, `assembly-default`, `columns-default` — verify Penumbra renders a scene index/sceneName at 4 standard camera angles.
  - **Display-mode matrix** — `assembly-{viz-aabbs,viz-cascades,viz-centroids,viz-field-id,viz-normal,wire-bricks,wire-cascades,eval-tape,point-cloud,fieldAABBs-overlay,atomAABBs-overlay,d1-lipschitz,d3-tricubic-on,mesh-determinism}`, mirrored under `atlas-blob-*`, `columns-diag-*`, `teapot-*`, `multi-field-*`, `foreign-objects-*`. Each exercises one `__canarySetDisplayMode(...)` or `__canarySetDisplayState(...)` toggle and captures.
  - **Compute marcher (E5)** — `atlas-blob-compute-smoke{,-3b3a,-bisection,-d2cubic,-d3tricubic,-d7c,-lighting,-materials,-persistent,-tiled,-wg4,-wg16}` + `atlas-blob-compute-determinism`. All invoke `__canaryWaitForAtlasPipelineReady`, `__canaryPrebuildComputeMarchPipeline`, `__canarySetComputeMarchToggles`, `__canaryRunComputeMarcherSmoke`.
  - **Feature loader (ADR 0014/15)** — `feature-loader-{all-off,c6-spike-1..5,stub-wiring,quality-profile,performance-profile,mutex-rejection}`. Use `__canaryLoadFeatureProfile`, `__canarySetDisplayState({features:...})`, `__canaryGetFeatureStatus`, `__canaryValidateCurrentFeatures`.
  - **Display preset (ADR 0011)** — `preset-{default,monument,particulate-blend,particulate-cloud,smoke-test,diagnostic-{aabbs,bricks,march-steps}}`. Set via setup-level `displayPreset` field (not a setup command). The harness dispatches `LoadDisplayPreset` to the agent which calls `pass.loadDisplayPreset(name)`.
  - **Materials / environment / effects** — `teapot-material-{damascus,marble,metal,wood,zebra}`, `atlas-blob-env-{neutral,night,outdoor,sunset}`, `tape-csg-{aces,bg-color,contours,custom-lighting,exposure,fog,noise,onion,orbit}`, `multi-field-{emissive,fresnel}`. Use `__canaryApplyMaterialPreset`, `__canaryApplyEnvironmentPreset`, `__canarySetFog`, `__canarySetLighting`, `__canarySetBackgroundColor`, `__canarySetComposite`, `__canarySetFieldEffect`, `__canarySetFieldEmissive`.
  - **3MF / STL importer** — `threemf-{volumetric-import,dense-import,dense6-import}`. Use `__canaryLoadThreeMFFromUrl` / `__canaryLoadSTLFromUrl`. Bug-0035 regression guard.
  - **Mesh suite** — `mesh-{bunny,teapot,benchy}`, `assembly-mesh-determinism` (uses `__canaryExtractMeshDeterminism`), `multi-field-mesh-determinism`. Verify mesh-as-SDF eval determinism.
  - **Multi-atlas (P11)** — `p11-{multi,teapot}-{atlas,tape}-{legacy,unified,unified-viznormal}`. Atlas pipeline matrix.
  - **VLM oracle** — `vlm-{atlas-blob-organic,cornell-box-layout,multi-field-separation,tape-csg-geometry,teapot-metal-mixed,teapot-shape,terrain-landscape}`. Use VLM mode (Ollama gemma4/qwen2.5vl); these don't depend on a baseline, only on the natural-language description matching the render.
  - **Bug regression** — `bug-0036-display-stall-soak.json` (~7-min stall-pattern soak using `__canaryGetRenderLoopState`, `__canaryGetDeviceState`).
  - **A6 backfill** — `a6-backfill.json` (multiscale splat-cache feature state).
- **25 suites** (`smoke`, `full`, `vlm`, plus matrices: `assembly-display-modes`, `atom-brick-allocator`, `bug-0036-stall`, `buyout-canonical`, `columns-diag`, `d1-lipschitz`, `d3-tricubic`, `display-matrix`, `display-modes`, `display-smoke`, `effects`, `environment`, `feature-loader`, `feature-loader-c6-spike`, `foreign-objects`, `materials`, `mesh-export-determinism`, `multiscale-overlays`, `overlays`, `p11-matrix`, `scenes`, `stl-import`).

#### `workloads/rhino/` — in-process Rhino driver (the Penumbra-in-Rhino preview)

- **`workloads/rhino/suites/penumbra.json`** — 2 tests, `penumbra-rhino-{00-smoke,01-gyroid}`. Tagged `[DEPRECATED — out-of-process image-blit fallback]`. Drives the legacy OOP overlay (`PenumbraOopShow` command, formerly `PenumbraShow`); file-source captures from `%LOCALAPPDATA%\Penumbra\preview-frames\active.png`. Kept ONLY because `penumbra-rhino-01-gyroid` still uniquely covers the CPig→Penumbra live-preview chain.
- **`workloads/rhino/suites/penumbra-glsl.json`** — 2 tests, `penumbra-glsl-{00-sphere,01-cpig-gyroid}`. The in-process GLSL studio path (Swath C). `RunCommand _PenumbraPreviewBackend _Glsl` switches the backend; `WaitForPenumbraFrame` gates capture. `mode: capture` (no pixel pass/fail today); VLM wired but dormant via `setup.vlm = ollama qwen2.5vl:7b`.
- **`workloads/rhino/suites/cpig-fieldops.json`** — 6 tests `cpig-fieldops-{00-create-copy-delete, 01-multi-type-scene, 02-native-copy-per-type, 03-gumball-move-per-type, 04-display-rep-cycle, rep-dense-pointcloud-mesh}`. Drives CPig.Rhino commands (no GH graph) and gates EACH op with `WaitForPenumbraFrame` to catch BUG-0017-class regressions (copy didn't adopt / delete didn't re-display).
- **`workloads/rhino/suites/cpig-bool-refactor.json`** — 14 tests `cpig-bool-refactor-00..13`. Phase 1 FieldWrapper refactor regression guard. Same pattern as cpig-fieldops: WaitForPenumbraFrame after every boolean/offset/smooth op is the deterministic gate.

### 2. Penumbra-specific Canary agent code

| File | Role |
|---|---|
| `src/Canary.Agent.Penumbra/Canary.Agent.Penumbra.csproj` | net8.0-windows, refs `Canary.Core` + `Canary.Agent` only; **no external NuGet** (built-in `System.Net.WebSockets.Client`). |
| `src/Canary.Agent.Penumbra/PenumbraBridgeAgent.cs` | The ICanaryAgent over CDP. Owns Vite + Chrome process lifecycle. Translates Canary actions → CDP `Runtime.evaluate` / `Page.captureScreenshot` / `Page.navigate`. Implements `ITelemetryAware` for CDP Console+Log+Network → sink. |
| `src/Canary.Agent.Penumbra/PenumbraConfig.cs` | `PenumbraConfig` + `PenumbraWorkloadConfig` JSON models. Fields: ProjectDir, CdpPort, VitePort, DefaultBackend, DefaultCanvas{Width,Height}, DefaultStabilizeMs, PageLoadTimeoutMs, ChromeFlags. |
| `src/Canary.Agent.Penumbra/PenumbraInstanceProbe.cs` | Reuse-existing-instance probe. UI uses it (`RunPenumbraAsync`) to connect to an already-running Penumbra dev session via the CDP `/json/list` endpoint instead of relaunching. |
| `src/Canary.Agent.Penumbra/ViteManager.cs` | Manages `npm run dev` child process. Spawn-registry registration, port-conflict cleanup (`KillStaleListenerAsync`), `PENUMBRA_NO_AUTO_OPEN=1` env var (Penumbra Bug 0031). 5s wait for port release on teardown. |
| `src/Canary.Agent.Penumbra/CdpInputReplayer.cs` | CDP equivalent of `InputReplayer` — replays mouse `vx,vy` recordings via `Input.dispatchMouseEvent` instead of `SendInput`. Path B (secondary path; today rarely used since camera control is scripted, not recorded). |
| `src/Canary.Core/Cdp/CdpClient.cs` | WebSocket-based CDP JSON-RPC client. Used by both Penumbra + Qualia agents. |
| `src/Canary.Core/Cdp/ChromeLauncher.cs` | Chrome/Edge auto-detect + launch with `--remote-debugging-port`. Used by both Penumbra + Qualia agents. |
| `src/Canary.Core/Cdp/CdpTelemetryStream.cs` | Subscribes `Runtime.consoleAPICalled`, `Log.entryAdded`, `Network.{requestWillBeSent,responseReceived,loadingFailed}` and forwards to `ITelemetrySink` tagged `source: "penumbra"`. |
| `src/Canary.Core/Telemetry/PenumbraPreviewTelemetryTail.cs` | Tails Penumbra's IN-RHINO NDJSON at `%LocalAppData%\Penumbra\preview\telemetry.ndjson`. Used by `RhinoSessionAgent` so supervised Rhino sessions capture Penumbra's domain events (host.start, scene.loaded, frame.real, gl.field.transform, rep.live, render.error) into `telemetry.ndjson`. |
| `src/Canary.Core/Config/TestDefinition.cs` | Penumbra-shaped JSON fields: `SceneSetup { Index, SceneName }`, `CanvasSetup { Width, Height }`, `CameraPosition { Azimuth, Elevation, Distance }`, `TestSetup.{Backend, DisplayPreset}`. The same `TestDefinition` serves both web Penumbra (camera + scene fields) and rhino-workload Penumbra-driving tests. |
| `src/Canary.Core/Orchestration/TestRunner.cs` | `RunAgentTestAsync`/`RunAgentSuiteAsync` — the in-process-agent loop the Penumbra bridge plugs into. `SendAgentSetupAsync` dispatches `SetCanvasSize` → `SetBackend` → `LoadSceneByName`/`LoadScene` → `LoadDisplayPreset` → `RunCommand`(setup.commands[]) → camera per checkpoint → capture. |
| `src/Canary.Harness/Cli/RunCommand.cs` | `RunPenumbraSuiteAsync` — creates the bridge agent once per suite, hooks it to `TelemetrySink`, runs all tests through `RunAgentSuiteAsync`. Triggered when `workload.AgentType == "penumbra-cdp"`. |
| `src/Canary.Harness/Session/SessionAgentFactory.cs` | `CreatePenumbraAsync` — boots a supervised browser session for `canary session start --workload penumbra`. |
| `src/Canary.Harness/Session/RhinoSessionAgent.cs` | Wraps the Rhino named-pipe agent. Implements `ITelemetryAware` and starts a `PenumbraPreviewTelemetryTail` so supervised Rhino sessions capture Penumbra preview events. |
| `src/Canary.UI.Avalonia/ViewModels/TestRunnerViewModel.cs` | `RunPenumbraAsync` — UI flow with `PenumbraInstanceProbe` reuse of an already-running dev instance. |
| `src/Canary.Agent.Rhino/RhinoAgent.cs` (`HandleWaitForPenumbraFrame`, lines ~787–859) | The peer-agnostic frame-readiness gate. Uses `AppDomain.CurrentDomain.GetAssemblies()` + reflection on `Penumbra.Bridge.PenumbraBridge.GetFrameState()` to read `{RealRevision, PresentedRevision, EvalMode, Status, DisabledByError}`. RELATIVE snapshot semantics — waits for the revision to INCREASE past the value captured at action start. |
| `canary-penumbra/` (handoff sources) | Original spec + integration guide + Penumbra-side hooks template. Source of truth for the original surface contract; the production code lives under `src/Canary.Agent.Penumbra/`. The duplicate copies of `Canary.Agent.Penumbra/` and `Canary.Core/Cdp/` inside `canary-penumbra/src/` are historical; the live tree is `src/`. |

### 3. Tests depending on Penumbra binary location

**Only two tests reference an environment-rooted Penumbra path** — both file-source captures of the OOP preview-frames dump:

| Test | Path used |
|---|---|
| `workloads/rhino/tests/penumbra-rhino-00-smoke.json` | `%LOCALAPPDATA%\Penumbra\preview-frames\active.png` (checkpoint `filePath`) |
| `workloads/rhino/tests/penumbra-rhino-01-gyroid.json` | `%LOCALAPPDATA%\Penumbra\preview-frames\active.png` (checkpoint `filePath`) |

The C# code references **one** Penumbra-rooted path:

| Code | Path |
|---|---|
| `src/Canary.Core/Telemetry/PenumbraPreviewTelemetryTail.cs` (`DefaultPath`) | `%LocalAppData%\Penumbra\preview\telemetry.ndjson` |

**No `G:\My Drive\builds\Penumbra` or build-artifact path references** anywhere in the live tree. The "binary location" for the Penumbra Rhino plug-in is implicit: it must be **registered for auto-load** in the Canary Rhino instance (documented in `CLAUDE.md` and per-test `description` blocks). Canary never copies, builds, or paths-into the `.rhp`.

The web workload uses `penumbraConfig.projectDir` (default `C:\Repos\Penumbra` in workload.json) to point Vite at a checkout — that's a source path, not a binary path.

### 4. Telemetry expectations

Canary consumes Penumbra telemetry through two streams:

#### Stream A — CDP Console / Log / Network (web Penumbra workload)

`CdpTelemetryStream.EnableAndSubscribeAsync` enables Console + Log + Network + Runtime domains and writes each event with `source: "penumbra"` into `workloads/<w>/results/[<suite>/]telemetry.ndjson`. **The agent does NOT parse domain semantics from these events** — it persists them for human/agent post-mortem inspection.

#### Stream B — `__canary*` JS API on `window` (web Penumbra workload)

`PenumbraBridgeAgent` calls these via `Runtime.evaluate`. Each is a contract that Penumbra must keep. Test JSONs ALSO call them directly via `setup.commands[]`. Comprehensive list of JS hooks called by Canary today (from `workloads/penumbra/tests/` + agent code):

| Hook | Called by | Used for |
|---|---|---|
| `__canaryGetRendererInfo()` | agent (`HeartbeatAsync`, `WaitForPenumbraReadyAsync`, `WaitForAtlasIfNeededAsync`) | Returns `{backend, fieldCount, hasAtlasFields, atlasBuildComplete, sceneName, sceneIndex, fps}`. Heartbeat liveness + atlas-presence detection. |
| `__canarySetScene(index)` | agent (`LoadSceneAsync`) | Setup-time scene load by integer index. |
| `__canarySetSceneByName(name)` | agent (`LoadSceneByNameAsync`) | Setup-time scene load by case-insensitive name substring. Preferred (scene index varies between WebGL2/WebGPU). |
| `__canarySetCamera(az, el, dist)` | agent (`SetCameraAsync`) + bug-0036 soak test inline | Set spherical camera position per checkpoint. |
| `__canaryWaitForPresentedFrame(timeoutMs)` | agent (`WaitForStableAsync`) | Confirm a full-res frame actually rendered AFTER the latest change. Sprint step 2b. |
| `__canaryWaitForAtlasPipelineReady(timeoutMs)` | agent (`WaitForAtlasIfNeededAsync`) AND ~80 test JSONs | Strong wait for the WebGPU atlas pipeline to be presentable. |
| `__canaryHideUI(true)` | agent (`CaptureScreenshotAsync` — enforced contract) AND many test JSONs | Hide HUD overlays before capture; also primes full-res capture. |
| `__canaryLockSize` | agent (`LockCanvasSizeAsync` injects) | Boolean flag — Penumbra's render loop must NOT auto-resize the canvas when set. |
| `__canaryRenderer` / `__canaryPass` | agent (`LoadDisplayPresetAsync`) | Renderer instance + Pass instance — `loadDisplayPreset` is called on whichever exists. |
| `__canarySetDisplayMode(mode)` | tests directly | Toggle display mode (viz-aabbs, viz-cascades, viz-centroids, viz-field-id, viz-normal, wire-bricks, wire-cascades, eval-tape, point-cloud, etc.). |
| `__canarySetDisplayState(patch)` | tests directly | Merge a patch into the renderer's DisplayState (tricubic, features, render.{tricubicEnabled,...}). |
| `__canaryGetAvailableModes()` | tests directly | Returns the array of supported display modes (gate on atlas presence). |
| `__canaryToggleOverlay(key)` | tests directly | Toggle debug overlay (cascades, bricks, fieldAABBs, atomAABBs, foreignObjects, pointCloud). |
| `__canaryDiagnoseBricks()` | tests directly | Brick-overlay pipeline state for debugging. |
| `__canaryLoadFeatureProfile(name)` | tests directly | ADR 0014 — load a feature flag profile (default, quality, performance). |
| `__canaryGetFeatureStatus(key)` / `__canaryGetAllFeatureStatus()` / `__canaryValidateCurrentFeatures()` | tests directly | Feature-loader status introspection. |
| `__canarySetComputeMarchToggles({...})` / `__canaryPrebuildComputeMarchPipeline()` / `__canaryRunComputeMarcherSmoke(timeoutMs)` / `__canaryRunComputeCalibration()` / `__canaryRunBudgetAllocator(bool)` / `__canaryComputeMarcherDeterminismCheck(N)` | tests directly | E5 compute marcher control surface. |
| `__canaryLoadThreeMFFromUrl(url)` / `__canaryLoadSTLFromUrl(url)` | tests directly | Bug-0035 + dense/volumetric import regression guards. |
| `__canaryApplyMaterialPreset(name)` / `__canaryApplyEnvironmentPreset(name)` / `__canarySetFog(...)` / `__canarySetLighting(...)` / `__canarySetBackgroundColor(color)` / `__canarySetComposite(...)` / `__canarySetFieldEffect(...)` / `__canarySetFieldEmissive(...)` / `__canarySetClassificationMethod(...)` | tests directly | Material / environment / effects pipeline. |
| `__canaryGetFields()` / `__canaryGetEvalSnapshot()` / `__canaryExtractMeshDeterminism()` / `__canaryGetMultiscaleSignalState()` / `__canaryGetRenderLoopState()` / `__canaryGetDeviceState()` | tests directly | State introspection — bug-0036 soak, multiscale signals, mesh determinism. |
| `__canaryForceMarkDirty()` | tests directly | Force a render-loop redraw (post C2 event-gate). |
| `__canaryClearC()` / `__canaryGetC()` | bug-0036 soak | Internal-state clear/read for soak sampling. |
| `__canaryPauseAnimation` | (flag set by `__canarySetScene`) | Pause the scene-2 torus orbit so captures are deterministic. |

#### Stream C — File NDJSON (`%LocalAppData%\Penumbra\preview\telemetry.ndjson`)

The IN-RHINO Penumbra plug-in writes this; **Canary tails it but does not assert on it.** Captured into supervised-session `telemetry.ndjson` so the SESSION_REPORT renders a "Penumbra preview telemetry" section. Observed phases the parser knows to surface (per `CLAUDE.md` rhino-session feature doc, `PenumbraPreviewTelemetryTail.cs`, and `cpig-fieldops` + `cpig-bool-refactor` suite docs):

- `host.start` — Penumbra Rhino plug-in came up (carries `hostDir`, `cacheDir`).
- `host.warn.stray` — stray-node warning.
- `scene.loaded` (alias `gl.scene.loaded`) — a scene was loaded (carries `atoms`, `source`, `bounds`, `+tape/+grid` markers).
- `gl.field.transform` — a gumball moved a field.
- `rep.live` — live display rep switched (`tape` / `dense-grid` / etc.).
- `frame.real` — a real (non-companion) frame was presented.
- `render.error` — render-pipeline error.
- `cpig.copy:native-adopt` — CPig watcher adopted a native Rhino copy.
- `cpig.delete` — CPig deleted a field.
- `cpig.reconcile` — CPig idle reconciler ran.
- `cpig.field.added`, `cpig.boolean.start`, `cpig.boolean.aabb`, `cpig.boolean.created` — Phase 1 structural composite events (cpig-bool-refactor diagnosis side-channel).
- `gl.compile3mf` — node-side 3MF compile (legacy sphere-stub regression marker).

The parser is **structurally tolerant** — it accepts any `{t, kind, level, source, data}` envelope, wraps the domain `kind`/`data.phase` as `data.event` and keeps the original `data` as `data.payload`. **No telemetry-content asserts exist today** (a CPig field-count panel asserts on GH side instead).

### 5. The Rhino agent and Penumbra-in-Rhino tests

The Rhino agent **is not Penumbra-specific** — it's `Canary.Agent.Rhino` (`net48` Rhino plug-in DLL) hosted in-process by Rhino, communicating with Canary over `canary-rhino-<pid>` named pipe.

Penumbra-in-Rhino tests reach the Penumbra plug-in by:

1. **Running Rhino commands** via `RunCommand` action (`RhinoApp.RunScript`): `PenumbraOopShow` (legacy OOP overlay), `_PenumbraPreviewBackend _Glsl` (in-process GLSL studio), `_CPigSphere`/`_CPigGyroid`/etc. (CPig.Rhino fields → CPig auto-display routes to the Penumbra backend).
2. **Driving Grasshopper** via `GrasshopperSetToggle` / `GrasshopperSetPanelText` (loads a Slop fixture that wires CPig Penumbra Preview).
3. **Reading state** via `WaitForPenumbraFrame` action — the only Penumbra-coupled handler in the Rhino agent.

Relevant files:

- `src/Canary.Agent.Rhino/RhinoAgent.cs` — all 10+ action handlers including the Penumbra-coupled `HandleWaitForPenumbraFrame` (lines 787–859).
- `src/Canary.Agent.Rhino/CanaryRhinoPlugin.cs` — Rhino plug-in entry; starts `AgentServer` on pipe.
- `src/Canary.Agent.Rhino/RhinoScreenCapture.cs`, `FullScreenCapture.cs` — capture surface (viewport-source captures include the Penumbra GLSL conduit's `PostDrawObjects` draw; DrawForeground-blit overlays are NOT capturable).

### 6. Penumbra-specific actions in the Rhino agent — contract

**Only one Penumbra-named action**: `WaitForPenumbraFrame`.

Parameters:
- `timeoutMs` (int, default 120000 — overridden to 180000 by test JSONs)
- `minRevision` (int, deprecated — parsed for backwards compat but no longer gates; the wait is now RELATIVE)
- `requireReal` (bool, default true — wait for `RealRevision` to advance; false waits for any presented frame)

Contract:
1. The Penumbra Rhino plug-in must load assembly `Penumbra.Bridge` exposing `public static class PenumbraBridge` with `public static <FrameState> GetFrameState()`.
2. `FrameState` must be a type (struct or class) with these PUBLIC FIELDS (not properties): `RealRevision: long`, `PresentedRevision: long`, `DisabledByError: bool`, `EvalMode: string`, `Status: string`.
3. Reflection is via `AppDomain.CurrentDomain.GetAssemblies()` then `asm.GetType("Penumbra.Bridge.PenumbraBridge")` → `bridgeType.GetMethod("GetFrameState", Public|Static)` → `st.GetField(...)`.

The handler:
- Returns `Success=false` if the Bridge isn't loaded (so a CPig-only test in an environment without Penumbra still fails loudly rather than hanging).
- Snapshots the revision on first read (`baseline`), returns Success when the target revision (`RealRevision` if `requireReal` else `PresentedRevision`) increases past baseline.
- Returns Success=false if `DisabledByError == true` ("Penumbra viewer disabled by error: <status>").
- Pumps `RhinoApp.Wait()` between polls — load-bearing because CPig idle-scheduled adoption/reconcile only runs when the message pump turns.
- Default timeout pre-test 180s in JSONs.

**Other Penumbra-relevant Rhino-agent actions** (not Penumbra-named but Penumbra-driving):

- `RunCommand` — used to run `PenumbraOopShow`, `_PenumbraPreviewBackend _Glsl`, `_CPigSphere`, `_CPigGyroid`, etc.
- `GrasshopperSetToggle("Build", true)` — triggers CPig graph push → Penumbra preview.
- `GrasshopperSetPanelText("JsonPath", "...53_penumbra_preview.json")` — points the Slop loader at a Penumbra-Preview-bearing fixture.
- `GrasshopperGetPanelText("Status"|"Connected"|"SlopSuccess"|"SlopLog")` — used by `PanelEquals`/`PanelContains`/`PanelDoesNotContain` asserts to verify CPig push success.
- `WaitForGrasshopperSolution(timeoutMs)` — waits for the CPig push to quiesce before declaring the scene ready.

### 7. Session / orchestration model

#### Web (Penumbra workload)

`canary run --workload penumbra [--suite <s>] [--test <t>] [--mode pixel-diff|vlm|both]` flow:

1. CLI dispatch in `RunCommand.RunAsync` reads `workload.json`, dispatches on `agentType`. `"penumbra-cdp"` routes to `RunPenumbraSuiteAsync`.
2. `RunPenumbraSuiteAsync` constructs `PenumbraBridgeAgent` ONCE per invocation, calls `InitializeAsync` (starts Vite → launches Chrome → navigates → locks canvas → measures canvas offset), registers `TelemetrySink`, then loops the test list through `runner.RunAgentSuiteAsync`.
3. Per test, `RunAgentTestAsync` runs: Heartbeat → `SendAgentSetupAsync` (SetCanvasSize → SetBackend → LoadScene* → LoadDisplayPreset → RunCommand(setup.commands[])) → pre-checkpoint Actions → per-checkpoint SetCamera + DispatchAgentCheckpointAsync (capture + compare) → post-checkpoint Asserts → BuildComposite.
4. **Every test reruns `SetBackend`** because the JSON declares `setup.backend = "webgpu"` — `SetBackend` re-navigates Chrome to `?autostart=true&backend=webgpu`, triggering full Penumbra re-init (~30–90s WebGPU adapter + Dawn pipeline build). `pageLoadTimeoutMs=180000` accommodates this; `NavigateWithRetryAsync` retries once on TimeoutException.
5. UI flow (`TestRunnerViewModel.RunPenumbraAsync`) probes for an EXISTING Penumbra instance first via `PenumbraInstanceProbe` and reuses it (`InitializeFromExistingAsync`) instead of relaunching.

Per-suite output: `workloads/penumbra/results/[<suite>/]{telemetry.ndjson, report.html, junit.xml}` plus per-test `runs/<yyyyMMdd-HHmmss-xxxx>/{REPORT.md, result.json, candidates/, baselines/, diffs/}`.

#### In-Rhino (Penumbra-in-Rhino tests under the rhino workload)

`canary run --workload rhino --suite {penumbra,penumbra-glsl,cpig-fieldops,cpig-bool-refactor}` flow:

1. `RunCommand` dispatch falls through to the `runMode: shared` branch (all penumbra-in-Rhino tests declare `runMode: shared`).
2. `runner.RunSharedSuiteAsync` launches Rhino ONCE, opens `fixtures/cpig_slop_loader.gh`, and runs every test in sequence inside that one Rhino instance.
3. Per test: actions list runs sequentially. Cleanup pulse (Build off → Cleanup on → wait → Cleanup off) is required to reset the GH graph between tests. `WaitForPenumbraFrame` is RELATIVE so a chained second test waits for ITS OWN new frame past the first test's leftover revision.
4. The Penumbra Rhino plug-in writes `%LocalAppData%\Penumbra\preview\telemetry.ndjson` independently of Canary; Canary does NOT tail it during test runs (only during supervised sessions).

#### Supervised sessions

`canary session start --workload {penumbra|rhino}` flow:

- For penumbra: `SessionAgentFactory.CreatePenumbraAsync` creates a `PenumbraBridgeAgent` and `InitializeAsync`s it. The REPL (`c`/`a`/`n`/`q`) emits captures into `workloads/penumbra/sessions/<id>/captures/`. CDP Console+Log+Network → telemetry.ndjson.
- For rhino: `SessionAgentFactory.CreateRhinoAsync` returns `RhinoSessionAgent` with `RegisterTelemetrySink` wired to a `PenumbraPreviewTelemetryTail`. Operator drives Rhino by hand; the NDJSON tail captures any Penumbra preview events that fire during the session.

### 8. CI integration

**Penumbra tests are NOT in CI today.** No GitHub Actions workflows (`.github/workflows/` doesn't exist), no Azure pipelines, no GitLab CI. The only automation scripts under `scripts/` are operator helpers (`add-preview-trio.ps1`, `cpig-test-from-slop.ps1`, `retrofit_kin_fourview.py`).

The "gate" today is **manual + agent-driven**:
- Operator runs `canary run --workload penumbra --suite smoke` (or `full`, or a matrix suite) after a Penumbra change.
- Agents run `canary run --workload rhino --suite penumbra-glsl` after a Penumbra Rhino plug-in change.
- Results are written to `workloads/<w>/results/<suite>/` and reviewed via the Canary UI (HTML report, per-test runs).
- The Canary debug-overhaul shipped 2026-05-24 introduced MCP server tools (`list_runs`, `get_run`, `list_sessions`, `get_session_report`, `list_feedback`, etc.) so an agent can poll past-run + session results without screen-scraping. That's the closest thing to a "CI surface" — agent-driven, not pipeline-driven.

The Canary repo also follows a cross-repo update protocol (`CLAUDE.md` "Cross-Repo Change Protocol") that requires any contract-changing PR to update the affected repo's `CLAUDE.md` and `spec/PEERS.md`. That's the human/agent contract gate.

## Analysis — which Penumbra changes FORCE Canary changes?

### A. The scene compiler graduating into a native C++ DLL with embedded QuickJS

#### Web Penumbra workload (`workloads/penumbra/`, 136 tests, 25 suites)

| Risk surface | Forces a Canary change? | Why |
|---|---|---|
| Vite dev server presence | **No** if the Vite path still serves `localhost:3000` with the same `?autostart=true&backend=webgpu` query → no change to `PenumbraBridgeAgent`. | The agent only cares that the dev-time HTTP entrypoint still works; what happens inside (V8 → QuickJS, TS scene compiler → native compile) is invisible to CDP. |
| `window.__canary*` JS API surface | **YES** — every hook (40+ enumerated above) must remain callable from `Runtime.evaluate` and return the same shapes. | If the QuickJS host exposes the canary surface differently (e.g. fronted by a different global than `window`, or async semantics changed, or some hook moved to a worker), 136 tests + the agent's `WaitForStableAsync`/`HeartbeatAsync`/`LoadScene*`/`SetCamera`/`SetCanvasSize` all break. The TS canary-hooks template (`canary-penumbra/penumbra-hooks/canary-hooks.ts`) needs a 1:1 port; any divergence cascades. |
| `__canaryGetRendererInfo()` shape | **YES (sticky)** — fields `{backend, fieldCount, hasAtlasFields, atlasBuildComplete, sceneName, sceneIndex, fps}` are baked into `WaitForPenumbraReadyAsync` (waits until the call returns a non-null object) and `WaitForAtlasIfNeededAsync` (reads `hasAtlasFields`). | If the native+QuickJS surface drops `hasAtlasFields`, every atlas-bearing test starts capturing pre-presentable frames silently. The 60s/240s CDP eval headroom + 90s `__canaryWaitForAtlasPipelineReady` calibration also assume the same atlas-pipeline cost model. |
| Setup commands embedded as raw JS strings in 136 test JSONs | **High blast radius if QuickJS rejects ANY pattern** the tests use (top-level `await`, async-arrow with `.then()`, IIFE, dynamic `Promise`). | The agent's CDP layer is documented (BUG-0012) as forbidding bare top-level `await`; tests work around it with `.then(...)` shape. QuickJS does NOT support `async/await` in older spec levels — verify the embedded engine handles ES2022 features the test JSONs use (e.g. `(async () => {...})()` IIFE in `feature-loader-stub-wiring.json` line 19, the `Promise` chains in `__canaryWaitForAtlasPipelineReady(120000).then(...)`). |
| `CDP Page.captureScreenshot` clip | **No** — agnostic to JS engine. | Pure browser/DOM API; native + QuickJS doesn't affect it. |
| WebGPU device-loss recovery & atlas pipeline rebuild | **No directly** — but the 240s CDP eval ceiling + 90s atlas wait + 180s `pageLoadTimeoutMs` are tuned to the CURRENT Dawn pipeline build cost. If the native compiler shortens that, no problem (faster = passing); if it LENGTHENS it, timeouts cascade. | Monitoring requirement: keep an eye on `WaitForAtlasIfNeededAsync` warn-log frequency. |
| `displayPreset` resolution path (`pass.loadDisplayPreset(name)`) | **YES if the Pass/Renderer access pattern changes.** | `LoadDisplayPresetAsync` (PenumbraBridgeAgent line 420) does `target = window.__canaryPass || window.__canaryRenderer` and calls `target.loadDisplayPreset(name)`. Both globals must remain exposed and `loadDisplayPreset` must remain a method on at least one. 8 `preset-*` tests + the `display-matrix`/`display-modes`/`display-smoke` suites use this. |

#### In-Rhino Penumbra (`workloads/rhino/suites/penumbra*,cpig-fieldops,cpig-bool-refactor`)

| Risk surface | Forces a Canary change? | Why |
|---|---|---|
| `Penumbra.Bridge.PenumbraBridge.GetFrameState()` symbol | **YES if signature changes.** | `RhinoAgent.HandleWaitForPenumbraFrame` reflects into this exact type + method name + field set (`RealRevision`, `PresentedRevision`, `EvalMode`, `Status`, `DisabledByError` as PUBLIC FIELDS). Any rename, property/field flip, or namespace move silently breaks all 22+ tests in the 4 Penumbra-driving rhino suites that gate captures on `WaitForPenumbraFrame`. |
| The Bridge being loaded in-process by Rhino | **YES** — when Jint is replaced by the native+QuickJS host, the `Penumbra.Bridge.dll` must still be loaded into the Rhino AppDomain. | If the new host runs out-of-process, `AppDomain.CurrentDomain.GetAssemblies()` won't find the type. Canary needs an alternate channel — file-based polling, named pipe, or HTTP probe — and `WaitForPenumbraFrame` becomes a peer-IO action. Documenting an `IPenumbraFrameProvider` named-pipe contract would isolate this. |
| `PenumbraOopShow` / `_PenumbraPreviewBackend _Glsl` Rhino commands | **YES if command names change.** | Test JSONs call these by name via `RhinoApp.RunScript`. Already migrated once (`PenumbraShow` → `PenumbraOopShow`, 2026-06-13 cleanup). |
| `%LocalAppData%\Penumbra\preview-frames\active.png` file dump | **YES** — both `penumbra-rhino-00-smoke` + `penumbra-rhino-01-gyroid` capture this file. | If the OOP path is retired (the `penumbra-glsl` suite is the replacement) the smoke test stays only as long as it's the working fallback; once removed, those checkpoints fail. **Mitigation**: the existing rhino-session feature doc tags these as DEPRECATED. |
| `%LocalAppData%\Penumbra\preview\telemetry.ndjson` envelope | **NO** if the envelope stays `{t, kind, level, source, data}` and `data.phase` keeps the human-readable phase name. | `PenumbraPreviewTelemetryTail.ParsePenumbraLine` is structurally tolerant — it extracts `data.phase` (falls back to `kind`) and wraps the entire payload. As long as JSON-per-line + same envelope shape are preserved, new phase names appear in the SESSION_REPORT verbatim with no Canary change. |
| `%LocalAppData%\Penumbra\pipeline-cache` (shader compile cache) | **No** — Canary doesn't reference this path. It's mentioned in CLAUDE.md docs only as operator context. | |

### B. FSM + cascade scheduler refactored into a TS kernel

This is mostly an INTERNAL refactor. Canary cares about it only where the kernel exposes a different surface:

- If `setDisplayState({features: {...}})`, `__canarySetDisplayMode(mode)`, `__canaryToggleOverlay(key)`, `__canarySetComputeMarchToggles({...})` etc. still work with the SAME argument shapes and SAME observable side effects, no Canary change is required.
- If the kernel removes "imperative" mode-set semantics (e.g. setting `viz-cascades` becomes a coordinator-mediated request that may be DENIED), the assembly-display-modes + atlas display-mode + overlay tests can start failing in new ways. A `__canarySetDisplayMode` returning a status object (`{ok, applied, mode}`) instead of throwing on unknown modes is a quiet behaviour change. **Mitigation**: have the new kernel expose `__canaryGetAvailableModes()` (already used) and keep `__canarySetDisplayMode` throwing on rejection so the test JSONs surface the regression.
- If the FSM puts the renderer into a state where `__canaryWaitForAtlasPipelineReady` resolves before the kernel actually flushes the dependent display-mode shader, the new race surface needs a hook. The `WaitForAtlasIfNeededAsync` polling is the gate.
- E5 compute marcher tests (`atlas-blob-compute-*`, 14 tests) drive `__canarySetComputeMarchToggles` + `__canaryRunComputeMarcherSmoke` directly. The scheduler must keep dispatching ≥10 compute passes within the timeout, else the agent reads the smoke return value as failure.

### C. Removing Jint from the Penumbra Rhino plug-in

Today Penumbra uses Jint inside Rhino to host scene-compiler JS in-process. Canary doesn't talk to Jint — but the **Bridge** between Penumbra's JS-side scene state and Canary is the reflection target. Two paths post-Jint:

1. **Native + QuickJS in-process** (the planned host). `Penumbra.Bridge.PenumbraBridge.GetFrameState()` still callable via reflection → no Canary change required if the symbol survives.
2. **Out-of-process native host** (less likely per CLAUDE.md "N0 build GO"). Canary's `WaitForPenumbraFrame` needs a new transport (file-polling on the existing `telemetry.ndjson` would work — block on a new `frame.real` event past a baseline timestamp). That's a NEW action handler in `Canary.Agent.Rhino`; the existing one would log "Bridge not loaded".

Other Jint-removal risks:

- If the new host changes the way commands are registered with Rhino (`PenumbraOopShow`, `_PenumbraPreviewBackend`, `_CPigSphere`), the `RunCommand` actions break by name. Already encountered with `PenumbraShow` → `PenumbraOopShow`; the test JSONs are the only place to update.
- If the new host changes the timing/threading of the in-Rhino conduit (`PostDrawObjects` for the GLSL studio), the `WaitForPenumbraFrame` RELATIVE semantics still hold — but the `requireReal` field would need to know about any new not-real intermediate states (e.g. "node host warming up", "QuickJS compile in flight"). Add a new `EvalMode` value like `"compiling"` and `WaitForPenumbraFrame` returns Success=false until it transitions out.

### Migration-critical issues (in priority order)

1. **`__canary*` global API surface contract** — 40+ hooks. The TS hook template (`canary-penumbra/penumbra-hooks/canary-hooks.ts`) must be ported 1:1 to the new host AND extended for every hook added since (the original template covered ~12 hooks; the live set is ~40). Recommend a **canary-hooks conformance test** suite shipped INSIDE Penumbra that exercises each hook and asserts return shape — runnable BEFORE Canary runs. This is the highest-blast-radius risk.
2. **`Penumbra.Bridge.PenumbraBridge.GetFrameState()` reflection signature** — protect explicitly. Adding a `// TODO(jint-removal)` pin to `RhinoAgent.HandleWaitForPenumbraFrame` to surface it during the migration is cheap insurance.
3. **CDP `Runtime.evaluate` JS semantics** — verify the new host accepts all patterns used in `setup.commands[]` (top-level IIFE, `.then(...)`, multi-statement programs, `(async () => {...})()`, dynamic `throw`). A `tests/Canary.Tests` integration test that runs `__canaryRunComputeMarcherSmoke` end-to-end is the canary canary.
4. **`__canaryGetRendererInfo()` field set** — sticky contract. Don't drop `hasAtlasFields` or `atlasBuildComplete`; the agent gates on them.
5. **`__canaryWaitForAtlasPipelineReady` / `__canaryWaitForPresentedFrame` semantics** — strong always-resolve `{ok, reason}` promise contract. Don't switch to `reject`; the agent catches `Json.Parse` failures, not promise rejections.
6. **Rhino command name stability** — `_PenumbraPreviewBackend`, `PenumbraOopShow`, the `_CPig*` family. Cheaper to update test JSONs than to maintain aliases in the plug-in, but only if the rename is announced.
7. **Preview NDJSON envelope** — `{t, kind, level, source, data}` with `data.phase`. The parser is tolerant; only a wholesale envelope change breaks it.
8. **`%LocalAppData%\Penumbra\{preview,preview-frames}\` paths** — two tests + one tail target hardcode these. If the new host writes elsewhere, the SESSION_REPORT goes empty and the smoke + gyroid tests file-source-fail. Make the path discoverable (env var or workload.json field) before relocating.
9. **`displayPreset` resolution** — the `__canaryRenderer || __canaryPass` fallback assumes BOTH globals exist. Don't drop both.
10. **VLM mode** — orthogonal to all of the above. Each test's `setup.vlm` block is provider-agnostic.

### Recommended migration order

1. **Land the new host with Jint-PARITY surface first.** Ship `__canary*` shims that delegate to the new kernel; ship `Penumbra.Bridge.PenumbraBridge.GetFrameState()` with the same field names. Run `canary run --workload penumbra --suite smoke` + `--suite full` + `canary run --workload rhino --suite penumbra-glsl` — green is the gate.
2. **Then refactor the kernel internals**, asserting `__canary*` outputs remain bit-identical for the camera-orbit + display-mode tests. The pixel-diff baselines themselves are a regression guard for "shape of rendered output."
3. **Then announce + execute any contract-changing renames** (e.g. dropping deprecated hooks). One PR per rename, with the Canary test JSONs + agent code updated in the same PR per CLAUDE.md cross-repo protocol. Don't change the contract in the Penumbra side without simultaneously updating Canary.
4. **Once `penumbra-rhino` (OOP) suite's reason-to-exist disappears** (the `penumbra-glsl` suite covers CPig→Penumbra live preview end-to-end), DELETE the OOP suite and the two preview-frames file-source tests. That removes the `%LocalAppData%\Penumbra\preview-frames\active.png` dependency entirely.

## Conclusions

- **Canary's coupling to Penumbra has TWO tight surfaces**: the `window.__canary*` JS API (40+ hooks across web Penumbra) and `Penumbra.Bridge.PenumbraBridge.GetFrameState()` (reflection from the Rhino agent). Both must be preserved bit-for-bit across the Jint→QuickJS migration if Canary is to stay green.
- **The CDP transport, the agent itself, the test definition schema, and the orchestration loop are all engine-agnostic.** No code in `PenumbraBridgeAgent`, `TestRunner`, `TestDefinition`, or the CDP layer needs to know whether Penumbra's scene compiler runs in TS, V8, QuickJS, or native C++.
- **Telemetry (CDP stream + NDJSON tail) is structurally tolerant** and will absorb new phase names / new fields without code change.
- **Two tests reference file paths in `%LocalAppData%\Penumbra\preview-frames\`** and one tailer references `%LocalAppData%\Penumbra\preview\telemetry.ndjson`. These are the only filesystem coupling.
- **Penumbra tests are not in CI** — the gate is operator + agent-initiated. That's the green-throughout-the-overhaul constraint: every Penumbra PR needs `canary run --workload penumbra --suite smoke` (≥5 min) AND `canary run --workload rhino --suite penumbra-glsl` (≥2 min, requires Rhino + auto-loaded `.rhp`) to pass.
- **The biggest gap is the 40+ `__canary*` hooks** — only ~12 are in the canonical template (`canary-penumbra/penumbra-hooks/canary-hooks.ts`); the other ~28 grew organically with each feature and have no central spec. Creating a `PENUMBRA_CANARY_HOOKS.md` enumerating them + their return shapes is a low-cost migration-safety win that this audit unlocks.

## Recommended actions

- [ ] **Write a `PENUMBRA_CANARY_HOOKS.md` spec** enumerating all 40+ `__canary*` hooks with signatures + expected return shapes + which tests use each. Source: this audit's "Telemetry expectations / Stream B" table + a `grep -rho '__canary[A-Za-z]*'` over `workloads/penumbra/tests/`. Put it under `spec/` in BOTH Canary and Penumbra so both sides reference one source of truth.
- [ ] **Pin `RhinoAgent.HandleWaitForPenumbraFrame` with `// TODO(jint-removal)`** so the contract surface surfaces during the migration.
- [ ] **Ship a Penumbra-side conformance test** that calls each `__canary*` hook and asserts return shape. Run it BEFORE invoking Canary in the migration PR's local gate.
- [ ] **Make `%LocalAppData%\Penumbra\preview*` paths configurable** in workload.json + `PenumbraPreviewTelemetryTail.DefaultPath` so a different LocalApp location can be tested without code changes.
- [ ] **After GLSL studio reaches CPig parity**, delete the `penumbra` rhino suite (OOP fallback) + its two file-source tests; this removes the `preview-frames/active.png` coupling.
- [ ] **Make CI a thing.** Even a manual-trigger GitHub Action that runs `canary run --workload penumbra --suite smoke --headless` on a self-hosted Windows runner with WebGPU + Chrome would catch the high-blast-radius regressions without operator turnaround. The MCP server gives an agent the polling surface; only the trigger is missing.
- [ ] **Snapshot a "baseline regen" pass** before the migration begins (kept in `workloads/penumbra/baselines/_pre-quickjs-migration/`) so any pixel-diff drift after the migration is attributable.

## Related

- Bugs: (none open at audit time)
- Decisions: ADR 0011 (Penumbra display-preset), ADR 0014/0015 (Penumbra feature loader) — both directly coupled to `__canary*` surface.
- Specs: `Penumbra/spec/CANARY.md` (peer doc — needs the hooks table); `Canary/spec/PENUMBRA_WORKLOAD.md` (referenced in code comments but not enumerated in the spec index).
- Penumbra-side progress logs (`docs/progress/2026-05-*-penumbra-*.md`, `docs/progress/2026-05-09-penumbra-bug-0036-stall-phase-a.md`) — historical record of every contract change the integration has weathered so far.
- Cross-repo memories: `penumbra-rhino-native-host.md` (host migration N0 GO), `penumbra-canary-env-debug.md` (the integration's bug history), `penumbra-bridge-no-default-params.md` + `penumbra-bridge-per-field-contract.md` (the Bridge's existing contract discipline), `penumbra-rhino-select-flicker.md` (a Bridge verb pitfall).
