# PHASES_UI.md — Canary Build Phases 8–13 (GUI + Refactor + CPig Workload)

## Phase 8: Rhino .rhp Fix + Canary.Core Extraction
**Goal:** Fix the Rhino plugin to output `.rhp`, and extract all shared logic from `Canary.Harness` into a new `Canary.Core` library so both the CLI and GUI can share it.

### Checkpoint 8.1: Rhino Plugin .rhp Fix
- Modify `src/Canary.Agent.Rhino/Canary.Agent.Rhino.csproj`:
  - Add `<TargetExt>.rhp</TargetExt>`
  - Add `<UseWindowsForms>true</UseWindowsForms>`
  - Add `<ImportWindowsDesktopTargets>true</ImportWindowsDesktopTargets>`
- Create `src/Canary.Agent.Rhino/Properties/AssemblyInfo.cs`:
  - `[assembly: Guid("...")]` with a unique GUID
  - `[assembly: PlugInDescription(...)]` attributes for all required fields
- Verify: `dotnet build` produces `Canary.Agent.Rhino.rhp` (not `.dll`)

### Checkpoint 8.2: Create Canary.Core Project
- Create `src/Canary.Core/Canary.Core.csproj` — `net8.0-windows`, `TreatWarningsAsErrors`
- NuGet: `SixLabors.ImageSharp`, `SixLabors.ImageSharp.Drawing`
- ProjectReference: `Canary.Agent`
- Add to `Canary.sln` under `src` solution folder
- Verify: empty project builds with 0 errors, 0 warnings

### Checkpoint 8.3: Move Comparison Engine to Core
- Move from `src/Canary.Harness/Comparison/` to `src/Canary.Core/Comparison/`:
  - `PixelDiffComparer.cs`, `SsimComparer.cs`, `ComparisonResult.cs`, `CheckpointComparison.cs`, `CompositeBuilder.cs`
- Namespace: `Canary.Comparison` → `Canary.Core.Comparison`
- Update `Canary.Harness.csproj`: add ProjectReference to Core, remove ImageSharp NuGets
- Update `Canary.Tests.csproj`: add ProjectReference to Core
- Update `using` statements in Harness and Test files
- Verify: `dotnet build` passes, all 52 tests pass

### Checkpoint 8.4: Move Config, Models, Input, Reporting, Orchestration to Core
- Move `TestDefinition.cs`, `WorkloadConfig.cs` → `Canary.Core.Config`
- Move `TestResult.cs` (TestStatus, CheckpointResult, TestResult, SuiteResult) → `Canary.Core.Orchestration`
- Move `HtmlReportGenerator.cs`, `JUnitReportGenerator.cs` → `Canary.Core.Reporting`
- Move `ProcessManager.cs`, `AppLauncher.cs`, `Watchdog.cs` → `Canary.Core.Orchestration`
- Move `InputEvent.cs`, `InputRecording.cs`, `InputRecorder.cs`, `InputReplayer.cs`, `ViewportLocator.cs` → `Canary.Core.Input`
- Update all namespace references in Harness CLI files and test files
- Verify: `dotnet build` passes, all 52 tests pass

### Checkpoint 8.5: Decouple TestRunner via ITestLogger
- Create `src/Canary.Core/ITestLogger.cs` with `ITestLogger` interface (Log, LogStatus, Verbose)
- Create `TestStatusLevel` enum (Pass, Fail, Crash, New, Info)
- Move `TestRunner.cs` to `src/Canary.Core/Orchestration/`, inject `ITestLogger` via constructor
- Replace all `Program.Log(...)` calls with `_logger.Log(...)`
- Replace all `Program.LogStatus(...)` calls with `_logger.LogStatus(...)`
- Extract `ApproveTest` → `BaselineManager.cs` in Core
- Extract `DiscoverTestsAsync` → `TestDiscovery.cs` in Core
- Create `src/Canary.Harness/ConsoleTestLogger.cs` implementing `ITestLogger`
- Update `RunCommand.cs` and `ApproveCommand.cs` to use new Core types
- 3 new unit tests

**Phase 8 Exit Criteria:** `.rhp` output works. `Canary.Core` contains all shared logic. Harness is a thin CLI shell. 55 tests pass. 0 errors, 0 warnings.

---

## Phase 9: WinForms Application Shell
**Goal:** A buildable WinForms application with main window layout, navigation, and workload browsing.

