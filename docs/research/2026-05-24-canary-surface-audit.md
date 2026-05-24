---
date: 2026-05-24
tags: [research, canary, audit, debug-overhaul]
status: completed
project: canary
component: full-surface
---

# Canary surface audit — input to the debug-overhaul design

Parent prompt: `MultiVerse/prompts/canary-debug-overhaul-audit-2026-05-24.md` (Phase A).
Sibling: `2026-05-24-test-harness-prior-art.md` (Phase B).
Drives: `docs/plans/2026-05-24-canary-debug-overhaul.md` (Phase C, to be written).

## Executive summary

Canary today is a *visual regression harness* with a working WinForms shell, three CDP/named-pipe agents (Rhino / Penumbra / Qualia), a pixel-diff + VLM comparison engine, and a per-test result.json + HTML report pipeline. **What it is not yet** — and what the debug-overhaul aims for — is a *debugging cockpit*. The headline gaps:

1. **Console + network telemetry: 0/3 workloads capture them.** CDP `Console.enable` / `Network.enable` are never called; named-pipe heartbeat returns only a small state dict.
2. **Click + key event log: 0/3 workloads.** Penumbra dispatches mouse events via `Input.dispatchMouseEvent` but doesn't log the dispatched stream; Rhino replays via `SendInput` ditto; Qualia uses JS hooks for clicks (no log).
3. **Reports scatter across files.** A failing test means reading `result.json` + the HTML report + the run-log text in the GUI + (for Rhino) `C:\Repos\CPig\logs\agent_viewport_diag.log` + the Slop LogHub file. There is no single "what happened in this run" doc.
4. **CLI does NOT launch the UI** — confirmed by source grep for `Canary.UI.exe`: no match. `STANDARD.md` §16 locked rule 8 names this as queued V1 work.
5. **Runs overwrite each other.** `workloads/<w>/results/<test>/result.json` has no timestamp — every run clobbers the last. No browsable history (the `ResultsHistory` service scans `result.json` files via *file mtime*, so "last 5 runs" is impossible).
6. **Localhost knowledge is workload-private.** `ViteManager` (Penumbra) + `ViteManager` (Qualia) each independently scan `netstat -ano` and `taskkill /F /T` orphans on the dev port. This pattern is exactly what a localhost manager needs — but it's duplicated, not exposed, and `ProcessManager` doesn't know about it.
7. **UI-only mode picker missing.** `--mode {pixel-diff|vlm|both}` is a CLI flag. The TestRunnerPanel constructs `TestRunner` without setting `ModeOverride`. UI runs are always `None` (= honor per-checkpoint `mode`). The picker should be in the runner panel.

The good news: the named-pipe + CDP plumbing, the `ITestProgressEvents` interface for live UI events, the `ProgressFeedPanel` with VLM cards, the `ResultsViewerControl` with per-checkpoint approve/reject, and the dark-themed WinForms shell all exist. The overhaul builds on these rather than replacing them.

---

## A1 — Canary.UI WinForms surface

### Forms + Controls inventory

