---
title: "Feature Status"
date: 2026-04-25
tags:
  - features
  - status
---

# Feature Status

Living tracker for all Canary features. Updated as work progresses.

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

## Documentation System

| Feature | Status | Notes |
|---------|--------|-------|
| `docs/bugs/` -- individual bug files | Done | Dataview-queryable frontmatter |
| `docs/decisions/` -- ADRs | Done | MADR format |
| `docs/debug-sessions/` -- investigation journals | Done | Template ready, no entries yet |
| `docs/features/FEATURE_STATUS.md` | Done | This file |
| `docs/templates/` -- reusable templates | Done | bug, decision, debug-session, feature |
| `CHANGELOG.md` -- Keep a Changelog format | Done | Versions 0.1.0 through Unreleased |
| Auto-journaling rules in CLAUDE.md | Done | Triggers for bugs, features, decisions |
| Obsidian compatibility | Done | YAML frontmatter, standard links, .gitignore |
