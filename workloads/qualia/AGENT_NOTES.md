# Qualia — Canary Agent Notes

## Architecture

Qualia is a **browser-based React + Vite app** (not a desktop app — the
April-stub of this file assumed otherwise). The agent at
`src/Canary.Agent.Qualia/` follows the same pattern as
`Canary.Agent.Penumbra`: spawn `npm run dev`, launch Chrome with CDP,
navigate to the dev URL, drive everything through `Runtime.evaluate`
against `window.__canary*` hooks installed by the app.

## Hook surface (Qualia side)

Lives in `Qualia/packages/ui/src/canary-hooks.ts` (app-level hooks), plus
`Qualia/packages/ui/src/debug/Playground.tsx` (scenario / snapshot hooks
installed only while the playground overlay is mounted).

App-level (always available after `__canaryHooksReady === true`):

- `__canaryHooksReady` — boolean marker set true after install.
- `__canaryWaitForReady(timeoutMs)` — resolves once demo data has loaded.
- `__canaryGetAppInfo()` — `{ ready, theme, moduleCount, profile, landingOpen, playgroundOpen }`.
- `__canaryHideUI(hidden)` — hides toolbar/sidebar/panels for canvas-only screenshots.
- `__canaryGetPersonaConfig()` / `__canaryListPersonas()` — registry inspection.
  (Renamed from `__canaryGetModuleConfig` / `__canaryListModules` in
  Qualia Phase 7.2, 2026-05-12; the legacy names remain as
  `@deprecated` aliases for one transition release.)
- `__canarySetPersonaEnabled(id, enabled, opts?)` / `__canaryApplyProfile(name)` — mutation.
  (Renamed from `__canarySetModuleEnabled` in Qualia Phase 7.2, 2026-05-12;
  legacy alias preserved for one transition release. 2026-07-19: additive
  `opts.preserveProfile` third param — without it a toggle flips the
  profile to `'custom'` and drops the whole base-profile settings layer;
  pass `{ preserveProfile: true }` for single-variable persona mutations.
  2-arg calls behave exactly as before.)
- `__canaryShowLandingScreen()` / `__canaryCloseLandingScreen()`.
- `__canaryGetLandingState()` — DOM-driven inspection of the modal.
- `__canaryClickProfilePill(name)` / `__canaryToggleLandingPersona(id)`.
  (`__canaryToggleLandingPersona` was `__canaryToggleLandingModule`
  before Qualia Phase 7.2, 2026-05-12; legacy alias preserved for one
  transition release.)
- `__canaryClickLandingApply()` / `__canaryClickLandingCancel()`.
- `__canaryPlaygroundOpen()` — opens the Debug Playground overlay. Returns
  `{ ok: false, reason: 'module-disabled' }` when `debug.playground`
  isn't enabled. Tests must `ApplyProfile('workshop')` (or
  `SetModuleEnabled('debug.playground', true)`) first.
- `__canaryPlaygroundClose()` / `__canaryPlaygroundIsOpen()`.

**qualia-v4 hooks** (added 2026-05-12; cover the pointer / qverse / RAG
UI surface for VLM testing — see `suites/qualia-v4-ui.json`):

- `__canaryAddNode({ id, type?, label?, importance? })` — fixture a graph node.
- `__canarySelectNode(nodeId)` — select (drives PropertiesPanel content).
- `__canaryAddPointer({ nodeId, kind, path?, url?, bytes?, encoding?, role?, slot?, status?, lastKnownHash? })` —
  programmatically add a pointer. `kind` ∈ `file|directory|url|inline`. Returns the new pointer id.
- `__canarySetPointerStatus(nodeId, pointerId, status)` — flip a pointer's
  `status` (`live|stale|missing|conflicting|unchecked`) for fixture state.
- `__canaryRefreshPointers()` — run the resolver against every pointer
  (same as the Refresh toolbar button). Returns `{ missing, conflicting }` counts.
- `__canarySwitchContext(contextId | null)` — `null` = superposition.
- `__canarySpawnChildContext({ parentNodeId, parentContextId?, childContextId?, label?, nodeIds? })` —
  fixture a child context off a parent node so tests can screenshot the
  QverseNavigator + Breadcrumb in a nested state without driving a
  real directory scan.