| File | Purpose | Operator-visible controls | Consumes | Emits |
|---|---|---|---|---|
| `Program.cs` | Entry point — `Application.Run(new MainForm())`. | none | none | none |
| `MainForm.cs` | App shell: ToolStrip + TreeView nav + content panel + StatusStrip. | ToolStrip: **Open Folder**, **Run Tests**, **Record**, **Approve**, **View Report**, **Deploy Agent**, **Close Workload**, **Expand All**. TreeView: workload → `Suites (N)` / `All Tests (N)` / `Recordings (N)`. Status colors: Pass=green, Fail=red, Crashed=purple, New=yellow. Keyboard: Ctrl+O / Ctrl+R / F5 / Ctrl+Shift+R / Ctrl+A / Delete. Pause-key abort hotkey registered during runs. | `workloads/` dir (auto-detect or Open Folder dialog). | Spawns TestRunnerPanel / TestEditorControl / SuiteEditorControl / WorkloadEditorControl / RecordingPanel / ResultsViewerControl into the content panel. Writes test JSON / suite JSON / workload.json edits. |
| `Controls/TestRunnerPanel.cs` | Live test execution. | Top: status label + suite label + **Stop (Pause)** button (turns into "Close App" when keepOpen). Middle: ProgressBar. SplitContainer: left **log box** (Consolas, selectable RichTextBox), right **ProgressFeedPanel**. 50/50 split, FixedPanel.Panel2. | `WorkloadConfig`, list of `TestDefinition`, workloadsDir, suiteName, useSharedMode, suiteKeepOpen. Wires `GuiTestLogger` → log box, `ProgressFeedPanel` → TestRunner.Progress. | `RunCompleted` event with `SuiteResult`. Saves `workloads/<w>/results/<test>/result.json` per test. Saves `workloads/<w>/results/report.html`. |
| `Controls/ProgressFeedPanel.cs` | Per-checkpoint live card feed (implements `ITestProgressEvents`). | Header label (current test name); FlowLayoutPanel of `CheckpointCard` (540×~165). Each card: title, status badge (Pending/Evaluating/Pass/Fail/Crash with color), screenshot thumbnail (clickable → ImageViewerForm), VLM prompt label, VLM reasoning label. Placeholder shown pre-run. | TestRunner.Progress callbacks. | none (display only). |
| `Controls/ResultsViewerControl.cs` | Per-test + per-suite results viewer. | TableLayoutPanel of per-checkpoint rows with baseline / candidate / diff thumbnails + diff% / SSIM / tolerance. Approve / Reject / Approve-All buttons (per checkpoint and per suite). For suite mode: header + suite grid + per-test expandable sections. | `TestResult`, `SuiteResult`. | `ApproveCheckpointRequested(testName, cpName)`, `RejectCheckpointRequested(testName, cpName)`, `ApproveAllRequested`. |
| `Controls/TestEditorControl.cs` | Edit a `TestDefinition` JSON. | (not fully read — surface: form fields for test JSON + Save button) | TestDefinition. | `SaveRequested(json)` — MainForm writes to disk. |
| `Controls/SuiteEditorControl.cs` | Edit a `SuiteDefinition` JSON. | (similar) | SuiteDefinition + tests list. | `SaveRequested(json)`. |
| `Controls/WorkloadEditorControl.cs` | Edit `workload.json` (with Penumbra-aware fields). | (similar) | WorkloadConfig + optional `PenumbraWorkloadConfig`. | `SaveRequested(json)`. |
| `Controls/RecordingPanel.cs` | Mouse/keyboard recording for input replay. | (workload dropdown + Launch + Start/Stop + Save buttons; binds `InputRecorder`) | List of `WorkloadConfig`, workloadsDir. | `RecordingSaved` event → MainForm refreshes tree. |
| `Controls/AbortOverlayForm.cs` | Borderless overlay anchored to the target window showing "RUNNING" + abort gesture. | "RUNNING" / "ABORTING" text overlay, ESC closes. | target HWND. | `Aborted` event. |
| `Controls/ImageViewerForm.cs` | Click-to-zoom screenshot viewer. | (image canvas + nav) | image path + sibling images. | none. |
| `Services/WorkloadExplorer.cs` | Walks workloads dir → workload entries with tests + suites + recordings lists. | none | dir path. | `LoadWarning` event. |
| `Services/ResultsHistory.cs` | Scans `workloads/<w>/results/**/result.json`, sorted by file mtime. | none | workloadsDir + workload name. | `List<HistoryEntry>` with FilePath + TestResult + Timestamp. |
| `Services/GuiTestLogger.cs` | Bridges `ITestLogger` → events the panel can subscribe to. | none | logger callbacks. | `MessageLogged`, `StatusLogged`, `SummaryLogged` events. |
| `Services/AbortHotkey.cs` | Global Pause-key hotkey (via WndProc + RegisterHotKey). | none. | MainForm handle. | `AbortRequested` event. |

### What is reachable only via the CLI today

Every CLI flag from `src/Canary.Harness/Cli/RunCommand.cs`:

| `canary run` flag | UI exposure |
|---|---|
| `--workload` | UI-exposed (TreeView workload selection) |
| `--test` | UI-exposed (TreeView test selection) |
| `--suite` | UI-exposed (TreeView suite selection) |
| `--verbose` | **CLI-only.** GUI hardcodes `verbose: true` (TestRunnerPanel.cs:195). |
| `--quiet` | **CLI-only.** |
| `--keep-open` | UI-partial: not a top-level button, but per-test `keepOpenOnFailure` JSON field is honored + UI shows "Close App" button after a failing run with that flag. |
| `--mode {pixel-diff,vlm,both}` | **CLI-only — primary gap.** `TestRunnerPanel.RunAsync` creates `TestRunner` without setting `ModeOverride`, so UI runs are always `None` (per-checkpoint `mode` field wins). No picker anywhere. |
| `canary record --app --name` | UI-exposed (RecordingPanel). |
| `canary approve --workload --test --suite` | UI-exposed (per-checkpoint Approve/Reject + Approve-All + toolbar Approve + context-menu Approve). |
| `canary report --workload` | UI-exposed (toolbar **View Report** opens most recent `report.html`). |

