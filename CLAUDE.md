# CLAUDE.md

## Project: Canary — Cross-Application Visual Regression Testing Harness

### Quick Reference
- **Build**: `dotnet build Canary.sln` (must be 0 errors, 0 warnings)
- **Test**: `dotnet test tests/Canary.Tests/Canary.Tests.csproj --filter "Category=Unit"`
- **UI-first runs (canonical, `MultiVerse/STANDARD.md` §16 locked rule 8)**: every operator-triggered `canary run` launches with `Canary.UI.exe` visible. Implemented in Phase 1 of the debug-overhaul (shipped 2026-05-24). `--headless` bypasses for CI; `--quiet` implies `--headless`. Second `canary run` while UI is up forwards args to the existing instance via named pipe.
- **When the operator says "run canary" (verbal/chat shorthand): do NOT use `--headless`.** The operator means the UI-visible default path: `canary run --workload <w> [--test <t> | --suite <s>]` (NO `--headless` flag) which spawns `Canary.UI.exe` and runs the test there. The UI stays open after the run completes today (UI auto-close-on-done is not wired — `AutoRunArgs` has no `AutoExit` flag in `Canary.Core/Cli/AutoRunArgs.cs`). Only use `--headless` when the operator explicitly asks for headless / CI / scripted use. **You (the agent) may still prefer `--headless` for your own end-to-end verification** because the UI launch flakes from agent sessions per the `canary_launch_from_session` auto-memory — but that's an agent-internal tooling choice, not what the operator means by "run canary".
- **Run Penumbra tests**: `canary run --workload penumbra`
- **Run CPig tests**: `canary run --workload rhino --suite cpig` (from `C:\Repos\Canary`)
- **Supervised session (shipped 2026-05-27 for Qualia + Penumbra; Rhino added 2026-06-02)** — `canary session start --workload {qualia|penumbra|rhino}` boots the target app under supervision (visible Chrome / Rhino, no automated tests) and enters a capture REPL (`c` / `a` / `n` / `q`); or click the **Sessions** nav tab in `Canary.UI` for the GUI flow with Ctrl+Shift+C / Ctrl+Shift+A hotkeys. Sessions live at `workloads/<w>/sessions/<yyyyMMdd-HHmmss-xxxx>/` with `SESSION_REPORT.md` + `session.json` + `telemetry.ndjson` + `captures/`. Rhino v1 has no telemetry source yet (no equivalent to CDP Console/Log/Network wired — see `docs/features/rhino-session.md` "Deferred to v2"). See [`docs/features/supervised-session.md`](docs/features/supervised-session.md) + [`docs/features/rhino-session.md`](docs/features/rhino-session.md). MCP: `list_sessions` / `get_session_report` (bringing the total to 10).
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

### Running Penumbra-in-Rhino tests (live preview, NEW 2026-06-13)
`canary run --workload rhino --suite penumbra` drives the **Penumbra Rhino plug-in's viewport overlay** (not the web `penumbra` workload — that's CDP/Vite). It runs `PenumbraShow` via the `RunCommand` action so the plug-in renders into the Rhino viewport, then gates the screenshot on a real frame.

- **New agent action `WaitForPenumbraFrame`** (`Canary.Agent.Rhino`): blocks until the Penumbra overlay has presented a frame showing the REAL field, not the bounding-sphere **companion stand-in** the atlas path shows while baking. Params: `timeoutMs` (180000), `minRevision` (1), `requireReal` (true). Implemented peer-agnostically via **reflection** into `Penumbra.Bridge.PenumbraBridge.GetFrameState()` (no compile-time dependency on the Penumbra plug-in); returns failure (not a throw) when the Bridge isn't loaded. Use it after `RunCommand PenumbraShow` and after any field push, before a capture — Rhino's `CaptureToBitmap` includes the conduit foreground, so the overlay is captured.
- **Environment dependency:** the **Penumbra Rhino plug-in must be registered for auto-load** in the Canary Rhino instance (drag the `.rhp` in once with "always load"), and `PENUMBRA_HOST_DEV` must point at a `packages/host-node` checkout (dev host). First `PenumbraShow` on a cold machine eats a one-time ~351s shader compile (then cached at `%LocalAppData%\Penumbra\pipeline-cache`). Mirrors how `kbridge` requires KinematicImporter + cm units.
- **Plan + gates:** `Penumbra/docs/plans/2026-06-13-rhino-preview-autonomous-hardening.md` (▣0 smoke = demo; ▣1 gyroid lattice; ▣2 camera axis; ▣3 units; …). v1 tests use `mode: "capture"` until baselines are approved.

#### Penumbra Studio debug loop (`penumbra-studio` suite, NEW 2026-06-15)
`penumbra-rhino-studio-{sphere,gyroid}` drive the **standalone** Penumbra Rhino Studio commands (no Grasshopper/Slop/CPig): `RunCommand _PenumbraSphere`/`_PenumbraGyroid` → `WaitForPenumbraFrame(real)` → file-source `active.png` capture. They're the deterministic in-Rhino reproduction loop for studio-command debugging (it already root-caused + verified the fix for a blank-gyroid auto-fit-viewport bug). **Run them ISOLATED, not as a `--suite`** (`canary run --workload rhino --test penumbra-rhino-studio-gyroid --headless`): in a shared Rhino the cumulative `RealRevision` makes the 2nd test's `WaitForPenumbraFrame(minRevision:1)` return instantly and capture the *previous* test's frame. Debug-loop footguns (learned the hard way): `canary.exe` resolves `workloads/` **relative to CWD** → always run from `C:\Repos\Canary`; **delete `%LocalAppData%\Penumbra\preview-frames\active.png` before each run** (it's a shared file — a timeout silently captures the stale prior frame); `mode:"capture"` never FAILs → **evaluate the candidate PNG + `%LocalAppData%\Penumbra\preview\telemetry.ndjson` yourself**, don't trust "PASS". The Penumbra-side `telemetry.ndjson` (`insert.*`/`fit.auto`/`frame.real` with eye+viewport/`render.error`) is the primary debug signal; surfacing it into REPORT.md (a `telemetryPath` field) is the queued Tier-2 enhancement.

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

See [`MultiVerse/SKILLS.md`](../MultiVerse/SKILLS.md) for the canonical catalog. The `multiverse-supervisor` skill enforces [`MultiVerse/SUPERVISOR.md`](../MultiVerse/SUPERVISOR.md) at session start for any non-Conversation work.

### Commit Messages
Use conventional commits: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`.