### Checkpoint 9.1: Create Canary.UI Project
- Create `src/Canary.UI/Canary.UI.csproj` — `WinExe`, `net8.0-windows`, `UseWindowsForms`
- ProjectReferences: `Canary.Core`, `Canary.Agent`
- Create `Program.cs` with `Application.Run(new MainForm())`
- Add to `Canary.sln` under `src` solution folder
- Verify: project builds, launches empty form

### Checkpoint 9.2: Main Window Layout
- Create `MainForm.cs` with:
  - `ToolStrip` at top: Open Workload Folder, Run Tests, Record, Approve, View Report buttons
  - `SplitContainer` (vertical): left `TreeView` (250px) + right content `Panel`
  - `StatusStrip` at bottom: status label + test count label
- Form title: "Canary — Visual Regression Testing"
- Minimum size: 1024×768

### Checkpoint 9.3: Workload Discovery and Tree Population
- Create `src/Canary.UI/Services/WorkloadExplorer.cs`:
  - `LoadWorkloadsAsync(string workloadsDir)` — returns workload configs + test definitions
- Populate `TreeView` with workload → tests → baselines → results hierarchy
- "Open Workload Folder" button shows `FolderBrowserDialog`
- Create `WelcomePanel.cs` — shown when no test is selected
- Auto-detect `workloads/` subdirectory on startup

### Checkpoint 9.4: UI Tests
- 3 new unit tests for `WorkloadExplorer` (discovery, empty dir, missing tests dir)

**Phase 9 Exit Criteria:** `Canary.UI.exe` launches with tree view and toolbar. Workloads populate. 58 tests pass. 0 errors, 0 warnings.

---

## Phase 10: Results Viewer + Baseline Management
**Goal:** Display test results with side-by-side image comparison and approve/reject baselines.

### Checkpoint 10.1: Results Viewer Control
- Create `ResultsViewerControl.cs` (UserControl):
  - Test header: name, status badge, duration
  - Per-checkpoint row: name, stats (diff%, tolerance, SSIM), three `PictureBox` (baseline, candidate, diff)
  - Scrollable panel for multiple checkpoints

### Checkpoint 10.2: Full-Size Image Viewer
- Create `ImageViewerForm.cs` (modal Form):
  - Full-resolution image display with `AutoScroll`
  - Mouse wheel zoom, click-drag pan
  - Toggle buttons: baseline / candidate / diff
  - Escape closes

### Checkpoint 10.3: Approve/Reject from GUI
- Approve/reject buttons per-checkpoint and per-test
- Extend `BaselineManager` with `ApproveCheckpoint` and `RejectCheckpoint` methods
- Tree view icons update after approve/reject (green/red/yellow)

### Checkpoint 10.4: Test Result Serialization + History
- Add `TestResultSerializer` to Core (save/load TestResult as JSON)
- Create `ResultsHistory.cs` service — scans result directories
- `DataGridView` in results viewer shows run history

### Checkpoint 10.5: Tests
- 5 new unit tests (ApproveCheckpoint, RejectCheckpoint, serializer round-trip, history scan, empty dir)

**Phase 10 Exit Criteria:** Results viewer shows side-by-side images. Approve/reject works. History browsable. 63 tests pass. 0 errors, 0 warnings.

---

## Phase 11: Test Manager — Create, Edit, Run
**Goal:** Full test lifecycle management from the GUI.

### Checkpoint 11.1: Test Definition Editor
- Create `TestEditorControl.cs` (UserControl):
  - Fields: name, workload (combo), description
  - Setup group: file path (browse), viewport (width/height/projection/display), commands list
  - Checkpoints: `DataGridView` (name, atTimeMs, tolerance)
  - Validation via `ErrorProvider`
  - Save writes JSON to `workloads/{name}/tests/{test}.json`

### Checkpoint 11.2: Workload Configuration Editor
- Create `WorkloadEditorControl.cs` (UserControl):
  - Fields for all `WorkloadConfig` properties
  - Browse for app executable, combo for agent type
  - Save writes `workload.json`

### Checkpoint 11.3: Test Runner with Live Progress
- Create `TestRunnerPanel.cs` (UserControl):
  - Progress bar, per-test status list, live log lines
  - Stop button
- Create `GuiTestLogger.cs` implementing `ITestLogger`:
  - Fires events, marshalled to UI thread via `Control.BeginInvoke`
- Tests run on background thread via `Task.Run`

### Checkpoint 11.4: Recording UI
- Create `RecordingPanel.cs` (UserControl):
  - Workload combo, target window field, start/stop buttons, live event counter
  - Wires to `InputRecorder` from Core
  - Save dialog writes `.input.json`