**Other UI-only capabilities (no CLI equivalent):**
- Per-test result browsing (TreeView click → ResultsViewerControl loads the most recent `result.json`).
- Drag-and-drop of test JSON / `.3dm` into the tree (Drag handlers exist but `OnTreeDragDrop` only displays a status message — no actual import implemented yet).
- Deploy Agent (toolbar): copies `Canary.Agent.Rhino` build to `%AppData%\McNeel\Rhinoceros\8.0\Plug-ins\Canary (...)`.
- New Suite via context menu.
- Edit Workload Config / Edit Test / Edit Suite / Duplicate Test.

---

## A2 — Canary.Harness CLI surface

Subcommands (all defined in `Cli/`):

| Command | Flags | Exit behavior | Output writes |
|---|---|---|---|
| `canary run` | `--workload` (req), `--test`, `--suite`, `--verbose`, `--quiet`, `--keep-open`, `--mode {pixel-diff,vlm,both}` (default pixel-diff). Mutual-exclusion: `--test` + `--suite` is rejected. | Currently never returns non-zero on test failure — `RunAsync` is `void` return and exit code stays 0 (regression from `spec/PHASES.md` Phase 4 which specified `0=pass / 1=fail / 2=crash`; the code only sets non-zero via uncaught exception → SystemCommandLine default). | `workloads/<w>/results/[<suite>/]report.html` (HtmlReportGenerator), `…/junit.xml` (JUnitReportGenerator). Note: in CLI path, per-test `result.json` is NOT written (only `TestRunnerPanel.RunAsync` writes it). **Gap.** |
| `canary record` | `--app` (req), `--name` (req) | Returns after Ctrl+C / Enter. | `workloads/<app>/recordings/<name>.input.json`. |
| `canary approve` | `--workload` (req), `--test` (req), `--suite` (optional) | 0 always. | Promotes `workloads/<w>/results/<test>/candidates/*.png` to baselines (path TBD — see BaselineManager). |
| `canary report` | `--workload` (optional) | 0 always. | Opens most recent `report.html` via `Process.Start` with `UseShellExecute=true`. |

**Working-directory dependency:** `RunCommand.RunAsync` uses `Directory.GetCurrentDirectory()` to find `workloads/` — the CLI MUST be run from `C:\Repos\Canary` (matches the `CLAUDE.md` "Quick Reference" hint).

**Ctrl+C handling:** `Program.OnCancelKeyPress` sets a single static `Cts`; `RunCommand` registers an additional handler that calls `pm.KillAll()` on the workload's `ProcessManager`.

---

## A3 — Telemetry pipeline (per workload + unified gap)

### Rhino workload (`src/Canary.Agent.Rhino/RhinoAgent.cs`, net48)

- **Captured today:**
  - Viewport screenshot via `Rhino.Display.ViewCapture.CaptureToBitmap` (RhinoScreenCapture.cs).
  - Heartbeat: `{ rhinoVersion, documentName, objectCount }`.
  - Per-Grasshopper-panel text read via `HandleGrasshopperGetPanelText` — used by `TestAssert.PanelEquals/Contains/DoesNotContain`.
  - **Side-channel:** `RhinoAgent.HandleSetViewport` writes diagnostic lines to `C:\Repos\CPig\logs\agent_viewport_diag.log` (hardcoded path).
  - **Side-channel:** the CPig `_auto_insert_log_taps` pattern writes Slop component output to LogHub files — Canary doesn't read them, but they exist alongside the test artifacts.
- **NOT captured:**
  - Rhino's command-line console output (would need `RhinoApp.WriteLine` interception or `CommandLineWriter` subscription — not done).
  - .NET exception traces from GH components (the popup-dismisser auto-dismisses dialogs, eating any human-readable error text in the process).
  - GH solver step events (only the binary "PostProcess for 600ms" quiesce signal is observable, not the per-component status).
  - Input replay event log (what `InputReplayer` actually dispatched).

