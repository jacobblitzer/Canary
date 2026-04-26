# PHASES_UI.md — Canary Build Phases 8–12 (GUI + Refactor)

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
- Update `CLAUDE.md` with new namespaces and current phase

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
- Implement in `Canary.Harness/TestRunner.cs`. New deserialization classes in `Canary.Core/Models/`.
- 3 new assert kinds: `PanelEquals`, `PanelContains`, `PanelDoesNotContain`. Each calls `GrasshopperGetPanelText` then string-compares.

### Checkpoint 13.3: Loader fixture
- Build `workloads/rhino/fixtures/cpig_slop_loader.gh` with a Slop component, `JsonPath` panel, `Build` toggle, Crash Guard, Log Hub, three output panels (`SlopLog`, `SlopSuccess`, `SlopCount`).
- Set deterministic viewport projection + display mode at the document level.
- Save with both Rhino and Grasshopper closed cleanly.
- Verify it loads + builds the smoke test (`16_field_evaluate.json`) end-to-end manually.

### Checkpoint 13.4: Bulk-generate test JSONs
- Helper script `scripts/cpig-test-from-slop.ps1`: reads a Slop JSON file path, emits a matching `cpig-NN-slug.json` test definition under `workloads/rhino/tests/`.
- Run for all 17 Slop tests in `CPig/research/slop_tests/`.
- First run records candidates only (no baselines); review, approve manually.

### Checkpoint 13.5: Initial baselines
- Run `canary run --workload rhino --filter "cpig-*"` end-to-end.
- Inspect every candidate PNG. Approve those that match expected geometry.
- Commit baselines to `workloads/rhino/results/<test>/baselines/`.
- Update `CHANGELOG.md` and `spec/CPIG_WORKLOAD.md`.

**Phase 13 Exit Criteria:** All 17 cpig-* tests run end-to-end. At least the smoke test (`cpig-00-smoke-ping`) passes pixel diff. The three crash-related tests (`cpig-07-alpha-wrap`, `cpig-09-implicit-advanced`, `cpig-16-field-evaluate`) confirm the Phase A+B CPig mitigations hold — either a pixel-diff pass or a clean error report (Watchdog does NOT fire). 0 errors, 0 warnings.