### Checkpoint 11.5: Tests
- 6 new unit tests (editor validation, save round-trip, GuiTestLogger events)

**Phase 11 Exit Criteria:** Tests can be created, edited, and run from GUI. Recording captures input. 69 tests pass. 0 errors, 0 warnings.

---

## Phase 12: Polish + Integration
**Goal:** Keyboard shortcuts, context menus, drag-drop, spec updates, final regression.

### Checkpoint 12.1: Keyboard Shortcuts
- Ctrl+O (open folder), Ctrl+R / F5 (run), Ctrl+A (approve), Ctrl+Shift+R (record), Escape (stop/close), Delete (delete test with confirmation)
- Implement via `MainForm.KeyPreview` and `KeyDown` handler

### Checkpoint 12.2: Drag-and-Drop
- Drag `.json` test file onto tree to import
- Drag `.3dm` file onto workload node to create test with that setup file
- `TreeView.AllowDrop = true`, `DragEnter`/`DragDrop` events

### Checkpoint 12.3: Context Menus
- Right-click workload: Run All Tests, Edit Config, Open in Explorer
- Right-click test: Run, Edit, Approve, Delete, Open in Explorer
- Right-click baseline/result: View, Replace, Delete
- `ContextMenuStrip` dynamically populated by node type

### Checkpoint 12.4: Update Spec Documents
- Update `spec/SUPERVISOR.md` with Phase 8–12 gate checklists + dependency matrix
- Update `spec/ARCHITECTURE.md` with Core + UI in architecture
- Update `AGENTS.md` with new namespaces and current phase

### Checkpoint 12.5: Final Regression
- Verify CLI still works after Core extraction
- Verify GUI launches and all controls functional
- 3 new tests (CLI --help, RunCommand uses Core, MainForm launches)
- Full regression: 72+ tests, 0 warnings

**Phase 12 Exit Criteria:** Shortcuts, context menus, drag-drop all work. Specs updated. Both CLI and GUI functional. 72+ tests pass. 0 errors, 0 warnings.

---

## Phase 13: CPig Regression Workload
**Goal:** Drive CPig's Slop JSON test definitions through the existing `rhino` workload, with crash-tolerant assertion + visual regression. See `spec/CPIG_WORKLOAD.md` for fixture layout and conventions.

### Checkpoint 13.1: New agent actions
- Add to `Canary.Agent.Rhino/RhinoAgent.cs`: `GrasshopperSetToggle`, `GrasshopperSetPanelText`, `GrasshopperGetPanelText`. Each mirrors `GrasshopperSetSlider`: case-insensitive nickname lookup, mutate, `ExpireSolution(true)`, marshal via `InvokeOnUi`.
- Unit tests in `tests/Canary.Tests/` (Category="Unit"): verify each action finds the right object and updates state.
- **Status:** Landed 2026-04-26. Build clean, 0 warnings.

### Checkpoint 13.2: Test runner extensions
- Extend test JSON schema: `actions[]` and `asserts[]` arrays (see `spec/CPIG_WORKLOAD.md` for shape).
- `actions[]` runs sequentially before checkpoint capture.
- `asserts[]` runs after each checkpoint.
- Implement in `Canary.Core/Orchestration/TestRunner.cs`. Deserialization classes in `Canary.Core/Config/TestDefinition.cs` (`TestAction`, `TestAssert`).
- 3 new assert kinds: `PanelEquals`, `PanelContains`, `PanelDoesNotContain`. Each calls `GrasshopperGetPanelText` then string-compares. Unknown types fail with typo-hint message.
- **Status:** Landed 2026-04-26. `TestRunner.RunTestAsync` executes `actions[]` before checkpoints, evaluates `asserts[]` after each checkpoint via `EvaluateClientAssertAsync`. Build clean, 0 warnings.

### Checkpoint 13.3: Loader fixture
- Build `workloads/rhino/fixtures/cpig_slop_loader.gh` with a Slop component, `JsonPath` panel, `Build` toggle, Crash Guard, Log Hub, three output panels (`SlopLog`, `SlopSuccess`, `SlopCount`).
- Set deterministic viewport projection + display mode at the document level.
- Save with both Rhino and Grasshopper closed cleanly.
- Verify it loads + builds the smoke test (`16_field_evaluate.json`) end-to-end manually.
- **Status:** Landed 2026-04-26. Fixture file is 21KB. Also includes `cpig_slop_loader_generator.json` template.