- `__canaryGetPointerSection()` — DOM-readout of the Pointers section
  on the right Properties panel.
- `__canaryOpenAddPointerForm()` — clicks the `+ Add pointer` button.
- `__canaryClickRefresh()` — clicks the Refresh toolbar button.
- `__canaryClickPropTab('properties' | 'notes' | 'behavior' | 'edit')` —
  switches the Properties panel sub-tab.
- `__canaryGetDeadNodePrompt()` — modal state readout.
- `__canaryGetCrossQverseBadge()` — corner-badge text + visibility.
- `__canaryGetQverseNavigator()` — context count + active label.
- `__canaryGetBreadcrumb()` — `{ nested, labels[], activeLabel }`.
- `__canaryGetRagIndicator()` — top-center pill state.
- `__canaryGetProgressBadgeState()` — bottom-right EagerExtractionProgressBadge
  state. Returns `{ visible, inFlight, queueDepth, model, text }`. Added
  2026-05-27 (Qualia Move 3, eager-L3 Phase 3) to drive the
  `eager-l3-progress-badge` fixture. Reads `data-in-flight` /
  `data-queue-depth` / `data-model` attributes off
  `.qualia-eager-extraction-badge`.

**Move 4 dev-test extensions (2026-05-27):**

The `rag.extraction-queue` dev test now surfaces a `skipped(byteCap)=N`
fragment when extractions are skipped by the new
`eagerL3MaxContentBytes` extractor-side gate. The `sidecar.behavior-cache`
dev test summary now appends `provider[ollama=N,openai-compat=M,...]`
with per-provider entry counts (pre-Move-4 entries with no provider
field bucket as `legacy`). The `provider-swap` fixture parses this
fragment to assert post-swap re-extraction landed under the new
provider id.

**Editing VLM prompts.** Every `mode: "vlm"` checkpoint's `description`
field is the prompt sent to Gemma — that's the *editable expectation
surface*. Tweak the description text to refine pass/fail criteria as
the UI evolves; no code change required. The first `setup.vlmDescription`
acts as test-wide context the model sees before each checkpoint.

Playground-scoped (only installed while the overlay is mounted — call
`PlaygroundOpen` first):

- `__canaryPlaygroundGetState()` — `{ activeScenario, params, snapshotCount,
  nodeCount, edgeCount, moduleProfile }`.
- `__canaryPlaygroundListScenarios()` — descriptor metadata for every
  scenario id (`random | grid | tree | scale-free | stress-1k | stress-10k`).
- `__canaryPlaygroundLoadScenario(id, paramsOverride?)` — switch scenario.
- `__canaryPlaygroundSetParam(key, value)` — adjust the active scenario's param.
- `__canaryPlaygroundListSnapshots()` / `__canaryPlaygroundSaveSnapshot(label)`
  / `__canaryPlaygroundRestoreSnapshot(id)` / `__canaryPlaygroundDeleteSnapshot(id)`.

All hooks return `{ ok, value | reason }` envelopes for failure paths.

## Agent action mapping

