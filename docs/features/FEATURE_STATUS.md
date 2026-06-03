---
title: "Feature Status"
date: 2026-04-25
tags:
  - features
  - status
---

# Feature Status

Living tracker for all Canary features. Updated as work progresses.

## Supervised sessions (capture-and-annotate, no automated tests)

| Workload | Status | Phase | Notes |
|---------|--------|-------|-------|
| Qualia (CDP) | Done | 14 | Shipped 2026-05-27. Telemetry via CDP Console + Log + Network. |
| Penumbra (CDP) | Done | 14 | Shipped 2026-05-27. Same telemetry path as Qualia. |
| Rhino (named pipe) | v1 landed | 15.1 (2026-06-02) | `RhinoSessionAgent` wraps `AppLauncher.Launch` + `HarnessClient` behind `ICanaryAgent`. Smoke-verified end-to-end. v1 caveats: no telemetry source yet (Rhino command-line + Slop log tail → v2); Rhino process not torn down on closeout (zombie processes; v1.1 fix); no `--file`/`--mech` shortcuts (v2). |

## Core Harness

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| CLI shell (`canary run/approve/report/record`) | Done | 0, 4-5 | All commands functional |
| Ctrl+C abort handling | Done | 0 | Always available, kills child processes |
| Named pipe IPC (JSON-RPC) | Done | 1 | `canary-{workload}-{pid}` naming |
| Input recording (Win32 hooks) | Done | 2 | WH_MOUSE_LL / WH_KEYBOARD_LL on STA thread |
| Input replay (SendInput) | Done | 2 | MOUSEEVENTF_ABSOLUTE, CancellationToken support |
| Pixel diff comparer | Done | 3 | Configurable color threshold, diff image output |
| SSIM comparer | Done | 3 | 8x8 sliding window, grayscale luminance |
| Composite builder | Done | 3 | Baseline/candidate/diff horizontal strips |
| Test runner + orchestrator | Done | 4 | Full lifecycle: launch, connect, replay, capture, compare |
| App launcher + process manager | Done | 4 | Process tracking, KillAll on shutdown |
| Watchdog (heartbeat monitor) | Done | 4 | 3 consecutive misses = dead |
| HTML report generator | Done | 5 | Dark theme, embedded images, status badges |
| JUnit XML report | Done | 5 | CI-compatible output |
| `--verbose` / `--quiet` modes | Done | 5 | Per-checkpoint details vs summary only |

## WinForms GUI

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| Main window layout | Done | 9 | Dark theme, ToolStrip, SplitContainer, StatusStrip |
| Workload discovery + tree | Done | 9 | Auto-detects `workloads/` directory |
| Results viewer | Done | 10 | Per-checkpoint images with stats |
| Image viewer (full-size) | Done | 10 | Zoom (0.1x-10x), pan, keyboard navigation |
| Approve/reject from GUI | Done | 10 | Per-checkpoint and per-test buttons |
| Test result serialization | Done | 10 | JSON round-trip with history scanning |
| Test definition editor | Done | 11 | Validation, DataGridView for checkpoints |
| Workload config editor | Done | 11 | Browse for exe, agent type combo |
| Test runner panel (live progress) | Done | 11 | Background thread, stop button, log display |
| Recording panel | Done | 11 | Start/stop, save dialog |
| Keyboard shortcuts | Done | 12 | Ctrl+O/R/A, F5, Delete, Escape |
| Drag-and-drop | Done | 12 | .json and .3dm files on tree |
| Context menus | Done | 12 | Right-click workload/test nodes |

## Workload Agents

| Agent | Status | Target | Notes |
|-------|--------|--------|-------|
| Rhino (`Canary.Agent.Rhino`) | Done | Rhino 8 (.rhp plugin) | RhinoCommon API, net48 |
| Penumbra (`Canary.Agent.Penumbra`) | Active | Chrome via CDP | Browser bridge, Vite integration, 44 tests across 5 suites |
| Qualia | Stub | Custom viewer | `workloads/qualia/AGENT_NOTES.md` only |
| Grasshopper (via Rhino agent) | Active | Rhino 8 + GH | 22 CPig tests via Slop loader, shared-instance suite |

## Penumbra Integration

| Feature | Status | Notes |
|---------|--------|-------|
| CDP client library | Done | WebSocket-based, built-in .NET types only |
| Chrome launcher (auto-detect Edge/Chrome) | Done | `--force-device-scale-factor=1` for determinism |
| Vite dev server management | Done | Startup detection, graceful shutdown |
| Scripted camera positions (Path A) | Done | `setSpherical()` via `Runtime.evaluate` |
| Recorded mouse input (Path B) | Done | CDP `Input.dispatchMouseEvent` |
| Canvas size locking | Done | `__canaryLockSize` hook |
| Atlas convergence waiting | Done | Polls `isAtlasBuildComplete()` |
| Penumbra-side hooks | Done | `canary-hooks.ts` in test/main.ts |
| 51 test definitions (8 suites) | Done | smoke (4), scenes (10), display-modes (8), overlays (7), effects (8), materials (5), environment (6), vlm (7) — 15 NEW pixel-diff awaiting baseline approval |

## CPig / Rhino Integration