### Checkpoint 13.4: Bulk-generate test JSONs
- Helper script `scripts/cpig-test-from-slop.ps1`: reads a Slop JSON file path, emits a matching `cpig-NN-slug.json` test definition under `workloads/rhino/tests/`.
- Run for all 17 Slop tests in `CPig/research/slop_tests/`.
- First run records candidates only (no baselines); review, approve manually.
- **Status:** Landed 2026-04-26. Script is fully implemented and idempotent. All 17 test JSONs generated and committed.

### Checkpoint 13.5: Initial baselines
- Run `canary run --workload rhino --filter "cpig-*"` end-to-end.
- Inspect every candidate PNG. Approve those that match expected geometry.
- Commit baselines to `workloads/rhino/results/<test>/baselines/`.
- Update `CHANGELOG.md` and `spec/CPIG_WORKLOAD.md`.

**Phase 13 Exit Criteria:** All 17 cpig-* tests run end-to-end. At least the smoke test (`cpig-00-smoke-ping`) passes pixel diff. The three crash-related tests (`cpig-07-alpha-wrap`, `cpig-09-implicit-advanced`, `cpig-16-field-evaluate`) confirm the Phase A+B CPig mitigations hold — either a pixel-diff pass or a clean error report (Watchdog does NOT fire). 0 errors, 0 warnings.

---

## Phase 14: Workload-tree click-to-view / Full TestEditor / Past Runs viewer
**Goal:** Make the workload tree the operator's primary read+edit surface. Single-click any test → side-panel opens with the test's full definition, inline editing, and a Past Runs tab listing every prior `runs/<timestamp>/result.json` for that test. Right-click context menu now works. The TestEditor surfaces VLM oracle config, setup commands, and the Phase 4.6.F GIF/scrub fields that previously round-tripped silently through the backing POCO.

Prompted by an operator report on 2026-06-02: "right clicking doesn't do anything. but a normal click should open a side panel to view/edit." Plus a corollary: "for vlm i'd be able to change prompt, change behavior for how stuff opens/runs/closes inside a suite" + "i can't see past test results in canary."

### Checkpoint 14.1: Workload-tree single-click side panel + right-click fix
**Status:** Landed 2026-06-02 (commit `a97f9ec`).

Why right-click did nothing: Avalonia's `TreeView` doesn't auto-select on right-click. The context-menu commands (`RunSelection`, `EditSelection`, `ApproveSelection`, `OpenInExplorer`) read `Tree.SelectedNode` and early-out when null. So the menu opened against a stale / null selection and silently no-op'd. **Fix:** `TestsView.OnTreePointerPressed` PointerPressed handler — on right-button press, walk the visual tree to the enclosing `TreeViewItem` and assign its `WorkloadNode` DataContext to `Tree.SelectedNode` before the menu opens.

Why single-click did nothing: `Tree.SelectedNode` was a two-way bound `ObservableProperty` but no observer fired. The right pane (`TestsViewModel.ActiveContent`) only swapped on explicit button/command. **Fix:** `TestsViewModel` subscribes to `Tree.PropertyChanged` in its constructor; `RouteSelectionToActiveContent` dispatches Test nodes → new `TestDetailsViewModel` swap. Other node kinds (Suite, Workload, *Group) fall back to Welcome until their dedicated panels land in a future 14.1b checkpoint. Also `NotifyCanExecuteChanged()` on the right-click commands so future `CanExecute=` attribute additions don't re-plumb.

New files: `ViewModels/TestDetailsViewModel.cs`, `Views/TestDetailsView.axaml(.cs)`. The details view wraps the existing `TestEditorViewModel` so its 3-tab editor renders inline (no `EditorHostWindow` modal). Header row has Run / Approve / Open in Explorer buttons that route back to the parent via callbacks. Save propagates through the existing `TestEditorViewModel.SaveRequested` event.

**Exit:** Single-click a test node → side panel renders with name + action buttons + embedded editor. Right-click works on the right item. Build 0/0; 289/289 Canary.Tests.

### Checkpoint 14.2: TestEditor field coverage (VLM, Setup.Commands, Capture/Scrub)
**Status:** Landed 2026-06-02 (commit `8135d93`).

The editor previously surfaced 9 fields; the backing POCO (`_definition` in `TestEditorViewModel.cs`) preserved everything else through Save (Phase 3 round-trip), but operators couldn't see or change them without editing JSON. 14.2 surfaces:

