---
title: "Changelog"
tags:
  - changelog
---

# Changelog

All notable changes to Canary are documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added — Debug-overhaul Phase 1 (C3 non-headless enforcement, 2026-05-24)
- CLI now launches `Canary.UI.exe` by default for `canary run` invocations and exits 0 — implements `STANDARD.md` §16 locked rule 8. `--headless` flag bypasses for CI / scripted use (and `--quiet` implies `--headless`). UI is located via search order: same directory as `canary.exe` → sibling solution layout (`Canary.UI/bin/{Release|Debug}/net8.0-windows/Canary.UI.exe`) → `Canary UI.lnk` shortcut. If no UI exe found, falls through to today's text-only path with a one-line warning.
- UI single-instance behavior: a second `canary run` (or direct UI launch) detects the first via a `Global\Canary.UI.SingleInstance` mutex and forwards its auto-run args via a named pipe (`canary-ui-singleinstance-pipe`) to the running instance, which dispatches them through the same code path as a tree-click → Run Tests button press. Operator decision Q5.
- `Canary.Cli.AutoRunArgs` POCO (in `Canary.Core`) carries the auto-run-relevant subset of CLI args (`--workload`, `--test`, `--suite`, `--mode`) with JSON + argv round-trips. Used by both the CLI-handoff path and the pipe-forwarded path.
- `Canary.UiLocator` helper (in `Canary.Harness`) — file/COM-based search for `Canary.UI.exe` with the search order above.
- `Canary.UI.SingleInstancePipeServer` + `SingleInstancePipeClient` — async one-shot named-pipe server + fire-and-forget client; the server raises `AutoRunRequested` on the pipe-loop thread which `Program.cs` marshals to the UI thread.
- `MainForm.AutoRunAsync(AutoRunArgs)` — workload-tree-aware auto-run driver. Polls up to 10s for the workloads tree to populate (handles the constructor's fire-and-forget `AutoDetectWorkloadsDir`), selects the matching workload / test / suite node, sets the one-shot `_autoRunModeOverride`, then triggers the existing `OnRunTests` handler.
- `TestRunnerPanel.RunAsync` gains an optional `modeOverride` parameter (default `PixelDiff`) — wires the mode through to `TestRunner.ModeOverride`. The corresponding UI mode picker arrives in Phase 7.
- 13 new unit tests (`AutoRunArgsTests` × 10 round-trip / parse / IsEmpty cases; `HeadlessFlagTests` × 3 flag-accepted / help-mentions / UI-locator-contract).
- 2 new integration tests (`SingleInstancePipeTests` — full pipe round-trip; client-without-server returns quickly without throwing).

### Fixed — Debug-overhaul implementation (2026-05-24)
- CLI exit code: `canary run` now returns `1` when any test fails or crashes (`0` when all pass; `New` baselines count as pass). Previously `RunCommand.RunAsync` was void-returning and CLI always exited `0` — silent false-positives for any CI consumer. Regression against `spec/PHASES.md` Phase 4. Bug 0007. Precursor commit before Phase 1 of the debug-overhaul implementation (`MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md`).

### Added — Debug-overhaul audit (2026-05-24)
- `docs/research/2026-05-24-canary-surface-audit.md` — full inventory of Canary's UI / CLI / agent / report / localhost / screenshot surface. Phase A output of the audit prompt `MultiVerse/prompts/canary-debug-overhaul-audit-2026-05-24.md`. Drives the debug-overhaul design (Phase C, in flight) — telemetry envelope, Claude-readable REPORT.md, non-headless enforcement, sketch+annotate feedback, tiered localhost manager.
- `docs/research/2026-05-24-test-harness-prior-art.md` — Phase B prior-art survey. Three references (Playwright Inspector + Trace Viewer, Cypress App + Cloud, Sysinternals Process Explorer) with steal / skip per reference and a cross-tool synthesis table mapping conventions to Canary's §C design sections.
- `docs/plans/2026-05-24-canary-debug-overhaul.md` — Phase C design proposal. Nine design sections (C1 universal telemetry envelope, C2 Claude-readable REPORT.md, C3 non-headless enforcement, C4 UI overhaul, C5 sketch+annotate, C6 feedback channel (file inbox + MCP server), C7 tiered localhost manager, C8 live+past-runs browser, C9 VLM/visual-regression demotion path) plus Implementation Plan appendix proposing a 9-phase ordering totaling ~9.5–11.5 weeks of focused work (with a Phases 1–4 v1 cut at ~4.5 weeks delivering ~70% of operator-visible value). First entry under new `docs/plans/` directory in Canary.

### Added — Qualia workload (backfill May 2026; promoted from Stub → Active)
- `spec/QUALIA_WORKLOAD.md` workload specification (2026-05-24) — promoted from `workloads/qualia/AGENT_NOTES.md` per the 2026-05-24 testing+Canary audit (`MultiVerse/audit/2026-05-24-testing-canary-audit-and-plan.md`).
- `spec/PEERS.md` Qualia section (2026-05-24) — documents the Canary↔Qualia contract (hook stability, port co-existence with Penumbra, baseline conventions, hook-addition workflow).
- Qualia workload buildout (2026-05-08) — initial commit `13771d9`. New `Canary.Agent.Qualia` CDP bridge agent, `workloads/qualia/workload.json` (agentType `qualia-cdp`, CDP port 9223, Vite port 5173 for co-existence with Penumbra), 5 fixtures + `landing-screen.json` suite. Pixel-diff + VLM (gemma4:e4b) supported. First fixture pins the May-8 LandingScreen overlap bug as a VLM regression.
- Wave 0.B Playground hooks for Qualia workload (2026-05-10) — gated by Qualia's `debug.playground` module; scenario + snapshot hooks (random / grid / tree / scale-free / stress-1k / stress-10k); `playground.json` suite (one test per scenario + snapshot round-trip).
- qualia-v4-ui suite (2026-05-12) — pointer / qverse / RAG UI fixtures. `__canaryPersona*` rename (legacy `__canaryModule*` aliases preserved for one transition release).
- RH-2 multi-display sweep (2026-05-14) — `workloads/qualia/suites/multi-display.json` with 11 `rh2-*.json` tests covering theme / junction / edge-width / bloom / particulate variants on Qualia's `examples/minimal/.qualia` (9 nodes / 4 contexts / 1 ghost / 1 cross-context edge). Three new Qualia hooks: `__canaryApplyPerfSnapshot`, `__canaryWaitForRenderSettled`, `__canaryLoadMinimalSample`. Tests use `setup.commands` raw JS — no new C# bridge action needed.
- Diagnostic dump hook (`__canaryDumpDiagnostics`, 2026-05-19) — renders pipeline state (persona enable map / edge type colors / tone mapping / bg / fog) as a top-right overlay before the next screenshot; dumps to console for offline capture. Pencil-profile debugging convention.
- README.md workload table updated: Qualia row `Browser (Vite + Chrome via CDP) | Canary.Agent.Qualia | Active — 6 suites / 78 tests` (was `Custom viewer | Built-in module | Stub`).

### Added
- File-source checkpoints: `TestCheckpoint` gains `source` (default `"viewport"`) and `panelNickname` fields — when `source: "file"`, the runner reads a file path from a GH panel instead of capturing a viewport screenshot, then copies it into candidates for normal pixel-diff comparison. Enables Pigture Cycles render output to flow through the checkpoint pipeline.
- VLM Oracle comparison mode (Phase 8): checkpoints with `mode: "vlm"` evaluate screenshots against natural-language descriptions using a Vision-Language Model, returning pass/fail verdicts without requiring baseline images
- `VlmConfig` type for configuring VLM provider/model in test setup
- `ClaudeVlmProvider`: calls Anthropic Messages API with base64 screenshot + description prompt
- `VlmEvaluator` factory with API key resolution from `CANARY_VLM_API_KEY` / `ANTHROPIC_API_KEY`
- Mixed-mode test support: pixel-diff and VLM checkpoints can coexist in the same test
- HTML report: VLM detail sections showing description, reasoning, confidence, and screenshot
- `OllamaVlmProvider`: calls local Ollama instance for VLM evaluation — no API key required, supports any vision-capable model (e.g., `gemma4:e4b`)
- 26 unit tests for VLM feature (98 total)
- 7 Penumbra VLM oracle test definitions: `vlm-tape-csg-geometry`, `vlm-atlas-blob-organic`, `vlm-cornell-box-layout`, `vlm-teapot-shape`, `vlm-multi-field-separation`, `vlm-terrain-landscape`, `vlm-teapot-metal-mixed`
- `vlm` suite definition (7 tests) for the Penumbra workload
- Full suite updated to 51 tests (was 44)

### Fixed
- Atlas cell boundary rectangular artifact in Penumbra — face-neighbor expansion in cascade manager ([bug 0005](docs/bugs/0005-atlas-cell-boundary-artifact.md))

### Added
- 10 new CPig gap buildout test entries: `cpig-24-shortest-path`, `cpig-25-feature-edges`, `cpig-26-graph-export`, `cpig-27-svg-export`, `cpig-28-unroll-overlap`, `cpig-29-unroll-tabs`, `cpig-30-vdb-morphology`, `cpig-31-vdb-nary-boolean`, `cpig-32-geodesic`, `cpig-33-adaptive-mesh`
- CPig suite updated to 33 tests (was 23) — covers graph shortest path, feature edges, export formats, unroll overlap/tabs, VDB morphology, geodesic distance, and adaptive meshing
- 5 new CPig field modifier test definitions: `cpig-19-noise-field`, `cpig-20-domain-modifiers`, `cpig-21-field-mix`, `cpig-22-turbulence`, `cpig-23-displace-mesh`
- CPig suite updated to 22 tests (was 17) — covers noise, domain modifiers, value modifiers, and geometry effects
- `--keep-open` flag for `canary run` — keeps the target application open after tests complete for manual inspection (press Ctrl+C to close)
- Phase 9 material preset tests: 5 new Canary tests (metal, wood, marble, zebra, damascus on SDF Teapot)
- Phase 9 environment preset tests: 4 atlas-blob env tests (outdoor, sunset, night, neutral) + 2 custom lighting/bg tests
- `materials.json` + `environment.json` suite definitions
- Full suite updated to 44 tests (was 33)
- Phase 7+8 effects regression suite: 8 new Canary tests (fog, ACES, exposure, emissive, Fresnel, contours, onion, noise) exercising Penumbra shader effects via CDP hooks
- `effects.json` suite definition for the 8 Phase 7+8 effect tests
- Full suite updated to 33 tests (was 25)
- Named test suites: `--suite` CLI option for running grouped subsets of tests (e.g., `canary run --workload penumbra --suite smoke`)
- `SuiteDefinition` model class for suite JSON files (`workloads/{workload}/suites/*.json`)
- Suite discovery and test resolution in `TestDiscovery` (`DiscoverSuitesAsync`, `DiscoverTestsForSuiteAsync`)
- Suite-scoped results directories: `results/{suiteName}/{testName}/` — running one suite never clobbers another's baselines/candidates/diffs
- Suite-scoped reports: `results/{suiteName}/report.html` and `junit.xml`
- 5 Penumbra suite definitions: smoke (4 tests), scenes (10), display-modes (8), overlays (7), full (25)
- Documentation system: `docs/` directory with bugs, decisions, debug-sessions, features, and templates
- Auto-journaling rules in CLAUDE.md for maintaining living documentation
- 20 new Penumbra visual regression test JSONs covering all 10 scenes, display modes, and overlays (25 total)
- Penumbra display mode control hooks: `__canarySetDisplayMode`, `__canaryGetAvailableModes`, `__canaryGetDisplayMode`
- Visual expectations reference: `workloads/penumbra/VISUAL_EXPECTATIONS.md`

## [0.4.0] - 2026-04-24

### Added
- Penumbra CDP bridge agent (`Canary.Agent.Penumbra`) for browser-based visual regression testing
- CDP client library (`CdpClient`, `ChromeLauncher`) in `Canary.Core/Cdp/`
- Vite dev server management (`ViteManager`) for Penumbra test harness
- Penumbra test definitions: `tape-csg-orbit`, `atlas-blob-orbit`, `multi-field-orbit`, `stress-test-orbit`, `atlas-blob-bricks-overlay`
- Penumbra-side hooks (`canary-hooks.ts`) for scene/camera/animation control
- Workload documentation: `AGENT_NOTES.md`, `CLAUDE_CODE_RUNNER.md`, `BRICK_OVERLAY_TEST.md`

### Fixed
- Brick overlay invisible due to row-major/column-major matrix swap in Penumbra ([bug 0001](docs/bugs/0001-brick-overlay-invisible.md))
- Register overflow in 64-sphere stress test -- compiler never recycled registers ([bug 0002](docs/bugs/0002-register-overflow-64-sphere.md))
- coarseRes initialization ordering -- `initAtlas()` called before `computeCascadeParams()` ([bug 0003](docs/bugs/0003-coarse-res-ordering.md))
- atlas-diagnostic.ts temporal dead zone error ([bug 0004](docs/bugs/0004-atlas-diagnostic-tdz.md))

## [0.3.0] - 2026-04-11

### Added
- WinForms GUI application (`Canary.UI`) with dark theme (Phase 9)
- Results viewer with baseline/candidate/diff image comparison (Phase 10)
- Full-size image viewer with zoom and pan (Phase 10)
- Test definition editor with validation (Phase 11)
- Workload configuration editor (Phase 11)
- Test runner panel with live progress and stop button (Phase 11)
- Recording panel for input capture (Phase 11)
- Keyboard shortcuts: Ctrl+O, Ctrl+R, F5, Ctrl+Shift+R, Ctrl+A, Delete, Escape (Phase 12)
- Drag-and-drop support for `.json` and `.3dm` files (Phase 12)
- Context menus for workload and test tree nodes (Phase 12)

### Changed
- Extracted `Canary.Core` shared library from `Canary.Harness` (Phase 8)
- `TestRunner` now accepts `ITestLogger` via constructor injection (Phase 8)
- Extracted `BaselineManager` and `TestDiscovery` into Core (Phase 8)

### Fixed
- Rhino plugin now outputs `.rhp` extension correctly (Phase 8)

## [0.2.0] - 2026-04-04

### Added
- Rhino workload agent (`Canary.Agent.Rhino`) with RhinoCommon API integration (Phase 6)
- Workload stubs for Qualia and Penumbra (Phase 7)
- Agent creation guide: `docs/creating-a-workload.md` (Phase 7)
- HTML report generator with dark theme, embedded base64 images, status badges (Phase 5)
- JUnit XML report generator for CI integration (Phase 5)
- `canary report` command to open most recent report (Phase 5)
- `--verbose` and `--quiet` CLI flags (Phase 5)
- Color-coded console output: green/red/magenta/yellow for PASS/FAIL/CRASH/NEW (Phase 5)

## [0.1.0] - 2026-04-04

### Added
- Solution scaffold with `Canary.sln`, CLI shell, Ctrl+C handler (Phase 0)
- Named pipe IPC with JSON-RPC protocol (Phase 1)
- `ICanaryAgent` interface with Execute, CaptureScreenshot, Heartbeat, Abort (Phase 1)
- `AgentServer` and `HarnessClient` for bidirectional communication (Phase 1)
- Input recording and replay via Win32 `SendInput` (Phase 2)
- `ViewportLocator` for window discovery and coordinate normalization (Phase 2)
- Pixel diff comparer with configurable color threshold (Phase 3)
- SSIM comparer with 8x8 sliding window (Phase 3)
- Composite image builder (baseline | candidate | diff) (Phase 3)
- Test runner orchestrating full lifecycle: launch, connect, replay, capture, compare (Phase 4)
- App launcher, process manager, watchdog with 3-miss heartbeat timeout (Phase 4)
- `canary run` and `canary approve` CLI commands (Phase 4)
- 52 unit tests covering all core functionality
