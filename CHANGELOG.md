---
title: "Changelog"
tags:
  - changelog
---

# Changelog

All notable changes to Canary are documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Fixed
- Atlas cell boundary rectangular artifact in Penumbra — face-neighbor expansion in cascade manager ([bug 0005](docs/bugs/0005-atlas-cell-boundary-artifact.md))

### Added
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