- `Setup.VlmDescription` (multiline) — the natural-language prompt the VLM reads.
- `Setup.Vlm.Provider` + `Setup.Vlm.Model` (text) — `claude` / `ollama` etc. Optional; empty fields round-trip as null.
- `Setup.Commands` (one per line) — pre-checkpoint open / run / close hooks. Rhino macros, Penumbra display-state mutations, etc.
- Per-checkpoint `Capture` (Gif / FrameCount / IntervalMs) + nested `Scrub` (Nickname / Values / SettleMs / SolveTimeoutMs) — the Phase 4.6.F Session B+ GIF + slider-scrub schema. Emits as null when no capture features are active.

Tests: 3 new `TestEditorViewModelRoundTripTests` verify the full kin_15-shaped definition round-trips through Load → BuildDefinition without data loss, including `VlmConfig.MaxTokens` which the editor doesn't surface but the POCO preserves.

**Out of scope for 14.2** (deferred to a Phase 14+ Penumbra-flavoured drop): `Setup.Scene / Backend / Canvas / DisplayPreset` (Penumbra), per-checkpoint `Camera / StabilizeMs` (Penumbra). The Rhino+CPig fields are the immediate operator priority.

**Exit:** A test JSON with `setup.vlmDescription` + `setup.commands` + `capture.scrub` round-trips through the editor unchanged. Canary.Tests 292/292 (was 289).

### Checkpoint 14.3: Past Runs browser
**Status:** Landed 2026-06-02 (commit `db47ec8`).

The `ResultsViewerView` already rendered cards with baseline / candidate / diff / GIF thumbs + Approve / Reject buttons — but only loaded from an in-memory `SuiteResult` (`LoadResult` / `LoadSuiteResult`). It couldn't bootstrap from a `runs/<timestamp>/result.json` on disk. `TestResultSerializer.LoadAsync` knew how to deserialize from a path; `BaselineManager.ApproveCheckpoint` already supported past-run `suiteName=<timestamp>` paths. The missing piece was the UI entry point.

`TestDetailsView` now wraps the editor + a Past Runs tab in a TabControl. The Past Runs tab embeds:
- A `DataGrid` of past run rows (Started, Status, Duration, Run directory) from new `Services/PastRunsScanner.cs`.
- Below: the existing `ResultsViewerView` bound to a nested `ResultsViewerViewModel`. Selecting a row populates it via the new `ResultsViewerViewModel.LoadFromPathAsync(resultJsonPath)`.

When a row is selected, the viewer's context is set with `suiteName=<timestamp>` so Approve / Reject writes to `runs/<ts>/baselines/<checkpoint>.png` rather than the test's top-level `baselines/` folder — `BaselineManager` already supports this path; 14.3 just wires the UI.

Tests: 3 new `PastRunsScannerTests` cover empty / sorted / orphan-tolerant scenarios. Canary.Tests 295/295 (was 292).

**Exit:** Click a test's Past Runs tab → list of every prior run, newest first. Click a row → ResultsViewerView shows that run's checkpoints + artifacts. Approve / Reject targets the correct timestamp directory.

### Checkpoint 14.4: Document the phase + carry-over
**Status:** Landed 2026-06-02 (this entry).

This Phase 14 section. Plus a `MultiVerse/BUILD_LOG.md` cross-repo one-liner per `STANDARD.md §15`, and a new auto-memory pointer (`canary_ui_side_panel.md`) so future agents know the tree-click side panel is the canonical edit/view surface and don't re-add the modal editor flow.

**Out of scope (named for follow-up Phase 14+ work):**
- SuiteDetailsView / WorkloadDetailsView inline panels (single-clicking Suite / Workload nodes still falls back to Welcome; right-click → Edit opens the modal flow for those kinds until a 14.1b ships).
- Penumbra-specific Setup / Checkpoint fields (Scene, Backend, Canvas, DisplayPreset, Camera, StabilizeMs).
- A "Run all from a past timestamp" multi-test rollup view (the Past Runs viewer is per-test today).

**Phase 14 Exit Criteria:** Single-click any test → side panel renders the editor + Past Runs tab. Right-click works on the right item. Editor surfaces VLM + setup commands + capture/scrub. Past Runs lists every prior `runs/<ts>/` with status / duration; clicking a row loads it into the same ResultsViewerView. 295/295 Canary.Tests pass. Build 0/0 on Canary.sln.

### Checkpoint 14.5: ResultsViewer polish (feedback, snapshot, image-open, error info)
**Status:** Landed 2026-06-02.

