# CLAUDE.md

## Project: Canary — Cross-Application Visual Regression Testing Harness

### Before Any Work
**Trap-tracker:** read [`CODE-TRACING-CHECKLIST.md`](CODE-TRACING-CHECKLIST.md) before any non-trivial change. Per `MultiVerse/SUPERVISOR.md` Discipline 6, also UPDATE the checklist when this session discovers a new load-bearing path.

### Quick Reference
- **Build**: `dotnet build Canary.sln` (must be 0 errors, 0 warnings)
- **Test**: `dotnet test tests/Canary.Tests/Canary.Tests.csproj --filter "Category=Unit"`
- **UI-first runs (canonical, `MultiVerse/STANDARD.md` §16 locked rule 8)**: every operator-triggered `canary run` launches with `Canary.UI.exe` visible. Implemented in Phase 1 of the debug-overhaul (shipped 2026-05-24). `--headless` bypasses for CI; `--quiet` implies `--headless`. Second `canary run` while UI is up forwards args to the existing instance via named pipe.
- **When the operator says "run canary" (verbal/chat shorthand): do NOT use `--headless`.** The operator means the UI-visible default path: `canary run --workload <w> [--test <t> | --suite <s>]` (NO `--headless` flag) which spawns `Canary.UI.exe` and runs the test there. The UI stays open after the run completes today (UI auto-close-on-done is not wired — `AutoRunArgs` has no `AutoExit` flag in `Canary.Core/Cli/AutoRunArgs.cs`). Only use `--headless` when the operator explicitly asks for headless / CI / scripted use. **You (the agent) may still prefer `--headless` for your own end-to-end verification** because the UI launch flakes from agent sessions per the `canary_launch_from_session` auto-memory — but that's an agent-internal tooling choice, not what the operator means by "run canary".
- **Run Penumbra tests**: `canary run --workload penumbra`
- **Run CPig tests**: `canary run --workload rhino --suite cpig` (from `C:\Repos\Canary`)
- **Supervised session (shipped 2026-05-27 for Qualia + Penumbra; Rhino added 2026-06-02)** — `canary session start --workload {qualia|penumbra|rhino}` boots the target app under supervision (visible Chrome / Rhino, no automated tests) and enters a capture REPL (`c` / `a` / `n` / `q`); or click the **Sessions** nav tab in `Canary.UI` for the GUI flow with Ctrl+Shift+C / Ctrl+Shift+A hotkeys. Sessions live at `workloads/<w>/sessions/<yyyyMMdd-HHmmss-xxxx>/` with `SESSION_REPORT.md` + `session.json` + `telemetry.ndjson` + `captures/`. Rhino sessions now capture **Penumbra in-Rhino preview telemetry** (2026-06-17): `RhinoSessionAgent` (now `ITelemetryAware`) tails `%LocalAppData%\Penumbra\preview\telemetry.ndjson` (`PenumbraPreviewTelemetryTail`) into the session `telemetry.ndjson` + a "Penumbra preview telemetry" section in `SESSION_REPORT.md` — `scene.loaded` (+tape/+grid + bounds), `gl.field.transform` (gumball moves), `rep.live` (display-rep switches), `frame.real`, `render.error`. The Rhino analogue of the CDP Console stream, so a hand-driven CPig/Penumbra session is debuggable from the report. (Penumbra events are captured as `Kind=Log, Source="penumbra"` with the domain kind in `Data.event`, since Penumbra's free-form `kind` doesn't map to Canary's `TelemetryKind` enum.) Rhino-command-history + Slop-log-tail sources remain v2 — see `docs/features/rhino-session.md`. See [`docs/features/supervised-session.md`](docs/features/supervised-session.md) + [`docs/features/rhino-session.md`](docs/features/rhino-session.md). MCP: `list_sessions` / `get_session_report` (bringing the total to 10). **Flight recorder Phase A (2026-07-02, branch `desktop-canary-flight-recorder-a`):** `canary session start --workload rhino --file <abs>.3dm` auto-opens the document; every session writes a `manifest.json` (opened file + SHA256, machine, app+PID, launcher env decisions incl. the per-spawn `PENUMBRA_SESSION_REF`, exit record, harvested Penumbra SHAs); the spawned Rhino PID is watched so a kill/crash before close-out lands in the manifest as `diedUnexpectedly` (+ REPL notice); each capture records before/after Penumbra frame state + active view (agent action `GetPenumbraFrameState`); the previous session's global Penumbra NDJSON is rescued to `telemetry-prior.ndjson` before the spawn truncates it; Rhino TEST runs now feed the per-suite `telemetry.ndjson` too (F7b). Per-spawn env vars MUST go through `AppLauncher.LaunchWithEnv` — the PENUMBRA_* auto-resolve STRIPS process-only vars (see CODE-TRACING-CHECKLIST). Full campaign: `MultiVerse/prompts/canary-session-flight-recorder-2026-07-02.md` (Phase B = Penumbra-side, post-V5-Phase-6).
- **Debug-overhaul (shipped 2026-05-24)** — see [`docs/plans/2026-05-24-canary-debug-overhaul.md`](docs/plans/2026-05-24-canary-debug-overhaul.md) for the design + [`docs/progress/2026-05-24-canary-debug-overhaul.md`](docs/progress/2026-05-24-canary-debug-overhaul.md) for the per-phase log. Headline surfaces operators interact with:
  - **Toolbar mode picker** (pixel-diff / vlm / both) drives `TestRunnerPanel.RunAsync.modeOverride` (resolves the §A1 GUI gap).
  - **Nav tabs** (Tests / Past Runs / Localhost / Feedback / Sessions / Telemetry / Settings) above the existing tree. Sessions tab added 2026-05-27.
  - **Per-run dir** `workloads/<w>/results/[<suite>/]<test>/runs/<yyyyMMdd-HHmmss-xxxx>/REPORT.md` + `result.json` per Phase 3.
  - **Telemetry NDJSON** at `workloads/<w>/results/[<suite>/]telemetry.ndjson` per Phase 2 (CDP Console + Log + Network from Penumbra/Qualia agents).
  - **MCP server** `src/Canary.McpServer/bin/.../Canary.McpServer.exe` exposing 10 tools (8 from debug-overhaul + `list_sessions` / `get_session_report` from supervised-session 2026-05-27) — see [`docs/mcp-server.md`](docs/mcp-server.md) for `.mcp.json` setup.
  - **Feedback inbox** at `docs/feedback/{inbox,triaged,resolved}/` — operator Annotate flow + Claude session-start scan.
- **Status**: see `spec/PHASES.md` for the canonical phase list and the tail of `BUILD_LOG.md` for current progress. Test counts move every commit; check `dotnet test --list-tests | wc -l` rather than trusting any number stamped here.


### Asks queue (docs/asks/)

When Canary needs work from a peer (a new hook, baseline regeneration, contract change), file the ask at `docs/asks/<peer>/<NNNN>-slug.md` per the convention in [`docs/asks/README.md`](docs/asks/README.md). To spawn coordinated peer-side work, instantiate `MultiVerse/prompts/_template-canary-coordinated-work.md` parameterized by `(peer, ask-id)`. The Canary MCP server will expose `list_consumer_asks` + `get_consumer_ask` for live queries — these are queued as a §8.1 addendum to `MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md` Phase 6 (the 8-tool Phase 6 already shipped 2026-05-24; the 2 asks-queue tools await a follow-up session). Per-peer workflow docs live at each peer's `spec/CANARY.md` (CPig, Penumbra, Qualia today; Rhino queued as `docs/asks/rhino/0001-create-canary-md.md`).

### Active Penumbra integration initiatives

For each multi-session Penumbra-side initiative driven through Canary, see the matching progress log per STANDARD.md §19:

- **Penumbra bug 0036 stall observability + soak fixture (Phase A shipped 2026-05-09)** — [`docs/progress/2026-05-09-penumbra-bug-0036-stall-phase-a.md`](docs/progress/2026-05-09-penumbra-bug-0036-stall-phase-a.md)
- **Penumbra A3 multiscale foundation + 7 graduated features (PR #6 + #7 + #8 merged 2026-05-08)** — [`docs/progress/2026-05-08-penumbra-a3-multiscale.md`](docs/progress/2026-05-08-penumbra-a3-multiscale.md)
- **Penumbra Phase 0 feature loader (ADR 0015, shipped 2026-05-07)** — [`docs/progress/2026-05-07-penumbra-phase-0-feature-loader.md`](docs/progress/2026-05-07-penumbra-phase-0-feature-loader.md)
- **Penumbra display-preset workload (Phase 5 shipped 2026-05-03)** — [`docs/progress/2026-05-03-penumbra-display-preset.md`](docs/progress/2026-05-03-penumbra-display-preset.md)
- **Penumbra per-atom brick allocator (Wave 1 / E1+E2, in-progress 2026-05-05)** — [`docs/progress/2026-05-05-penumbra-per-atom-brick-allocator.md`](docs/progress/2026-05-05-penumbra-per-atom-brick-allocator.md)
- **Penumbra Tier D1 directional Lipschitz (Wave 3 Phase 3, shipped 2026-05-05)** — [`docs/progress/2026-05-05-penumbra-d1-lipschitz.md`](docs/progress/2026-05-05-penumbra-d1-lipschitz.md)
- **Penumbra D3 tricubic + Sturm root isolation (Wave 3 Phase 6, shipped 2026-05-05)** — [`docs/progress/2026-05-05-penumbra-d3-tricubic.md`](docs/progress/2026-05-05-penumbra-d3-tricubic.md)
- **Penumbra E6 mesh-export determinism (Wave 3 Phase 5d, shipped 2026-05-05)** — [`docs/progress/2026-05-05-penumbra-e6-mesh-export.md`](docs/progress/2026-05-05-penumbra-e6-mesh-export.md)
- **Penumbra E5 Compute marchRay (Wave 3 Phase 7, merged 2026-05-07)** — [`docs/progress/2026-05-07-penumbra-e5-compute-marchray.md`](docs/progress/2026-05-07-penumbra-e5-compute-marchray.md)

New initiatives: create `docs/progress/YYYY-MM-DD-<slug>.md` per STANDARD.md §19 (feature rhythm) and add a pointer here.

### Test mode duality (`--mode` flag)
Every Canary test definition is mode-agnostic. The runtime selects how to evaluate:

```
canary run ... --mode pixel-diff   # default — visual regression vs baseline
canary run ... --mode vlm          # semantic correctness via Ollama / Claude
canary run ... --mode both         # run each checkpoint twice; report both verdicts
```

Visual regression is the unit-test-equivalent (catches code-stability deltas); VLM is the correctness oracle (catches semantic errors a baseline would silently encode). Per-checkpoint `mode: "vlm"` in the test JSON still wins over the flag.

**Capture-only mode (`mode: "capture"`).** A checkpoint with `"mode": "capture"` (aliases `"none"`/`"off"`) saves the screenshot candidate and runs **no comparison** — neither pixel-diff nor VLM — and never FAILs (status = Passed, no verdict). It **wins over the `--mode` override**, so those checkpoints stay off even under `--mode pixel-diff|vlm`. Use it to record images for manual review without a baseline or an Ollama VLM (e.g. first-time bring-up; the `kbridge-*` tests use it). Implemented in `CheckpointMode.Capture` + `ResolveEffectiveModes`/`IsCaptureOnly` in `src/Canary.Core/Orchestration/TestRunner.cs`. See [`MultiVerse/CLAUDE.md` § Testing modes](../MultiVerse/CLAUDE.md#testing-modes--vlm-vs-visual-regression) for the canonical when-to-use-which guidance. Implementation lives in `src/Canary.Harness/Cli/RunCommand.cs` (flag) + `src/Canary.Core/Orchestration/TestRunner.cs` (`ModeOverride` enum + dispatcher). Mode override resolution: per-checkpoint `mode == "vlm"` wins, otherwise `--mode` applies, otherwise pixel-diff.

### Logging — Slop test JSONs auto-tap every component output
Both modes need behavioural visibility into the canvas, not just final viewport pixels. Slop's `Log Tap` is a pass-through wiretap that records each cpig-component output as it flows downstream. Test authors should wrap every "subject of test" component output with a tap; CPig's retopo generator does this automatically (`CPig/scripts/gen_retopo_slop_tests.py` `_auto_insert_log_taps`). When debugging a failing Canary run, the per-stage tap entries in Slop's `LogHub` file are the first place to look — they show where the data flow went empty / wrong before the screenshot capture. See [`MultiVerse/CLAUDE.md` § Logging in Slop tests](../MultiVerse/CLAUDE.md#logging-in-slop-tests--every-components-behaviour-every-run).

### Running Penumbra tests
A single `canary run --workload penumbra [--suite s] --headless` invocation already shares **one** Vite dev server + Chrome across all its tests (`RunCommand.RunPenumbraSuiteAsync` — there is no per-test respawn within an invocation; only separate invocations spawn fresh). But every test still does a **full page reload**: each test's `setup.backend` dispatches `SetBackend` → `Page.navigate`, which re-runs Penumbra's WebGPU adapter + Dawn pipeline init (30–90s on this hardware). That reload is governed by `penumbraConfig.pageLoadTimeoutMs` in `workloads/penumbra/workload.json` (default 180s) and retried once on load-timeout by `PenumbraBridgeAgent.NavigateWithRetryAsync` — see BUG-0011 (`docs/bugs/0011-penumbra-page-load-timeout-sweep-crashes.md`) for why the old hard-coded 60s ceiling crashed ~35% of a 90-test sweep. Per-test `canary run` loops need no external node-kill/sleep workarounds: `ViteManager` verifies port release before spawning and `ChromeLauncher` clears stale CDP-port listeners + GCs leaked `%TEMP%\canary-chrome-*` profiles.

**Note on the C2 event gate** (`eventDrivenRender` ON in the shipping default profile since 2026-05-08): the render loop idles unless something marks dirty. **Resolved upstream 2026-06-12 (penumbra ask 0002)**: `__canaryRunComputeMarcherSmoke` now supplies its own render demand per tick, and the agent's `WaitForStable` calls Penumbra's `__canaryWaitForPresentedFrame` (frame actually rendered at full res after the latest change) before every capture — the 13 test-side `eventDrivenRender:'off'` workaround lines were removed (BUG-0013 closeout). If you author a NEW hook that polls per-frame counters, have it mark dirty itself (`deps.forceMarkDirty`) rather than adding test-side workarounds.

### Running CPig Tests
Always use `--suite cpig` to run CPig tests, not individual `--test` invocations. All CPig tests declare `runMode: shared`, which means Canary launches Rhino **once** and runs all tests sequentially in that single instance. Running tests individually with `--test` defeats this — each invocation opens and closes Rhino separately. The suite approach is faster and matches the intended workflow.

**`runMode: shared` is the DEFAULT for ALL tests (2026-06-16, `TestDefinition.RunMode`).** Every suite chains in ONE app instance unless a test explicitly sets `"runMode": "fresh"`. Set `fresh` ONLY for a test that holds process-global state with no in-session reset (today: just the Qualia breadcrumb nav). The single-launch shared path engages only when ALL of a suite's tests are shared — **one `fresh` test forces the whole suite to per-test launches** (`RunCommand.cs` dispatch). Each shared test MUST begin its `actions` with a cleanup pulse (Build off → Cleanup on → Cleanup off) since the Rhino doc + GH state persist between tests. (The Penumbra GLSL tests are now shared too: `WaitForPenumbraFrame` was made RELATIVE — snapshot-at-start, wait-for-increase — so the process-global frame-ready revision no longer causes stale captures when chained.)

```bash
# Correct — single Rhino instance, all tests sequential
cd C:\Repos\Canary
canary run --workload rhino --suite cpig

# Wrong — opens/closes Rhino for each test
canary run --workload rhino --test cpig-19-noise-field
canary run --workload rhino --test cpig-20-domain-modifiers
```

### Running Pigture Tests
Same shared-suite pattern as CPig: `canary run --workload rhino --suite pigture`.

Pigture checkpoints use `"source": "file"` instead of `"source": "viewport"` (the default). Viewport screenshots capture Rhino's Shaded display mode, not the Cycles render. The rendered image is saved to disk by RenderViewer, and its path flows through a `RenderFilePath` GH panel. At checkpoint time, the runner reads that panel via `GrasshopperGetPanelText`, copies the file to `candidates/`, and runs normal pixel-diff comparison.

Key `TestCheckpoint` fields for file-source:
- `"source": "file"` — use file instead of viewport capture
- `"panelNickname": "RenderFilePath"` — which GH panel holds the file path

See `spec/PIGTURE_WORKLOAD.md` for the full pattern.

### Running Slop tests
`canary run --workload rhino --suite slop` runs the Slop-native tests (`slop-01-graft-grid`, `slop-02-within-radius`) — pure core-Grasshopper + Slop components that validate the Slop `fodder/kb` authoring conventions produce buildable, correct definitions (graft cross-product, within-radius count). They reuse the `cpig_slop_loader.gh` fixture. Note: Slop's JSON schema is now drift-guarded by a tripwire (CI + every Slop `dotnet build`), so the `SLOP_PROMPT.md` contract these tests rely on stays in sync with the runtime `.gha`. When authoring new Slop JSON, see `Slop/fodder/kb/` + `Slop/fodder/tools/lookup_component.py` (now prints port `[access]`).

### Running KinematicBridge tests
`canary run --workload rhino --suite kbridge` runs the KinematicBridge operator-visual trust-check tests (`kbridge-four-bar-importer`, `kbridge-four-bar-cpig`, `kbridge-four-bar-overlay`, `kbridge-slider-crank-overlay`, `kbridge-slider-crank-sweep`, `kbridge-slider-crank-animate`, `kbridge-four-bar-animate`, `kbridge-oldham-animate`, `kbridge-spherical-four-bar-animate`, `kbridge-slider-crank-differential`, `kbridge-geneva-animate`, `kbridge-pin-gear-animate`, plus the 2026-06-11 coverage-campaign showcase trio `kbridge-geneva-20-animate`, `kbridge-ratchet-31-animate`, `kbridge-ratchet-22-animate` — three newly-cleaned corpus mechanisms emblematic of the F1/F2/F3/B-bug-014 fixes; CPig-only, doc=MM, emitted drivers, pass/fail = `Converged` + `SlopSuccess`). The four-bar trio is the Step-0 mm-isolation check for the Inventor↔CPig bridge: Run A renders the Inventor four-bar via **KinematicImporter** alone, Run B renders the bridge-emitted `.kin.json` skeleton via **CPig.Kinematics** alone, Run C overlays both (the unit-sensitive scale/frame check).

`kbridge-slider-crank-sweep` is different — a **behavioral regression guard**, not a capture-judged overlay. It is CPig-only (`Read Mechanism → Sweep(Live) → Pose at Step → Preview Diagram`) and proves the bridge's prismatic anti-roll fix (KinematicBridge B-bug-008): the slider-crank now assembles and sweeps where it previously couldn't converge. Its real pass/fail is the `PanelEquals Converged = True` assert (wired from the `Sweep` node's `Converged` output), so it FAILs loudly if the regression returns — even though its screenshots are still capture-only. The `Sweep` node overrides the range to `0 → 2.967 rad` (≈170°, in canonical radians) to stay short of the 180° dead-center singularity where CPig's solver still stagnates (B-cand-008). It needs **no** KinematicImporter `.gha` (CPig-only). They reuse the `cpig_slop_loader.gh` fixture and `runMode: shared`, but the Slop graphs live in the **bridge** repo (`C:/Repos/KinematicBridge/out/_slop-side-by-side/*.slop.json`), referenced by absolute path in each test's `JsonPath` action.

Two environment dependencies, unusual vs the CPig suite:
- **KinematicImporter `.gha` must be loaded** in the Rhino instance (Runs A + C). If it isn't, the Slop build fails and `SlopSuccess` asserts to `False` (a correct, loud failure).
- **Doc units must be centimeters.** The KinematicImporter is **cm-native** (it scales transforms but not its imported STEP geometry, so it only renders correctly at cm), and CPig's `PreviewDiagram` is now **unit-aware** (CPig ADR 0005) so it matches at cm. Each test sets `setup.commands` = `-_DocumentProperties _Units _ModelUnits _UnitSystem _Centimeters _EnterEnd` (tested working 2026-06-09). The `_EnterEnd` token backs out of the entire nested DocumentProperties command — including the conditional "scale by 0.1?" Yes/No — to a blank `Command:` prompt, so it does NOT depend on counting Enters or on whether the scale prompt appears. **Gotcha for any Rhino `-DocumentProperties` units macro:** you MUST include `_UnitSystem` before the unit name, and prefer `_EnterEnd` over hand-counted `_Enter`s — omitting either hangs the command line and blocks the whole test behind it (the agent pipe then disconnects). Full reference for authoring `setup.commands` Rhino macros: [`docs/features/rhino-setup-commands-macros.md`](docs/features/rhino-setup-commands-macros.md). If cm still doesn't take, set it once manually in the shared Rhino instance. Rebuild CPig + restart Rhino first so the unit-aware `.gha` is loaded.

No baselines exist yet, so run with `--mode vlm` (semantic check against each test's `vlmDescription`) or `--mode pixel-diff` to just capture candidate screenshots for manual review. Success criterion for this phase is **scale parity** in the overlay (no ~10× mismatch); position offset and the solid-vs-skeleton representation are deferred.

### Running Penumbra-in-Rhino tests (live preview — DEPRECATED out-of-process fallback)
`canary run --workload rhino --suite penumbra` drives the **legacy out-of-process** Penumbra Rhino overlay (not the web `penumbra` workload — that's CDP/Vite). It runs `PenumbraOopShow` (formerly `PenumbraShow`) via the `RunCommand` action so the plug-in renders into the Rhino viewport, then gates the screenshot on a real frame. **Superseded by the in-process GLSL studio (`PenumbraGl` — no rhino suite yet); the `penumbra` suite is kept ONLY because `penumbra-rhino-01-gyroid` still uniquely covers the CPig→Penumbra live-preview chain until the GLSL path's CPig integration lands.**

- **New agent action `WaitForPenumbraFrame`** (`Canary.Agent.Rhino`): blocks until the Penumbra overlay has presented a frame showing the REAL field, not the bounding-sphere **companion stand-in** the atlas path shows while baking. Params: `timeoutMs` (180000), `minRevision` (1), `requireReal` (true). Implemented peer-agnostically via **reflection** into `Penumbra.Bridge.PenumbraBridge.GetFrameState()` (no compile-time dependency on the Penumbra plug-in); returns failure (not a throw) when the Bridge isn't loaded. Use it after `RunCommand PenumbraShow` and after any field push, before a capture — Rhino's `CaptureToBitmap` includes the conduit foreground, so the overlay is captured.
- **Environment dependency:** the **Penumbra Rhino plug-in must be registered for auto-load** in the Canary Rhino instance (drag the `.rhp` in once with "always load"), and `PENUMBRA_HOST_DEV` must point at a `packages/host-node` checkout (dev host). First `PenumbraShow` on a cold machine eats a one-time ~351s shader compile (then cached at `%LocalAppData%\Penumbra\pipeline-cache`). Mirrors how `kbridge` requires KinematicImporter + cm units.
- **Plan + gates:** `Penumbra/docs/plans/2026-06-13-rhino-preview-autonomous-hardening.md` (▣0 smoke = demo; ▣1 gyroid lattice; ▣2 camera axis; ▣3 units; …). v1 tests use `mode: "capture"` until baselines are approved.

#### Removed Penumbra rhino suites (2026-06-16)
The `penumbra-studio` suite (`penumbra-rhino-studio-{sphere,gyroid}`) and the `penumbra-glspike` suite (`penumbra-glspike-00-multi`) were **DELETED**: the former drove the out-of-process sample-field inserters `_PenumbraSphere`/`_PenumbraGyroid`, which were REMOVED in the Penumbra 2026-06-16 command cleanup; the latter drove the throwaway `Penumbra.GlSpike` plugin's `PenumbraGlMulti` (renamed `PenumbraDebugMulti`), whose native-GLSL ray-march **graduated** into the in-process GLSL studio (`PenumbraGl`, in the real `Penumbra.Rhino` plugin). The GLSL studio's replacement suite is **`penumbra-glsl`** (below); the deprecated OOP `penumbra` fallback above survives only until that reaches parity.

#### Penumbra in-process GLSL studio (`penumbra-glsl` suite, 2026-06-16)
`penumbra-glsl-00-sphere` + `penumbra-glsl-01-cpig-gyroid` drive CPig live preview through the **in-process GLSL conduit** (Swath C): the Slop loader builds the field, `RunCommand _PenumbraPreviewBackend _Glsl` sets the backend, then Build → CPig's `.3mf` push routes to a **device-free node compile** (`compileThreeMFToGlArtifact`) and renders by ray-marching into Rhino's GL context. **Capture is `source: viewport`** (NOT file-source): `CaptureToBitmap` includes the conduit's `PostDrawObjects` GL draw (the Swath-4 lesson — only `DrawForeground`-blit overlays are uncapturable). Gated by `WaitForPenumbraFrame` (now wired for the GLSL conduit: `PenumbraBridge.FrameStateProvider` reports the GLSL conduit when it's enabled, else the OOP one). **Runs SHARED — chains in ONE Rhino** (`runMode: shared`, now the DEFAULT for all tests): `WaitForPenumbraFrame` is now RELATIVE (snapshots the revision at action start, waits for it to INCREASE), so the process-global GLSL frame-ready revision no longer causes stale-frame captures across chained tests. Requires the Penumbra Rhino plug-in auto-loaded + the CPig `.gha` (Penumbra Preview). **Two tests + shape-discrimination pair (2026-06-16):** `penumbra-glsl-00-sphere` (Field Primitive sphere → STRUCTURAL tape path; must render a smooth sphere) + `penumbra-glsl-01-cpig-gyroid` (unified Field TPMS gyroid∩sphere → DENSE voxel-bake → image3d→grid decode, Penumbra bug 0054; must render a gyroid LATTICE, not a sphere — dense fields now render, no more `gl.compile3mf` sphere stub). **Preview discipline (2026-06-16):** the Slop fixtures (`53_penumbra_preview.json`, `54_penumbra_sphere.json`) set `"preview": false` on every field-producing node, so ONLY the Penumbra render appears in the viewport — a visible CPig Field* preview (bbox+iso-mesh) clutters/occludes the SDF and masks whether Penumbra rendered. **Asserts:** deterministic panel asserts (SlopSuccess / Connected / Status=Pushed / no FATAL — prove the CPig→Penumbra push regardless of framing). **Visual:** `mode: capture` — saves the candidate for manual review (no image pass/fail). VLM regression is **wired but dormant**: each test carries `setup.vlm = {provider: ollama, model: qwen2.5vl:7b}` (local vision model, no API key — the rhino workload sets no VLM default, and `gemma4` is NOT vision-capable) + a discriminating `vlmDescription`; re-enable by flipping the checkpoint `mode` back to `vlm` (validated 2026-06-16: qwen2.5vl:7b correctly FAILed a sphere render against the gyroid description). **Run:** `canary run --workload rhino --suite penumbra-glsl`. **Diagnostic:** empty viewport ⇒ restart Rhino (new `.rhp` + a fresh device-free node host with the A1 dist) — the pushed-scene auto-fit (A2) landed 2026-06-16, so a framed render should appear; the sphere (tape path) vs gyroid (dense-grid path) tests isolate which path is at fault.

#### CPig field-ops (`cpig-fieldops` suite, 2026-06-17)
`cpig-fieldops-00-create-copy-delete` exercises CPig **Batch A per-field OPERATIONS** through the in-process GLSL conduit, driven by CPig.Rhino **commands** (NOT a GH graph — distinct from `penumbra-glsl`, which drives the GH `CPig_PenumbraPreview` component). Action sequence (all `RunCommand` macros via `RhinoApp.RunScript`): `_SelAll _Delete` (doc hygiene) → `_CPigSphere 0,0,0 10` (create + auto-display; CPigDisplay sets `Backend=Glsl` itself, so no `_PenumbraPreviewBackend`) → **native** `_SelLast _Copy 0,0,0 50,0,0 _Enter` (the operator's gesture — `CPigFieldWatcher`'s idle reconcile **adopts** the degraded duplicate into an independent 2nd field) → `_SelLast _Delete`. **The deterministic regression guard is `WaitForPenumbraFrame` (requireReal, RELATIVE) after EACH op** — create/copy/delete must each present a NEW real GLSL frame, else the action times out → test Crashed. That is what would have caught BUG-0017 ("copy made a box mesh + delete did nothing" = native copy not adopting / delete not re-displaying = no new frame). `WaitForPenumbraFrame` also pumps `RhinoApp.Idle` while polling, which is what lets the idle-scheduled copy/delete reconcile run. **Macro gotchas:** native `_Copy` is a multi-copy loop — the trailing `_Enter` is REQUIRED or Rhino parks at "Point to copy to" → "Pipe disconnected" crash; copy/delete reconcile on idle (not inline), so the `WaitForPenumbraFrame` between ops is load-bearing, not cosmetic. **Visual:** one viewport capture (post-delete, one sphere), `mode: capture` (no baseline yet) + a dormant VLM `vlmDescription`. **Telemetry** (`cpig.copy:native-adopt` / `cpig.delete` / `cpig.reconcile` / `gl.scene.loaded` atoms) is side-channel diagnosis only — NOT test-assertable until Tier-2 (task #43) or a CPig GH panel exposes the live field count. The `cpig_slop_loader.gh` fixture is used ONLY to launch Rhino cleanly (GH graph stays idle; Build never toggled on). **Run:** `canary run --workload rhino --suite cpig-fieldops`. Requires Penumbra.Rhino + CPig.Rhino registered for auto-load.

#### CPig display matrix (`cpig-display-matrix` suite, R1.3 2026-07-03)
The D7 rep-matrix: 24 GENERATED tests `cpig-repmatrix-<type>-<rep>` — (sphere|box|gyroid|mesh) × (auto|tape|atlasBaked|mesh|pointCloud|companionTape) — plus the D1 dense-rep guard + the 4 scriptable boolean cells (29 total, all `runMode: shared`, ONE Rhino). **Author via the generator, not by hand:** `python scripts/gen_rep_matrix_tests.py` rewrites all 24 + the suite JSON idempotently (edit the generator, re-run). The `procedural` rep column is pruned (field-independent fbm debug rep; dispatch locked once by `cpig-fieldops-04`); degrade-by-design cells are KEPT (dense mesh-field × tape correctly renders the companion bounding-sphere stand-in — that fallback IS the behavior to lock). Rep targeting: `_CPigDisplayRep` has NO direct-set arg — fresh fields start at `auto`, so each test selects the field and cycles k times (`auto→tape→atlasBaked→procedural→mesh→pointCloud→companionTape`). Camera: the cpig-booleans decoy-sphere solo-perspective recipe, cloned verbatim. Every test gates its capture on `requireSteady` (steady + bakes drained). **Baselines APPROVED 2026-07-03 (STOP-POINT R1.3 signed):** 23 tests blessed (46 baselines; archived to `G:\My Drive\builds\Canary\baselines-archive\2026-07-03-cpig-display-matrix\` per the audit-c pre-migration rule — re-archive before any future engine change). **19 live pixel-diff gates** (14 matrix + rep-dense + 4 booleans) at **tolerance 0.005** — the original 0.05 was proven decorative (a sphere r=10→12 break passed at 1.8%); 0.005 makes the same break FAIL and greens still diff at 0.0%. **10 cells HELD capture-mode** (regenerate with `--hold "mesh,cpig-repmatrix-sphere-companiontape,cpig-repmatrix-box-companiontape,cpig-repmatrix-gyroid-companiontape,cpig-repmatrix-gyroid-atlasbaked"`): the 6 mesh-row cells (Penumbra 0059 — dense rep switches visually inert) + 4 companion/atlas cells (Penumbra 0061 — those reps render nondeterministically run-to-run, 3.75–7% deltas at steady+bakes-drained). Un-hold + re-approve when those engine bugs close. Gate-can-fail PROVEN: break-one-render → FAIL 1.8% → revert → 29/29 green. Known intermittent: Penumbra 0060 (BakesOutstanding wedges at 1 under rep-switch re-push storms → requireSteady timeout crash, ~1 seen in 4 suite runs — the gate being correctly loud; do NOT weaken it). Authoring gotcha baked into the generator: re-select (`_SelAll`) before EVERY `_CPigDisplayRep` — each re-push fires `gl.load.post.deselect` and an unselected invocation silently falls into the SCENE-GLOBAL rep cycle (different cycle!), landing the rep one step short. **F11 (R1.5, 2026-07-03):** the two difference cells (`cpig-booleans-02/05`) joined the suite (31 tests) AND `cpig-booleans-scripted` (6 tests) — `-_CPigDifference` (hyphen = RunMode.Scripted) reads the preselection, earliest-created kept. **Camera-recipe race-proofing (same date):** the decoy is selected BY TYPE (`_SelSrf`, the only NURBS surface in these docs) immediately before Zoom AND before Delete — a CPig async re-push's load-side deselect was observed killing `_SelLast` selection between actions (Zoom→Nothing, Delete→Nothing, decoy survived into a 30%-diff capture). The recipe change is pixel-invariant (pre-change baselines still diff 0.0%). **Run:** `canary run --workload rhino --suite cpig-display-matrix`.

### Before Any Work
Read `spec/SUPERVISOR.md` — single source of truth for build decisions.

### Spec Files (read in order)
1. `spec/SUPERVISOR.md` — Orchestration, constraints, gate checklists, dependency matrix
2. `spec/ARCHITECTURE.md` — System design, IPC protocol, comparison engine, two-process model
3. `spec/PHASES.md` — Build phases with checkpoints (0–7)
4. `spec/PHASES_UI.md` — Build phases with checkpoints (8–13: Core extraction + GUI + CPig workload). UI was migrated to Avalonia 11 + FluentAvalonia + CommunityToolkit.Mvvm 2026-05-27 — see [`docs/features/canary-ui-avalonia.md`](docs/features/canary-ui-avalonia.md).
5. `spec/TESTS.md` — Unit and integration test specifications (0–7)
6. `spec/TESTS_UI.md` — Test specifications (8–12)
7. `spec/CPIG_WORKLOAD.md` — Conventions for the CPig regression workload (Phase 13). Peer doc: `C:\Repos\CPig\spec\CANARY.md`.
8. `spec/PIGTURE_WORKLOAD.md` — Conventions for the Pigture render workload. Peer doc: `C:\Repos\Pigture\spec\PEERS.md`.

### Key Rules
- **Namespace**: `Canary` (core + harness), `Canary.Agent` (shared), `Canary.Agent.*` (per-app)
- **Framework**: `net8.0-windows` (Core, Harness, UI), `net8.0;net48` (Agent), `net48` (Rhino). UI is **Avalonia 11.2 + FluentAvaloniaUI 2.2 + CommunityToolkit.Mvvm 8.3** (since 2026-05-27 cutover — see `docs/features/canary-ui-avalonia.md`).
- **IPC**: Named pipes + JSON-RPC only — no HTTP, no sockets
- **Screenshots**: Captured by agent inside the app, not by the harness
- **Ctrl+C**: Must always work. Display "Press Ctrl+C to abort" in status output
- **Tests**: `[Trait("Category", "Unit")]` headless, `[Trait("Category", "Integration")]` needs app

### Documentation Structure
```
docs/
  bugs/           # One .md per bug (frontmatter for status/severity)
  debug-sessions/ # Investigation journals
  decisions/      # Architecture Decision Records (MADR format)
  features/       # Feature status tracking
  feedback/       # Operator-authored feedback inbox (see below)
  research/       # Deep-dive research reports (techniques, literature, trade-offs)
  templates/      # Reusable templates for all doc types
CHANGELOG.md      # Keep a Changelog format
BUILD_LOG.md      # Phase checkpoint records
```

### Feedback inbox

If `docs/feedback/inbox/` is non-empty at session start, list new items
before proceeding with other work. Each item is a `<slug>.md` + sidecar
`<slug>/` (source.png, annotated.png, annotations.json) authored by the
operator via the Canary UI's Annotate button (Phase 5 / §C5). See
`docs/feedback/README.md` for the layout, item shape, and lifecycle
(open → triaged → resolved). Phase 6's MCP server exposes
`list_feedback` / `get_feedback` / `mark_feedback_triaged` tools when
configured.

### Cross-Repo Change Protocol

**This rule is mandatory.** When your session's changes affect other repos (new features they consume, contract changes, schema changes, corrected documentation):

1. **Update `CLAUDE.md` in every affected repo.** This is the #1 priority — CLAUDE.md is what the next Claude Code session reads first. Add or revise the section describing the capability/contract. If a new pattern was established (like file-source checkpoints), document it where future sessions will find it.

2. **Update `spec/PEERS.md`** in every affected repo that has one. Keep contract descriptions, input/output mappings, and GUID tables current.

3. **Log to MultiVerse.** Append a one-line entry to `C:\Repos\MultiVerse\BUILD_LOG.md`:
   ```
   YYYY-MM-DD | cross-repo | Canary → AffectedRepos | one-line summary
   ```

**What triggers this:** Any change that would leave another repo's CLAUDE.md or PEERS.md stale. Adding a `TestCheckpoint` field → update Pigture/CPig CLAUDE.md. Adding an agent action → update repos whose tests use it. Changing test conventions → update Slop CLAUDE.md if it affects JSON authoring.

### Auto-Journaling Rules

**These rules are mandatory.** When working in this repo, maintain living documentation:

1. **After fixing a bug**: Create `docs/bugs/NNNN-short-name.md` using the template. Include frontmatter with `status`, `severity`, `project`, `component`. Add a `### Fixed` entry to `CHANGELOG.md` under `[Unreleased]`.

2. **After completing a feature or significant change**: Add entry to `CHANGELOG.md` under the appropriate category (`Added`, `Changed`, `Removed`, `Fixed`). Update `docs/features/FEATURE_STATUS.md` if a feature's status changed.

3. **After a debug investigation** (whether or not it leads to a fix): Create `docs/debug-sessions/YYYY-MM-DD-short-name.md` using the template. Document hypothesis, evidence, conclusion.

4. **After a significant architectural decision**: Create `docs/decisions/NNNN-short-name.md` using the MADR template. Reference the relevant spec section.

5. **After a research deep-dive** (literature review, technique survey, performance analysis, or architecture exploration): Create `docs/research/YYYY-MM-DD-short-name.md` using the research template. Document the question, sources consulted, findings, and actionable conclusions. Link to any resulting decisions or bugs.

6. **After a build/test run**: Append to `BUILD_LOG.md` following existing format (date, status, tests run/passed/failed, issues, resolution).

7. **Frontmatter schema** (use consistently across all docs):
   ```yaml
   date: YYYY-MM-DD          # ISO 8601
   tags: [bug, feature, ...]  # From: bug, feature, decision, debug-session, research, penumbra, canary, rhino
   status: open | in-progress | resolved | accepted | deprecated
   project: canary | penumbra  # Which project this relates to
   severity: critical | high | medium | low  # Bugs only
   component: "..."           # Subsystem (e.g., atlas, cdp, tape-compiler, comparison)
   ```

### Key dependencies (per [`MultiVerse/STANDARD.md`](../MultiVerse/STANDARD.md) §21)

| Package | Version | Notes |
|---|---|---|
| .NET | 8.0 (net8.0-windows) | Canary.UI requires Windows; Canary.Harness + Canary.Core are net8.0 cross-platform-capable |
| xUnit / Microsoft.NET.Test.Sdk | latest test SDK | unit + integration tests in `src/*.Tests/` |
| Microsoft.Playwright | (CDP driver) | drives Penumbra Vite dev server for visual regression |
| SixLabors.ImageSharp | (pixel-diff backend) | baseline-vs-candidate comparison |
| Ollama HTTP client (via `OllamaVlmProvider`) | gemma4:e4b or qwen2.5vl:7b | local VLM provider |

### Release type (per [`MultiVerse/STANDARD.md`](../MultiVerse/STANDARD.md) §21)

This repo is **infrastructure** — no formal release; milestone tags only (e.g. `canary-v1`). BUILD_LOG entries under the `milestone` category for notable progress.

### How to reproduce bugs in this repo (per [`MultiVerse/STANDARD.md`](../MultiVerse/STANDARD.md) §15)

- **Test runner:** `dotnet test C:/Repos/Canary/Canary.sln --filter "<TestName>"`. For the Canary GUI flow: kill any running Canary.UI.exe, build Release, launch the built exe directly (not `dotnet run` — it backgrounds wrong). Pattern:
  ```bash
  taskkill //IM Canary.UI.exe //F
  cd C:/Repos/Canary && dotnet build Canary.sln --configuration Release
  start "" "src/Canary.UI.Avalonia/bin/Release/net8.0-windows/Canary.UI.exe"
  ```
- **Repro harness:** workload-scoped — `workloads/rhino/`, `workloads/penumbra/`, `workloads/qualia/`. Test definitions at `workloads/<w>/tests/*.json`, suites at `workloads/<w>/suites/*.json`, fixtures at `workloads/<w>/fixtures/*.gh` (Rhino) or programmatic (Penumbra/Qualia). Run a single test: `canary run --workload rhino --test cpig-NN-slug --mode pixel-diff|vlm|both`.
- **Environment:** Rhino 8 installed and licensed for Rhino-workload tests; a WebGPU-capable GPU + Vite dev server reachable for Penumbra workload; Ollama running locally (`ollama serve`) with the configured VLM model pulled.
- **Known gotchas:** `taskkill` in Git Bash needs `//IM` (double slash); do NOT use `dotnet run` for the UI (background mode fails); workload `projectDir` paths are absolute Windows paths — match the local machine layout.

### Skills available

See [`MultiVerse/SKILLS.md`](../MultiVerse/SKILLS.md) for the canonical catalog. The `multiverse-supervisor` skill enforces [`MultiVerse/SUPERVISOR.md`](../MultiVerse/SUPERVISOR.md) at session start for any non-Conversation work. **Discipline 7 (ground before you assert / anti-roll-past):** before any consequential, multifactorial, or state-asserting answer or plan, verify each claim at the running source (T0 — the code path that executes, the flag's default value), not a doc status field or a subagent summary (T2); "exists / compiles / shipped" (≤T1) is never "is the live default" (T0); a contradiction is stop-and-resolve. **Newest — Discipline 8 (trust through verification):** delegated research/plan output (subagents, Workflows) is not trusted on schema-validation alone — producers don't self-certify, junk submissions loud-fail + retry, load-bearing claims are source-verified by a non-producer, and the flake rate is reported. See `MultiVerse/RESEARCH-PLAN-CHECKLIST.md`.

### Commit Messages
Use conventional commits: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`.