### Penumbra workload (`src/Canary.Agent.Penumbra/PenumbraBridgeAgent.cs`, net8.0-windows, Playwright + CDP)

- **Captured today:**
  - Screenshot via `Page.captureScreenshot` (CdpClient.cs:210, with optional clip rect).
  - Heartbeat: `window.__canaryGetRendererInfo()` result dict (backend, atlas state, error if any).
  - Application-side state: every action result includes a textual `Message` field.
- **NOT captured:**
  - **`Console.enable` is never sent.** No JS console messages flow back to Canary. The CDP `Console.messageAdded` event is never subscribed. Diagnostic-only side channel: Penumbra exposes `window.__canaryLogBuffer` (per project memory) that *could* be read via `Runtime.evaluate`, but no agent action does so today.
  - **`Network.enable` is never sent.** No `Network.requestWillBeSent` / `Network.responseReceived` / `Network.loadingFailed` events flow back.
  - **No click/key event log.** Mouse events are dispatched via `Input.dispatchMouseEvent` (CdpClient.cs:250) but the dispatched stream is fire-and-forget — no record of what was sent.
  - **No `Page.frameAttached` / `Runtime.exceptionThrown` events.** Uncaught JS errors surface only if the next `Runtime.evaluate` happens to read a polluted state.

### Qualia workload (`src/Canary.Agent.Qualia/QualiaBridgeAgent.cs`, net8.0-windows, Vite + CDP)

- **Captured today:**
  - Full-viewport screenshot (no clip; tests call `HideUI(true)` if they want canvas-only).
  - Heartbeat: `window.__canaryGetAppInfo()` result dict.
  - The Qualia test fixture surface is rich (~50+ `__canary*` hooks per project memory) — but each is queried only when a test action calls it.
- **NOT captured:**
  - Same as Penumbra — no Console.enable, no Network.enable, no event log.
  - The `ClearStorage` action wipes localStorage but doesn't capture pre-wipe state.
  - Qualia is not yet wrapped in the Tauri desktop shell (per `qualia-desktop-mvp-2026-05-22.md` cross-repo memory) so the "Tauri dev window" target is a future surface Canary will need to wrap.

### Unified gap analysis

| Telemetry type | Rhino | Penumbra | Qualia |
|---|---|---|---|
| Screenshot | ✓ | ✓ | ✓ |
| Heartbeat-state dict | ✓ | ✓ | ✓ |
| Per-action Message text | ✓ | ✓ | ✓ |
| JS console capture | n/a | ✗ | ✗ |
| Network capture | n/a | ✗ | ✗ |
| Native console (Rhino) | ✗ | n/a | n/a |
| Click/key event log | ✗ | ✗ | ✗ |
| Uncaught-exception trace | ✗ | ✗ | ✗ |
| Slop tap entries (via Canary) | ✗ (exists on disk) | n/a | n/a |
| Reusable telemetry envelope | — | — | — |

Bold conclusion for §C1 of the design doc: **the universal telemetry envelope has zero precedent today.** Every workload emits a textual log via the `ITestLogger` interface, but that log is unstructured `[HH:mm:ss] [Canary] message` strings.

---

## A4 — Report artifacts

### Where outputs land

For a single test (any workload):

```
workloads/<w>/results/<test-name>/
  result.json                         <- TestResult (TestResultSerializer)
  composite.png                       <- baseline|candidate|diff strips (CompositeBuilder), per-test
  baselines/                          <- approved baseline PNGs (per checkpoint)
  candidates/                         <- most-recent run's candidate PNGs (per checkpoint)
```

Plus at the workload (or suite) level:

```
workloads/<w>/results/[<suite>/]report.html   <- HtmlReportGenerator
workloads/<w>/results/[<suite>/]junit.xml     <- JUnitReportGenerator
```

**Side-channels (orthogonal to the result tree):**
- `C:\Repos\CPig\logs\agent_viewport_diag.log` — Rhino agent appends per-SetViewport diagnostic lines (cross-repo path; hardcoded).
- Slop LogHub file (per CPig test JSON convention; path depends on the test).
- Rhino's `cpig_debug.log` and `%LOCALAPPDATA%\CPig\trace.log` (per CLAUDE.md "Known Pitfalls").

### result.json shape (confirmed from a live `diag-pencil-baseline` run)