| `ICanaryAgent.ExecuteAsync` action | What it does |
|---|---|
| `RunCommand` | Evaluate an arbitrary JS expression — the catch-all for anything not covered by a named action. |
| `WaitForReady` | Poll `__canaryWaitForReady` until app reports ready or timeout. |
| `WaitForStable` | `Task.Delay(ms)`. |
| `Reload` | Re-navigate to the current Vite URL + re-wait for `__canaryHooksReady`. Preserves localStorage (intentionally — the calling test seeded it via `setup.commands` and needs the new state to survive React's re-mount). Mirrors steps 5-7 of `InitializeAsync` minus the storage clear. Use this in `actions` (NOT `setup.commands`) to retrigger GRAPH_LOAD or other on-mount logic mid-test. Added 2026-05-25 to unblock the eager-L3 cold-launch / warm-launch / provider-swap fixtures whose pure-JS `window.location.reload()` was killing the CDP execution context ("Inspected target navigated or closed"). |
| `SetCanvasSize` | Set `documentElement` size — used to control screenshot dimensions. |
| `HideUI` | Toggle the chrome-hide CSS class via `__canaryHideUI`. |
| `ApplyProfile` | `__canaryApplyProfile(name)`. |
| `SetModuleEnabled` | `__canarySetPersonaEnabled(id, enabled)` (action name preserved for backwards compat; JS hook renamed in Qualia Phase 7.2, 2026-05-12). |
| `ShowLandingScreen` / `CloseLandingScreen` | Open / close the modal. |
| `ClickProfilePill` | Click a pill by name (minimal/standard/cinematic/workshop). |
| `ToggleLandingModule` | Toggle a persona checkbox by id inside the modal (action name preserved for backwards compat; JS hook renamed in Qualia Phase 7.2, 2026-05-12). |
| `ClickLandingApply` / `ClickLandingCancel` | Footer buttons. |
| `ClearStorage` | `localStorage.clear() + sessionStorage.clear()`. |
| `PlaygroundOpen` / `PlaygroundClose` | Toggle the Wave 0.B Debug Playground overlay (gated by the `debug.playground` module — `ApplyProfile('workshop')` first). |
| `PlaygroundLoadScenario` | Switch to a named scenario (`random`/`grid`/`tree`/`scale-free`/`stress-1k`/`stress-10k`). Optional `paramsJson` overrides per-scenario knobs. |
| `PlaygroundSetParam` | Adjust a single scenario param (e.g. `nodeCount=120`); regenerates + reruns layout. |
| `PlaygroundSaveSnapshot` | Persist current scratch graph + module config + camera to `localStorage[qualia.playground.snapshots]`. |
| `PlaygroundRestoreSnapshot` / `PlaygroundDeleteSnapshot` | Replay or remove a snapshot by id. |
| `PlaygroundListSnapshots` / `PlaygroundGetState` | Inspection. |

## Configuration

`workload.json` → `qualiaConfig`:

- `projectDir` — Qualia repo root (default `C:\Repos\Qualia`).
- `vitePort` — default 5173.
- `cdpPort` — default 9223 (Penumbra uses 9222; co-existence by design).
- `defaultCanvasWidth/Height` — 1280×720 default (LandingScreen needs more
  vertical space than Penumbra's 960×540 to avoid scroll).
- `readyTimeoutSec` — default 30.
- `clearLocalStorageOnInit` — default true; ensures first-launch behavior
  (LandingScreen visible, default profile) is reproducible.

## Running tests

```bash
cd C:\Repos\Canary

# Pixel-diff (default) — visual regression vs baseline.
canary run --workload qualia --suite landing-screen

# VLM oracle — Gemma 4 vision via Ollama. Requires `ollama pull gemma4:e4b`.
canary run --workload qualia --suite landing-screen --mode vlm

# Both modes per checkpoint.
canary run --workload qualia --suite landing-screen --mode both
```

## Caveats

- Tests boot a fresh Vite + Chrome per suite. ~5–10s startup overhead;
  the agent doesn't currently support `runMode: shared` (single-launch
  for an entire suite — that's a Canary-Core orchestration concern).
- `__canaryHideUI(true)` doesn't kill the LandingScreen; the modal sits
  on top with its own z-index (200). Use `__canaryCloseLandingScreen`
  first if you want a chrome-free viewport screenshot.
- Penumbra-specific actions (`LoadScene`, `SetCamera`, `LoadDisplayPreset`)
  are NOT wired here. If a future Qualia test needs camera control, add
  a `__canarySetCamera` hook on the Qualia side and a corresponding
  agent action.

## Status

Initial implementation — May 8, 2026. LandingScreen + module registry
fixtures are the first batch. fx.* visual tests will follow once D1–D6
have polished implementations beyond the Phase D scaffolds.

Wave 0.B Debug Playground hooks landed 2026-05-10 (Qualia commit + this
Canary update). First playground suite is `playground` (workloads/qualia/
suites/playground.json) — one test per scenario plus a snapshot
round-trip test. fx.* visual tests still queued.

**RH-2 — Multi-display sweep (2026-05-14).** New suite `multi-display`
with 11 tests (`rh2-*.json`) covers a sweep of perf/viewer/theme
variants on the minimal sample (`Qualia/examples/minimal/.qualia`, 9
nodes). Tests use three new Qualia-side hooks:

- `__canaryApplyPerfSnapshot({ theme?, perfSettings?, viewerSettings? })` —
  applies a settings bundle in one call. Schema mirrors what
  `Qualia/packages/ui/src/snapshot.ts` emits, so an existing snapshot
  can be replayed.
- `__canaryWaitForRenderSettled(timeoutMs = 5000)` — resolves once two
  consecutive frames have the same LOD digest. Use after
  ApplyPerfSnapshot before screenshotting.
- `__canaryLoadMinimalSample()` — loads `examples/minimal/.qualia` via
  the dev FS plugin or the static mount. Lighter than driving the
  Load-sample button.

Tests don't need new bridge-agent C# actions — `setup.commands` runs
the hooks as raw JS via `Runtime.evaluate`. Composite-grid output
(combining the 11 screenshots into one PNG for at-a-glance comparison)
lands as a Qualia-side post-processing script.

```bash
canary run --workload qualia --suite multi-display
```

## Debug-info hook expansion (2026-05-28, wave 1a)

A batched expansion of the hook surface to make Canary tests assert
specifics (camera pose, planar deviation, mounted personas, sim state,
RAG queue) instead of only screenshotting. Driven by recurring
zoom/pan/planar regressions that pixel-diff couldn't catch
deterministically.

Plan docs:
- Qualia side: `Qualia/docs/plans/2026-05-28-canary-hooks-expansion.md`
- Canary side: `Canary/docs/plans/2026-05-28-canary-hooks-expansion-agent.md`

Wave 1a landed §A (camera), §B (planar), §M (composite snapshot).
Wave 1b will land §C (renderer stats / frame geometry), §D (nodes /
edges), §E (qverse / context), §F (persona registry / mounted host),
§G (simulation), §H (RAG sidecar), §I (event / console / recorder),
§J (non-camera input dispatch), §K (DOM panels), §L (Penumbra bridge).

### New hook surfaces (Qualia side) — wave 1a

**A. Camera / viewport**
- `__canaryGetCameraState()` → `{ position, target, up, fov, aspect, distanceToTarget, projection }`.
- `__canaryGetCameraTransition()` → `{ active, durationMs, elapsedMs, fromTarget, toTarget }`. `fromTarget` / `toTarget` are null — position/target tween state belongs to the camera-controls library and isn't surfaced today. `durationMs` / `elapsedMs` reflect the up-vector slerp window only.
- `__canaryGetPerspectiveLock()` → `{ engaged, axis, origin, side, distance }`.
- `__canaryFitToView(duration?)` — programmatic frame.
- `__canaryAimAtFacet({ axis, origin }, duration?)` — drives `aimAtFacet`.
- `__canarySetCameraState({ position, target, up?, fov? }, duration?)`.
- `__canaryDispatchZoom(deltaY)` / `__canaryDispatchPan({ dx, dy })` / `__canaryDispatchOrbit({ dPhi, dTheta })`
  — route through camera-controls' imperative API (`dolly`/`truck`/`rotate`). Note: under an engaged perspective lock, zoom is a no-op because `SceneManager._applyPerspectiveLock` re-asserts camera distance every frame — tests should expect `before === after` for zoom under a lock.
- `__canaryGetControlsEnabled()` / `__canarySetControlsEnabled(enabled)`.
- `__canaryProjectToScreen([x, y, z])` → `{ x, y } | null`.

**B. Planar confinement**
- `__canaryGetPlanarSettings()` → full `PlanarSettings`.
- `__canarySetPlanarSettings(partial)`.
- `__canaryGetPlaneDeviation({ axisId? })` → `{ axis, origin, maxAbsDeviation, meanAbsDeviation, p95AbsDeviation, nodeCount, outliers[] }`
  — **the computed invariant**; first-class way to assert "the context stayed planar".
- `__canaryCaptureLevel(bandWidth?, name?)` / `__canaryUncaptureLevel(levelId)`.

**M. Composite reader (sparse — 1a fills camera + planar + counts only)**
- `__canaryGetFullSnapshot({ include? })` — orchestrator that calls every reader installed in 1a (camera, planar, node count, edge count, timestamp) and stitches the result. Sections C–L return `null` until wave 1b lands. Used by the supervised-session capture path and the heartbeat upgrade below.

### New named agent actions — wave 1a (7 of 12)

| Action | Hook | Notes |
|---|---|---|
| `DispatchZoom` | `__canaryDispatchZoom` | `deltaY: int`. |
| `DispatchPan` | `__canaryDispatchPan` | `dx: int, dy: int`. |
| `DispatchOrbit` | `__canaryDispatchOrbit` | `dPhi: number, dTheta: number` (radians). |
| `AimAtFacet` | `__canaryAimAtFacet` | `axisX/Y/Z, originX/Y/Z, duration?`. |
| `SetCameraState` | `__canarySetCameraState` | `posX/Y/Z, targetX/Y/Z, upX/Y/Z?, fov?, duration?`. |
| `FitToView` | `__canaryFitToView` | `duration?`. |
| `SetPlanarSettings` | `__canarySetPlanarSettings` | `paramsJson` (partial). |

Deferred to wave 1b (waiting on §G / §J / §L hooks): `DispatchClick`,
`DispatchDoubleClick`, `DispatchHover`, `DispatchDrag`, `DispatchKey`,
`SimStep`, `LoadPenumbraPreset`.

Every reader hook stays **JS-only** — call from `setup.commands` /
checkpoint commands or via the `RunCommand` action:

```json
{
  "action": "RunCommand",
  "params": { "command": "JSON.stringify(window.__canaryGetPlaneDeviation())" }
}
```

### Heartbeat upgrade

`HeartbeatAsync` now prefers `__canaryGetFullSnapshot()` (unwrapped from
its `{ ok, value }` envelope) and falls back to `__canaryGetAppInfo()`.
Supervised-session telemetry streams the full snapshot at every
heartbeat, so camera + planar state is captured passively even when no
checkpoint asks. The fall-through preserves heartbeat for older Qualia
builds during the rollout window.

### Test plan

A single new test `workloads/qualia/tests/diag-context-zoom-pan-planar.json`
covers the headline regression — load minimal sample → switch into a
perspective context → assert lock engaged + plane deviation within ε →
`DispatchZoom` → assert distance changed → `DispatchPan` → assert target
changed → re-assert plane deviation unchanged. Run with `--mode both`
so Gemma also vets the visual.

## Settings resolver verification (ADR 0037 Phase V, 2026-05-30)

The `settings-resolver` suite (13 tests) is the executable verification
of Qualia's settings-resolver architecture (ADR 0037). Each test isolates
one architectural claim: precedence (factory → profile → persona → user),
switch determinism, declarative persona intent, push-channel notification,
and persistence round-trip. Built so a Fail verdict points at a specific
invariant rather than a generic regression bucket.

New Qualia-side hooks (Qualia >= commit `caa3ae0`, 2026-05-30):

- `__canarySetPerfSettings(partial, opts?)` — simulate a UI slider tweak.
  `opts.markTouched` defaults true (matches SceneManager). Pass
  `{ markTouched: false }` for programmatic writes that shouldn't shadow
  profile/persona ownership.
- `__canaryUntouchField(field)` — mirrors PerfPanel's per-field reset.
  Removes the field from both the touched-fields set and persisted
  `userOverrides`. Follow with `__canaryReapplyProfile()` for the
  resolver to recompute + the renderer to apply.
- `__canaryReapplyProfile()` — invokes `window.__qualiaReapplyProfile`
  (installed by `Viewport.tsx` at Wave 6). Returns `{ ok: true, value:
  false }` if the hook isn't installed yet (Viewport not mounted),
  `{ ok: true, value: true }` on success.
- `__canaryGetSettingsChangeCount()` — installs an `onSettingsChange`
  subscription on first call, returns cumulative push-channel fire
  count thereafter. Counter is per-page-load (Reload resets). Pattern:
  read baseline → take action → read again → assert delta.

Existing hooks the suite uses without modification:
`__canaryApplyProfile`, `__canarySetPersonaEnabled`,
`__canaryGetPerfSettings`, `__canaryGetTouchedPerfFields`,
`__canaryGetNodeDisplayMode`, `__canaryGetEdgeRouting`,
`__qualiaGetSettingProvenance`.

### Test groups

- **A (precedence)** — `resolver-a1`..`a4`: profile beats factory,
  user beats profile, untouch reverts to profile, user beats persona.
  Tests provenance flips alongside value flips.
- **B (switch determinism)** — `resolver-b1`..`b3`: A→B→A byte-identity
  on Cinematic, out-of-band axis reset (nodeDisplayMode), edgeRouting
  struct reset.
- **C (declarative persona intent)** — `resolver-c1`..`c3`: laser-rat
  round-trip (multi-field intent revert), junction-preset disable revert
  (the original Wave-4 no-op-disposer footgun), multi-intent last-wins
  resolves to a stable choice.
- **D (push channel)** — `resolver-d1`: 3 setPerfSettings calls produce
  ≥3 `onSettingsChange` fires.
- **E (persistence)** — `resolver-e1`..`e2`: user override round-trips
  through Reload, one-shot legacy migration from the five pre-resolver
  localStorage keys.

Run with `canary run --workload qualia --suite settings-resolver`. All
13 are structural (no pixel-diff or VLM); they take a screenshot as a
record-keeping marker but the verdict is JS-assertion-driven.

## Solution document lifecycle (ADR 0039, 2026-06-17)

Qualia gained an Open/Save document loop (a solution = a vault folder;
`<name>.qualia.json` is its project file). C1 (core state) + C2 (Open)
shipped; C3–C5 (Save / Save As / polish) queued. Two additive,
read-only Qualia-side hooks expose the binding + dirty state so a suite
can assert that opening a solution binds it and clears dirty, and that
edits re-dirty it (the file IO itself is Tauri-gated, so drive the app
and read these):

- `__canaryGetCurrentSolution()` → `{ path, lastSavedAt } | null`
  (null = unsaved/unbound graph; New / Import / demo leave it null).
- `__canaryIsDirty()` → `boolean` (unsaved content edits since the last
  load / clear / open / save; selection / scope-switch / sim-pause /
  bulk-layout do NOT dirty — see ADR 0039).

Both are thin wrappers over `EventStore.currentSolution` / `isDirty`.
No wire-format change to existing hooks. A dedicated suite is not
authored yet; these are here for when C3+ adds save-loop tests.

## Display-sweep W0 probes (2026-07-19)

Added for the display-behavior sweep campaign
(`Qualia/docs/plans/2026-07-19-display-behavior-sweep.md`,
`MultiVerse/prompts/qualia-display-sweep-2026-07-19.md`). All additive;
envelope-wrapped like the rest of the surface.

- `__canaryClearTouchedPerfFields()` → `true` — bulk forget of every
  user-touched perf field. The sweep reset recipe is
  `clearTouchedPerfFields + ApplyProfile(base)`; re-applying the SAME
  profile alone does NOT clear touches (Viewport only wipes them on a
  profile-NAME change).
- `__canaryGetResolvedNodeDisplayMode(nodeId)` → `NodeDisplayMode | null` —
  the RESOLVED mode one node renders with (per-node > per-type > global
  cascade). `__canaryGetNodeDisplayMode()` remains global-only.
- `__canaryGetSocketState()` → `{ variant, count, visible }` — applied
  socket-layer state (vs. the perf SETTING `socketVariant`; divergence
  between the two is a finding, not noise).
- `__canaryGetHaloState()` → `{ variant, count, visible, radiusMul,
  dataChannel, perNode: [{ id, radius }] }` — applied halo-layer state
  incl. the dataMapping channel (`'pagerank'` under Aurora/X-ray/
  Bioluminescent) and per-node uploaded radii (post-dataMapping,
  post-radiusMul).
- `__canaryLoadDDV(opts?)` → `{ selected }` — loads the deterministic
  4-node Debug Default View fixture with a duration-0 camera snap
  (mirrors the toolbar DDV button; destructive). `{ select: false }`
  skips the snapshot's default `ddv-alpha` selection so base states can
  be selection-free.

First live use of `__canaryGetHaloState` surfaced a real anomaly: under
Aurora on the DDV graph the scene's halo layer reports variant
`aurora-vent`, `dataChannel 'pagerank'`, but **0 uploaded instances**
(verified at the raw layer: `geometry.instanceCount === 0`, mesh in
scene). Tracked as Qualia bug 0054; the W2 sweep owns the systematic
chase.