| Feature | Status | Notes |
|---------|--------|-------|
| Slop loader fixture (`cpig_slop_loader.gh`) | Done | Drives CPig components via JSON test definitions |
| Agent actions (SetPanelText, SetToggle, GetPanelText) | Done | Wired to Slop fixture panels |
| Test runner asserts (PanelEquals, PanelContains, PanelDoesNotContain) | Done | 3 asserts per test: SlopSuccess, no FATAL, no CRASH |
| `cpig` suite (shared Rhino instance) | Done | 22 tests, `runMode: shared`, single Rhino launch |
| Test generation script (`cpig-test-from-slop.ps1`) | Done | Reads Slop JSONs, emits Canary test wrappers |
| Field Modifier tests (cpig-19 through cpig-23) | Done | Noise, domain modifiers, value modifiers, mesh displacement |

## VLM Oracle (Phase 8)

| Feature | Status | Notes |
|---------|--------|-------|
| `mode` field on `TestCheckpoint` | Done | `"pixel-diff"` (default) or `"vlm"` |
| `VlmConfig` DTO | Done | provider, model, maxTokens |
| `IVlmProvider` + `VlmVerdict` | Done | Provider abstraction |
| `ClaudeVlmProvider` | Done | Anthropic Messages API via HttpClient |
| `OllamaVlmProvider` | Done | Local Ollama, no API key, 5-min timeout |
| `VlmEvaluator` factory | Done | "claude" and "ollama" providers |
| TestRunner VLM branching | Done | Lazy-init provider, `ProcessVlmCheckpointAsync` |
| HTML report VLM rendering | Done | Adaptive columns, VLM detail sections with reasoning |
| Mixed-mode tests | Done | pixel-diff + VLM checkpoints in same test |
| Unit tests (26) | Done | Serialization, parsing, configuration, Ollama provider |

## Debug-overhaul (2026-05-24, shipped Phases 0-9)

Implementation of `docs/plans/2026-05-24-canary-debug-overhaul.md` driven
by `MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md`.
9 design phases (C1–C9) over 10 implementation phases (0 + precursor + 1–9).

| Feature | Status | Phase | Notes |
|---------|--------|-------|-------|
| CLI exit code propagation (bug 0007 fix) | Done | Precursor | RunCommand.RunAsync returns int; 1 on any test fail or crash. |
| Non-headless enforcement (`--headless` flag + UI auto-launch) | Done | 1 (C3) | STANDARD.md §16 rule 8. Single-instance pipe forwards args. |
| Universal telemetry envelope | Done | 2 (C1) | `Canary.Telemetry.TelemetryRecord` + NDJSON sink. CDP Console + Log + Network captured by Penumbra + Qualia agents. |
| Claude-readable REPORT.md per run | Done | 3 (C2) | `MarkdownReportGenerator` + per-run dir `runs/<timestamp>/`. ResultsHistory dual-shape scan. |
| Tiered localhost manager (T1 + T2 + T3) | Done | 4 + 6 + 8 (C7) | Passive netstat + Canary spawn registry + name heuristic. Inline Tier 3 toggle. |
| Sketch + annotate feedback surface | Done | 5 (C5) | WPF `AnnotationCanvas` via ElementHost; FeedbackInboxWriter at `docs/feedback/inbox/`. |
| MCP server | Done | 6 (C6) | `Canary.McpServer.exe`, 8 tools, self-contained stdio JSON-RPC. |
| UI overhaul (nav tabs + mode picker) | Done | 7 (C4) | INavMode + TabControl wrapping the SplitContainer. Tests/PastRuns/Localhost/Feedback/Telemetry/Settings tabs. Toolbar mode picker resolves §A1 gap. |
| Settings persistence | Done | 8 (C9) | `CanarySettings` at `%LocalAppData%\Canary\settings.json`. UI mode + Tier 3 + retention. Maturation-mode panels not built per §C9. |
| Cross-repo doc pass | Done | 9 | Penumbra + Qualia CLAUDE.mds + MultiVerse BUILD_LOG updated. |
| Toolbar / nav-tab polish | Done | post-Phase 9 | Mode picker width fix; nav tabs FlatButtons + larger font; Tests-only items hide on non-Tests tabs; redundant Localhost toolbar button dropped. |

**Deferred follow-ups** (documented in BUILD_LOG.md phase entries):
- Rhino-side `RhinoApp.WriteLine` interception (Phase 2 — no clean RhinoCommon 8 hook found in scope; queued v2).
- InputReplayer event records (cross-cuts Phase 7 UI work).
- Per-test telemetry slicing in shared-suite mode (boundaries ambiguous).
- Moving candidates/diffs/composite into per-run dirs (current Phase 3 keeps them flat — overwrites per run).
- WMI command-line filtering for Tier 3 (name-only ships).
- Maturation-mode panels — explicit out per §C9 (toggle only).
- ResultRetention auto-wiring (helper available; operator decides cadence).
- PastRuns body search across REPORT.md content (metadata-only filter today).
- McpServerStdioIntegrationTests (in-process StringReader/Writer covers the protocol).
- UIOverhaulSmokeTests integration test (NavModeTests cover the contract).

## Documentation System

| Feature | Status | Notes |
|---------|--------|-------|
| `docs/bugs/` -- individual bug files | Done | Per-bug status/severity in YAML frontmatter |
| `docs/decisions/` -- ADRs | Done | MADR format |
| `docs/debug-sessions/` -- investigation journals | Done | Template ready, no entries yet |
| `docs/features/FEATURE_STATUS.md` | Done | This file |
| `docs/templates/` -- reusable templates | Done | bug, decision, debug-session, feature |
| `CHANGELOG.md` -- Keep a Changelog format | Done | Versions 0.1.0 through Unreleased |
| Auto-journaling rules in CLAUDE.md | Done | Triggers for bugs, features, decisions |