```json
{
  "TestName": "diag-pencil-baseline",
  "Workload": "qualia",
  "Status": "New",
  "CheckpointResults": [
    {
      "Name": "diag-pencil-baseline",
      "Status": "New",
      "DiffPercentage": 0,
      "Tolerance": 0.3,
      "SsimScore": 0,
      "BaselinePath": "C:\\...\\baselines\\diag-pencil-baseline.png",
      "CandidatePath": "C:\\...\\candidates\\diag-pencil-baseline.png",
      "DiffImagePath": null,
      "ErrorMessage": "No baseline exists. Run 'canary approve' to establish.",
      "VlmReasoning": null,
      "VlmConfidence": 0,
      "VlmDescription": null
    }
  ],
  "CompositeImagePath": null,
  "ErrorMessage": null,
  "Duration": "00:00:01.2191084"
}
```

Fields per `TestResult` model: `TestName`, `Workload`, `Status` (Passed/Failed/Crashed/New), `CheckpointResults[]` (with `Status`, `DiffPercentage`, `Tolerance`, `SsimScore`, `BaselinePath`, `CandidatePath`, `DiffImagePath`, `ErrorMessage`, `VlmReasoning`, `VlmConfidence`, `VlmDescription`), `CompositeImagePath`, `ErrorMessage`, `Duration` (ISO 8601 timespan string).

**Run history is not preserved.** Each run overwrites the previous `result.json`. `ResultsHistory.ScanAsync` returns one entry per test — the most-recent — ordered by file mtime. No way to compare "today's run vs last week's run."

### Claude-readability assessment