Operator-reported after 14.3 dogfooding: (a) "approve all doesnt seem to do anything, theres no feedback on my screen. neither is there for approve"; (b) requested a "save this run as-is, neither approve nor reject" action; (c) "Cant open the images / candidates / baselines or get more detailed info from the test result page." 14.5 closes all three.

- **Toast banner** at the top of the ResultsViewerView. Set by every Approve / Reject / Approve All / Save Snapshot via a new `Toast(msg, success)` helper on the VM. Green for success, red for error. Replaces the easy-to-miss bottom StatusText footer as the primary feedback channel. (Footer stays as a second affordance.)
- **Per-card resolution label.** When the operator clicks ✓ Approve or ✗ Reject on a card, the buttons hide and a "✓ Approved" / "✗ Rejected" label appears in their place in the matching colour. The whole card stays visible (it doesn't disappear), so the operator can still see what they just resolved.
- **💾 Save Snapshot button** on the ResultsViewer header. Mirrors the runner toolbar's existing Save Snapshot but works against any loaded run (fresh or past). Copies `candidates/`, `manual-captures/`, `logs/`, and `*.json` into `<testDir>/archived/<timestamp>/`. Detects past-run loads via `ActiveSuiteName` looking like a timestamp directory (`yyyyMMdd-HHmmss-…`) and pulls from `runs/<ts>/` in that case; otherwise from the test's top-level state.
- **Clickable thumbnails.** Each baseline / candidate / diff thumb is wrapped in a transparent Button bound to `OpenImageCommand` (Process.Start on the PNG path, opens in the OS default image viewer). Tooltip on hover surfaces the full path. The `OpenImageInExplorerCommand` is also wired (no UI affordance yet — reserved for a future right-click menu).
- **Path label under each thumb.** SelectableTextBlock with ellipsis trimming; the operator can copy the path with normal text selection. Hidden when the path is null (e.g. no diff image for passing checkpoints).
- **Per-checkpoint error message.** `CheckpointResult.ErrorMessage` (previously only surfaced at the test level via the existing top error banner) now renders on each card in a dark-red box. The "No baseline exists" / "Agent did not respond" / "diff > tolerance" messages now appear where the operator is already looking.

Files: `ViewModels/ResultsViewerViewModel.cs` (Toast helper, ResolutionLabel/Color on the card VM, SaveSnapshotCommand + LooksLikeTimestampDir + CopyDirectoryRecursive helpers, OpenImage / OpenImageInExplorer commands, ErrorMessage on BuildCard). `Views/ResultsViewerView.axaml` (toast row, header buttons, per-card layout overhaul).

**Exit:** Click Approve / Reject / Approve All / Save Snapshot → toast banner shows the result. Per-card state visibly changes. Clicking any thumb opens the PNG. Each path is visible + selectable below its thumb. ErrorMessage shows when non-empty. Build 0/0; 295/295 Canary.Tests.

### Checkpoint 14.6: Past Runs + snapshots unified surface, "View Latest Run" header button
**Status:** Landed 2026-06-02.

Operator-reported after 14.5 dogfooding: (a) "if i leave the post run page i can't get back to it" — once `ActiveContent` swapped away from the `ResultsViewer` (e.g. by clicking another tree node), there was no obvious path back to a freshly-finished run's results; (b) "when i hit the snapshot button [...] there is nothing logged in the past runs details of those tests" — `SaveSnapshot` writes to `<testDir>/archived/<stamp>/`, but `PastRunsScanner` only enumerated `<testDir>/runs/*`, so snapshots were invisible in the UI.

- **`PastRunsScanner` enumerates `archived/` too.** Refactored into two `ScanKindAsync` passes (`runs/` → `RowKind.Run`, `archived/` → `RowKind.Snapshot`), merged + sorted newest-first by timestamp directory name. Snapshots from `TestRunnerViewModel.SaveSnapshot` (no `result.json`) show Status="(snapshot)" + the dir mtime; snapshots from `ResultsViewerViewModel.SaveSnapshot` of a past-run load (which DOES copy the source's `result.json`) read Status / Duration from that file like a regular run. New `Kind` column in `PastRunsView` DataGrid: "Run" / "Snapshot".

- **Auto-select newest row on context load.** `PastRunsViewModel.ReloadAsync` now sets `SelectedRun = Rows.FirstOrDefault()` after loading. Side effect: clicking a freshly-run test in the tree → side panel opens → flipping to Past Runs tab loads the latest run's cards immediately (no extra grid click needed).

- **"🔁 View Latest Run" header button on `TestDetailsView`.** New `TestDetailsViewModel.ViewLatestRunCommand` sets `ActiveTabIndex = 1` (Past Runs tab) and triggers a `PastRunsViewModel.ReloadAsync` refresh. Combined with the auto-select above, this is a one-click path back to the post-run results after navigating away — addressing the "can't get back" complaint. TabControl gained a two-way binding on `SelectedIndex` to support the programmatic switch.

Tests: 1 new `ScanAsync_includes_archived_snapshots_with_Snapshot_kind` covers mixed runs/ + archived/ enumeration, status parsing for snapshots-with-result.json and snapshots-without, and the newest-first ordering across both kinds. Canary.Tests 296/296 (was 295).

**Exit:** Save Snapshot from the ResultsViewer → snapshot appears in that test's Past Runs tab. "🔁 View Latest Run" jumps to Past Runs + loads the newest row in one click. Every executed run's `runs/<ts>/result.json` is visible via the same DataGrid alongside snapshots, with a Kind column to distinguish them. Build 0/0; 296/296 Canary.Tests.

### Checkpoint 14.7: Snapshot data, Override slot, filetype labels, per-checkpoint viewport
**Status:** Landed 2026-06-02.

Operator-reported after 14.6 dogfooding:
1. **"Look at a past snapshot — i can't actually see any data, no image, no notes."** Both `SaveSnapshot` paths (TestRunner toolbar + ResultsViewer header) copy `candidates/`, `manual-captures/`, `logs/`, and top-level `*.json` from the test directory, but the recently-finished run's `result.json` lives at `testDir/runs/<latest>/result.json`, *not* at `testDir/result.json`. Result: archived snapshot dirs had bitmaps + logs but no `result.json` → `ResultsViewerViewModel.LoadFromPathAsync` (introduced in 14.3) failed to deserialise → the card grid stayed empty.
2. **"Write override snapshot, override the most recent. Wont usually need stacks."** Each Save Snapshot click landed a new `archived/<timestamp>/` dir, accumulating noise for the common "I just want the latest" use case.
3. **"Want the filetype, png. gif somewhere."** GIF candidates (Phase 4.6.F Session B scrub captures) look identical to PNG thumbnails in the card grid until clicked. The operator couldn't tell static from animated at a glance.
4. **"For this next round of tests, i want the 4 views captured. that can be part of a test right? a test takes multiple images?"** Per-test setup viewport was the only viewport knob; the four-views-per-test pattern (Front / Top / Right / Perspective) needed a per-checkpoint override.

- **Snapshot copies `runs/<latest>/result.json`.** Both VM paths gained a helper that finds the lexicographically-newest `runs/<ts>/` and copies its `result.json` into `archived/<slot>/result.json`. Past-run loads already had `result.json` sitting in `sourceBase` (`runs/<ts>/`) via the existing `*.json` enumeration; the new code only triggers when `sourceBase == testDir` (fresh in-session run). Result: snapshots now render exactly like the source run — thumbnails, error messages, VLM reasoning, the lot.
- **`📌 Override` button writes `archived/latest/`.** New `OverrideSnapshotCommand` next to each `💾 Save Snapshot` button. Deletes the existing `archived/latest/` dir (best-effort, swallows file-locked exceptions) then snapshots into that fixed slot. The two buttons coexist: 💾 for timestamped "keep this one" snapshots, 📌 for the routine "show me the latest" workflow. `PastRunsScanner.RowKind.Snapshot` already accepts non-timestamp dir names — the existing scanner displays `latest` as-is in the StartedDisplay column.
- **Per-cell filetype label on cards.** `CheckpointCardViewModel` gained three computed properties — `BaselineHeader`, `CandidateHeader`, `DiffHeader` — that render as `"baseline  ·  PNG"` / `"candidate  ·  GIF"` / `"diff  ·  PNG"` derived from the path extension. Empty path → bare label. Visible immediately under the small heading row above each thumb. `GifPath` is now propagated from `CheckpointResult` to the card VM (was previously dropped on the floor).
- **`TestCheckpoint.Viewport` per-checkpoint override.** New `ViewportSetup?` field on `TestCheckpoint`. When non-null, `TestRunner` calls `SetViewport` with the per-checkpoint values + a 250ms settle before capture. Null falls back to the test-level `Setup.Viewport`. Wired into both code paths (`ICanaryAgent` in-process + `HarnessClient` named-pipe). Enables a fixture with 4 checkpoints — `front` / `top` / `right` / `perspective` — to produce 4 cards from a single Grasshopper solve, each independently approvable. JSON-authoring works today via the field; TestEditor exposure is a Phase 14.8+ followup.

Tests: 1 new `TestCheckpoint_Parse_PerCheckpointViewportOverride_RoundTrips` covers the new field. Canary.Tests 297/297 (was 296).

**Exit:** Save Snapshot → snapshot row in Past Runs renders full cards (thumbs + paths + error + VLM). 📌 Override snapshot → only one `archived/latest/` per test, replaced on each click. Each card cell header shows `baseline · PNG` / `candidate · GIF` / `diff · PNG` based on extension. A test definition with 4 viewport-tagged checkpoints captures Front / Top / Right / Perspective from one solve. Build 0/0; 297/297 Canary.Tests.

### Checkpoint 14.8: Custom AnimatedImagePanel — resolve BUG-0006 in-card GIF crash
**Status:** Landed 2026-06-02.

`docs/bugs/0006-resultsviewer-gif-crash-batch-bind.md` documented three back-to-back Canary.UI crashes when navigating to Past Runs for a test whose `result.json` carried a `GifPath`. Stack trace from `dotnet-dump analyze` on `Canary.UI.exe.43820.dmp`:

```
System.ArgumentException: "Unsupported Source object: only Stream, Uri and absolute uri string are supported."
   at Avalonia.Labs.Gif.GifImage.InitializeGif()
   at Avalonia.Labs.Gif.GifImage.OnAttachedToVisualTree(...)
```

Plus a parallel `FileNotFoundException` signature when the `Source` Uri pointed at a file that disappeared. Three XAML-side defenses (`c79b9c1` raw bind, `092f9e7` `IsVisible` opt-in, `661a77f` null-Source-until-Play) all crashed; the labs decoder fires on `Source` assignment regardless of visibility and doesn't tolerate null or missing files.

- **New `Canary.UI.Avalonia.Controls.AnimatedImagePanel`** UserControl (~210 LOC). Hosts a plain `Avalonia.Controls.Image`; decodes GIF frames via `SixLabors.ImageSharp.Image.LoadAsync<Rgba32>` on `Task.Run`; ticks frames via `DispatcherTimer` using per-frame `GifFrameMetadata.FrameDelay` (centiseconds → ms, clamped to 20ms min). Public surface: `SourcePath` (string?), `IsPlaying` (bool), `Stretch`, `StretchDirection`. Null / empty / missing-file SourcePath are no-ops — no exception propagates to the dispatcher. `OnDetachedFromVisualTree` cancels in-flight decode + disposes the frame bitmap list. Rapid SourcePath changes cancel the prior decode via a `CancellationTokenSource`.

- **Avalonia.Labs.Gif package removed** from `Canary.UI.Avalonia.csproj`. `SixLabors.ImageSharp` added as a direct PackageReference (was transitive via `Canary.Core`; the GIF-format extension methods need the direct reference to bind cleanly inside the UI assembly). `Canary.UI.Avalonia/Converters/GifPathToSourceConverter.cs` deleted (no longer needed — the new control accepts a plain string path).

- **Both views updated**: `TestRunnerView.axaml` swaps `<gif:GifImage>` → `<controls:AnimatedImagePanel SourcePath="{Binding GifPath}" IsPlaying="True" />`. `ResultsViewerView.axaml` re-adds the in-card GIF section that was pulled in `4a24e8f`, binding `SourcePath="{Binding GifPath}"` + `IsPlaying="{Binding ShowGif}"`. The `🎞️ Play GIF` toggle and `🌐 Open externally` button are both retained.

- **`CheckpointCardViewModel.GifSource`** computed Uri property removed (was only there to bridge the labs control's typed `Source`). `ShowGif` boolean + `ToggleShowGifCommand` retained — they now drive the new control's `IsPlaying`.

Tests: 5 new `AnimatedImagePanelTests` (null / empty / missing-file SourcePath, rapid-change cancellation, IsPlaying toggle without source). Canary.Tests 302/302 (was 297). Build 0/0.

End-to-end re-verified: ran `cpig-kin-15-watt-straight-line` headless from session; 4 viewports captured + 11-frame scrub + GIF assembled; no new crash dumps in `%LOCALAPPDATA%/CrashDumps/`.

**Exit:** Past Runs navigation no longer crashes Canary.UI. Operator can click `🎞️ Play GIF` on the persp card and the linkage animates in-card. Operator can swap between Past Runs rows freely without crash. Same control used in the TestRunner live-progress card. `Avalonia.Labs.Gif` removed from dependency graph.