Today, to reconstruct a failing run, Claude needs to read:
1. `result.json` (verdict, paths, VLM reasoning if applicable)
2. `report.html` (suite context — embedded base64 images make this huge)
3. The text run-log displayed in the UI (lost on app close — not persisted to disk)
4. Per-workload side channels (Rhino: `agent_viewport_diag.log`; CPig tests: Slop LogHub file)
5. Optionally the candidate / baseline / diff PNGs (Claude can OCR/vision but they're at three different paths)

**Gap for §C2 design proposal:** a single Markdown `REPORT.md` per run that:
- Inlines the verdict + duration + workload + checkpoint table
- Inlines the most important telemetry (currently captured: zero; future: console tail + network errors + click log per §C1)
- Cross-links artifacts via stable relative paths
- Is generated alongside (not replacing) `result.json` + `report.html`

---

## A5 — Non-headless / UI-first state

### Current state (the gap §STANDARD.md §16 rule 8 names)

- **Source grep for `Canary.UI.exe`:** zero matches in `src/`. The CLI does not launch the UI.
- **Source grep for `headless` / `--headless`:** zero matches. There is no flag in either direction.
- **Operator workflow today:** open `Canary UI.lnk` (or `dotnet build` + run `bin\Release\net8.0-windows\Canary.UI.exe`), then click Run Tests in the UI. The UI calls `TestRunnerPanel.RunAsync` directly — NOT through `canary.exe`.
- **Penumbra Chrome is already visible-by-default.** `ChromeLauncher.LaunchAsync` (Cdp/ChromeLauncher.cs:174) passes `CreateNoWindow = true` to ProcessStartInfo (which controls the .NET parent process console, not Chrome's GUI) and never sets `--headless`. Chrome opens a visible window. Same for Qualia.
- **Rhino is launched visible** (it's a GUI app — `AppLauncher.Launch` doesn't and can't hide it).

### What "UI-first by default" requires (input to §C3)

To flip the default, `RunCommand` would need to:
1. If `--headless` not set AND the process has a console AND `Canary.UI.exe` is co-located with `canary.exe` in the same `bin/` tree → spawn `Canary.UI.exe` with an `--auto-run <args>` flag and exit early.
2. `Canary.UI.exe` gains an `--auto-run` CLI arg that bypasses the welcome panel and goes straight to TestRunnerPanel with the parsed selection.
3. The UI launches the workload visibly (already the case for all three).
4. The CLI run that triggered it stays alive only long enough to show "UI launched — see progress there" then exits.

**Recursion guard:** the UI must call `TestRunner` directly (the current code path) NOT shell out to `canary run` — otherwise UI-launching-UI loops on the Re-run button.

### Per-workload visibility today (without any code change)

| Workload | App under test visible? | Operator can see WHICH step is running? |
|---|---|---|
| Rhino | Yes (always — Rhino is a GUI app). | Partial — the AbortOverlayForm anchors a "RUNNING" badge to the target HWND. Operator sees Rhino moving, GH solver updating. Per-checkpoint status only appears in the Canary UI log + progress feed. |
| Penumbra | Yes (Chrome opens). | Same — Chrome shows the page, Canary UI shows the checkpoint cards. |
| Qualia | Yes (Chrome opens). | Same. |

The §16 rule 8 gap is NOT that the apps are headless — they aren't. It's that the **CLI** doesn't launch the **Canary UI**. The operator who types `canary run` in a terminal sees only `[HH:mm:ss] [Canary] ...` lines, no live screenshot feed, no per-checkpoint VLM verdict cards.

---

## A6 — Localhost-relevant infrastructure

### Existing port-enumeration code (the pattern the manager will reuse)

Two near-identical implementations:
- `src/Canary.Agent.Penumbra/ViteManager.cs` — `KillStaleListenerAsync(int port)` + `FindListenerPid(int port)`. Default port 3000.
- `src/Canary.Agent.Qualia/ViteManager.cs` — same pattern. Default port 5173.

Both use:
- `netstat.exe -ano` to find PID of `LISTENING` socket on a given port.
- `taskkill.exe /F /T /PID <pid>` to terminate.

The implementations are STRUCTURALLY identical (one is essentially a copy-paste of the other minus the Penumbra-specific `PENUMBRA_NO_AUTO_OPEN` env var). Candidate for extraction into `Canary.Core` per §C7.

### Per-workload child-process tracking

| Plumbing | Tracks | Lives in |
|---|---|---|
| `ProcessManager` (Core) | Process objects added via `Track()`. Used by `TestRunner` for the workload app (e.g. Rhino). KillAll on Ctrl+C / panel.Stop. | `src/Canary.Core/Orchestration/ProcessManager.cs`. |
| `PenumbraBridgeAgent._vite` (ViteManager) | Vite npm process. | Bridge agent's `_vite` field. Disposed in `agent.Dispose()`. |
| `PenumbraBridgeAgent._chrome` (ChromeLaunchResult) | Chrome browser process + temp profile dir. | Bridge agent's `_chrome` field. Disposed in `agent.Dispose()`. |
| `QualiaBridgeAgent._vite` + `_chrome` | Same. | Bridge agent. |
| Rhino subprocess | The `AppLauncher.Launch` Process returned by `Process.Start(workload.AppPath, workload.AppArgs)`. | Tracked in `ProcessManager`. |

**Critical observation:** the Vite + Chrome processes are NOT tracked in `ProcessManager`. If the bridge agent crashes between `_vite` startup and `agent.Dispose()`, those processes can survive (which is exactly the orphaned-Vite scenario `KillStaleListenerAsync` was added to fix per its inline comment dated 2026-05-06). The localhost manager should consolidate this.

### Existing ad-hoc kill paths

- `taskkill //IM Canary.UI.exe //F` (per `CLAUDE.md` "How to reproduce bugs") — operator pattern, no code.
- `ForceKillProcesses()` on TestRunnerPanel — UI button that calls `_pm?.KillAll()`.
- `_workloadContextMenu` "Close Workload" item — same path.

### IPC pipe registry

`workload.PipeName` is set in `workload.json` (e.g. `"canary-rhino"`). `RunCommand` and `TestRunnerPanel` construct the full pipe name as `$"{workload.PipeName}-{appProcess.Id}"`. There is no central registry — each test run constructs the name from the launched process's PID and stores it only in a `HarnessClient` instance scoped to the test.

**Implication for §C6 (MCP server):** the MCP server would need its own discovery mechanism — either watch named pipe enumeration via `Directory.GetFiles(@"\\.\pipe\", "canary-*")` (Windows-supported) or have Canary register active pipes in a known file at spawn time.

---

## A7 — Screenshot + diff infrastructure

### Storage layout

```
workloads/<w>/results/<test-name>/baselines/<checkpoint>.png   <- approved
workloads/<w>/results/<test-name>/candidates/<checkpoint>.png  <- most recent
workloads/<w>/results/<test-name>/<test-name>-composite.png    <- diff strips (CompositeBuilder)
```

Baselines are versioned in git (per `STANDARD.md` §16 rule 2); candidates + diffs are gitignored.

### Pixel-diff plumbing

- `Canary.Core/Comparison/PixelDiffComparer.cs` — straight Rgba32 per-channel threshold compare. Default `colorThreshold = 3` (handles antialiasing noise). Magenta-on-dim diff image.
- `Canary.Core/Comparison/SsimComparer.cs` — secondary metric (not gating).
- `Canary.Core/Comparison/CompositeBuilder.cs` — stacks `[baseline | candidate | diff]` strips vertically with a label bar above each. Pure pixel composition — no annotation layer, no overlay.
- `Canary.Core/Comparison/CompositeBuilder.cs` is image-only — no PDF, no SVG, no HTML.

### Image library

`SixLabors.ImageSharp` (per `Canary.Core.csproj`). Confirmed via grep. **Annotation viability:** ImageSharp supports drawing rectangles, paths, and text (`SixLabors.ImageSharp.Drawing.Processing` is already referenced by CompositeBuilder). Freehand strokes + rectangles + text labels can be implemented on top, BUT WinForms doesn't have a native canvas-with-modification widget — the live sketch UX would need either:
- Custom `Control.OnPaint` + `Control.OnMouse*` (functional, ugly to make smooth).
- WPF `InkCanvas` hosted via `WindowsFormsHost` (better stroke quality, requires WPF dep).
- Browser-based overlay (one-off Edge WebView2 or a small Vite app).

The CompositeBuilder output is rasterized PNG, not a layered file. Annotations would be a separate file pair: `<screenshot>.annotated.png` + `<screenshot>.annotations.json` per §C5 design.

### Click-to-zoom

`ImageViewerForm` exists but does not support click-on-region or annotation. Today: click a thumbnail → opens the image in a separate window with prev/next nav.

### What the UI does NOT support today

- Click a region of a screenshot and add a comment.
- Drawing overlays on top of a baseline / candidate / diff.
- Side-by-side scrolling sync between baseline + candidate (each is its own PictureBox).
- Diff-region zoom (jump to the largest contiguous diff region) — fully manual.

---

## Open questions / STATUS:unresolved

1. **STATUS: unresolved — does the CDP layer's `Console` + `Network` capture cost matter?** CDP events are pushed; enabling all four domains (Page + Runtime + Console + Network) per page session adds some bandwidth but is documented as acceptable for Playwright-class loads. Confirmed by Playwright + Cypress doing it always. Should be safe. Validate in Phase C1 design.
2. **STATUS: unresolved — does Rhino expose a programmatic console-line sink?** `RhinoApp.WriteLine` is one-way (to Rhino's console). The agent would need to either subscribe via a Rhino SDK callback (likely `Rhino.Commands.Command.BeginCommand` / `EndCommand` events) or monkey-patch `RhinoApp.CommandLineOut`. Out-of-scope for this audit; flag for §C1 design.
3. **STATUS: unresolved — what's the right wire format for streamed telemetry?** NDJSON per project §0.3 default seems right but the existing pipe protocol is line-delimited JSON-RPC (which is essentially NDJSON already). May reuse.
4. **STATUS: unresolved — does the UI need to read named-pipe events from a running CLI?** If the CLI keeps the in-process TestRunner (option from A5), the answer is no: there's only one process. If the CLI must orchestrate while the UI displays, they need to share a pipe. Phase C decides.
5. **STATUS: unresolved — CLI exit code regression vs. `spec/PHASES.md` Phase 4 spec.** Today `RunCommand.RunAsync` returns void; the CLI exits 0 even on test failures. Worth flagging as a bug-class finding separate from this audit — it'd need a `docs/bugs/NNNN-cli-exit-code.md` file under STANDARD.md §15.

---

## Inputs surfaced for Phase C

- `STANDARD.md` §16 locked rule 8 + the "Run-time operator visibility — Canary UI is the canonical surface" section already author the desired end-state for §C3.
- The `ITestProgressEvents` interface is the right place to extend for the live console / network / click panel — its current methods cover test/checkpoint/screenshot/VLM lifecycle but not the universal telemetry stream. Add `OnConsoleMessage`, `OnNetworkEvent`, `OnInputDispatched` methods or a generic `OnTelemetry(envelope)`.
- The `ProgressFeedPanel` already has a card-flow pattern that can be extended to a "telemetry feed" beside / under the per-checkpoint cards.
- `ResultsHistory` is the natural place to land per-run timestamping (currently overwrites; would become append).
- The two `ViteManager` copies are the natural extraction point for a shared `Canary.Core.LocalhostManager` per §C7.
