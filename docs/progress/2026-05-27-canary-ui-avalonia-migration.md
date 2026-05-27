---
date: 2026-05-27
tags: [progress, ui, avalonia, migration, canary]
status: in-progress
project: canary
component: ui
---

# Canary.UI Avalonia migration — progress log

Per-phase implementation log for the migration from WinForms to Avalonia 11. Parent: [`docs/features/canary-ui-avalonia.md`](../features/canary-ui-avalonia.md). Driving plan: [`docs/plans/2026-05-27-canary-ui-avalonia-migration.md`](../plans/2026-05-27-canary-ui-avalonia-migration.md). Implementation prompt: `C:/Repos/MultiVerse/prompts/canary-ui-avalonia-implement-2026-05-27.md`.

Snapshot tag: `pre-impl-ui-avalonia-2026-05-27` (preserved through Phase 6).

## Phase 0 — spike (2026-05-27)

### Pre-flight

- `git status` clean (modulo `.claude/settings.local.json`, the auto-allowlist).
- `git fetch origin` — 12 commits ahead, 0 behind. No external claims in `WORKING_ON.md`.
- `dotnet build Canary.sln` — 0 warnings, 0 errors. Baseline.
- `dotnet test --filter Category=Unit` — 258 passing. Baseline.
- `git tag pre-impl-ui-avalonia-2026-05-27` created at HEAD.

### Goals

Validate the migration thesis on the Sessions panel — the most layout-pained surface (clipped "Capture with note" button, overlapping Live/Past tab strip, status / hotkey label overlap). If Avalonia reflows it correctly, the rest of the migration is mechanical.

### What landed

**Commit 1 — `feat(ui-avalonia): Phase 0 spike — Sessions panel in Avalonia`:**
- `src/Canary.UI.Avalonia/Canary.UI.Avalonia.csproj` — Avalonia 11.2.5 + FluentAvaloniaUI 2.2.0 + CommunityToolkit.Mvvm 8.3.2.
- `Program.cs` + `App.axaml` + `App.axaml.cs` — Avalonia AppBuilder + FluentAvalonia dark theme + single-instance mutex (separate name from WinForms so both can run in parallel).
- `Views/MainWindow.axaml` + ViewModel — FluentAvalonia `NavigationView` shell with the Sessions item only.
- `Views/SessionsView` + sub-views `SessionsLiveView` + `SessionsPastView` — `TabControl` with the two sub-tabs.
- `ViewModels/SessionsLiveViewModel.cs` — state machine `Idle/Starting/Armed/Ending` with `[RelayCommand(CanExecute)]`. The four button gates fall out of `CanStart` / `CanCapture` / `CanEnd` predicates — no manual `Enabled = false` like WinForms.
- `ViewModels/SessionsPastViewModel.cs` — `DataGrid` + filter + Refresh + report preview.
- `Controls/AnnotationCanvas.cs` — Avalonia `Canvas` + `Shape` port of the WPF island. Pointer events instead of MouseDown/Move/Up; same four tool modes; same `annotations.json` v1 shape.
- `Views/AnnotateWindow` + `TextInputWindow` + `NotePromptWindow` + `CloseoutPromptWindow` — all use `SizeToContent=WidthAndHeight` (no hardcoded pixel positions).
- `Hotkeys/SessionHotkeyHook.cs` — Win32 `RegisterHotKey` against `TopLevel.TryGetPlatformHandle().Handle`. Comctl32 `SetWindowSubclass` intercepts `WM_HOTKEY` (Avalonia doesn't expose a WndProc message filter on Windows).
- `Services/SingleInstancePipeServer.cs` — copied verbatim from `src/Canary.UI/SingleInstancePipeServer.cs`. Same pipe name + protocol.
- `Canary.sln` updated to include the new project.

**Commit 2 — `test(ui-avalonia): SessionsLive + Past ViewModel tests`:**
- `tests/Canary.Tests/UI.Avalonia/SessionsLiveViewModelTests.cs` — 9 tests covering state-machine transitions, command CanExecute gating, workload-filter logic, note-prompt delegate behavior. Uses the existing `StubFactory` + `StubAgent` pattern from `SupervisedSessionTests`.
- `tests/Canary.Tests/UI.Avalonia/SessionsPastViewModelTests.cs` — 3 tests covering `ScanRows` + filter logic.
- `tests/Canary.Tests/Canary.Tests.csproj` — adds `Canary.UI.Avalonia` project reference.

**Commit 3 — `docs(features): canary-ui-avalonia feature + Phase 0 progress`:**
- `docs/features/canary-ui-avalonia.md` — new feature doc (status: in-progress).
- `docs/progress/2026-05-27-canary-ui-avalonia-migration.md` — this file.
- `CHANGELOG.md` — Unreleased entry.
- `BUILD_LOG.md` — Phase 0 entry prepended.

### Verification gates

1. **Build 0/0 (both exes green)**: ✅ `dotnet build Canary.sln` produces both `src/Canary.UI/bin/Debug/net8.0-windows/Canary.UI.exe` and `src/Canary.UI.Avalonia/bin/Debug/net8.0-windows/Canary.UI.Avalonia.exe`, 0 warnings 0 errors.
2. **Unit tests**: ✅ 258 → 270 (+12 net new). All passing.
3. **Manual layout smoke**: pending operator review — see "operator smoke checklist" below.
4. **Functional smoke (Sessions round-trip)**: pending operator review.
5. **CLI regression smoke**: pending — `canary run --workload qualia --test eager-l3-reload-smoke --headless` not affected by this work (CLI path untouched).
6. **Decision gate**: pending the manual smokes above.

### Operator smoke checklist

To complete Phase 0 verification:

1. Launch `Canary.UI.Avalonia.exe` from `src/Canary.UI.Avalonia/bin/Debug/net8.0-windows/`. Resize the window from 800×600 to 1920×1080 and observe button + tab + status text reflow.
2. Launch `Canary.UI.exe` (WinForms) side-by-side, pick a workload, observe the layout pain that motivated this migration.
3. Run the supervised-session end-to-end in the Avalonia build: pick qualia → Start → wait for Vite + Chrome → Capture → Ctrl+Shift+C → Capture + Annotate → annotate → Save → see annotated thumbnail → End → close-out → switch to Past tab → confirm the just-finished session appears + report preview renders.
4. Run `canary run --workload qualia --test eager-l3-reload-smoke --headless` and confirm the verdict + telemetry match the WinForms baseline (CLI path is untouched but verify).

If all four pass: ✅ Phase 0 green. If layout still pains: course-correct (different layout primitive, theme variant) within Phase 0. If Avalonia genuinely can't model something needed: ABANDON migration + take defensive WinForms cleanup path.

### Next phase

Phase 1 — shell + simple panels. Localhost, Feedback, Telemetry, Settings + the full NavigationView toolbar with Tests-only visibility bindings.

### Commits

- `768d259` — `feat(ui-avalonia): Phase 0 spike — Sessions panel in Avalonia`
- `21ca293` — `test(ui-avalonia): SessionsLive + Past ViewModel tests`
- `190718e` — `docs: canary-ui-avalonia feature + Phase 0 progress + CHANGELOG + BUILD_LOG`

## Phase 1 — shell + simple panels (2026-05-27)

### Pre-flight

- `git status` clean (modulo `.claude/settings.local.json`).
- Phase 0 verified by operator (layout reflow + functional smoke confirmed).
- Baseline: 270 unit tests passing, build 0/0.

### Goals

Stand up the full nav shell + port the four read-only panels (Localhost / Feedback / Telemetry / Settings). Mechanical AXAML conversions — no new architectural decisions.

### What landed

**Commit 1 — `feat(ui-avalonia): port LocalhostView to Avalonia`** (52ad6f8):
- `Views/LocalhostView.axaml` + `.cs` + `ViewModels/LocalhostViewModel.cs`.
- DataGrid-backed port-entry list with Refresh + Kill commands; Tier 3 toggle persisted to `CanarySettings`. Polling driven by `DispatcherTimer` (start on `AttachedToVisualTree`, stop on detach — no more `VisibleChanged` hack).

**Commit 2 — `feat(ui-avalonia): port FeedbackView to Avalonia`** (77cb8f7):
- `Views/FeedbackView.axaml` + `.cs` + `ViewModels/FeedbackViewModel.cs`.
- TreeView with three buckets (inbox / triaged / resolved) populated from `docs/feedback/<bucket>/*.md`. `OpenInboxFolder` command launches the shell.

**Commit 3 — `feat(ui-avalonia): port TelemetryView to Avalonia`** (7221a50):
- `Views/TelemetryView.axaml` + `.cs` + `ViewModels/TelemetryViewModel.cs`.
- DataGrid tailing the most recent `telemetry.ndjson` under `workloads/<w>/results/`. Source filter combo + 2s polling.

**Commit 4 — `feat(ui-avalonia): port SettingsView to Avalonia`** (896f34f):
- `Views/SettingsView.axaml` + `.cs` + `ViewModels/SettingsViewModel.cs`.
- UI mode RadioButton pair + Tier3 CheckBox + RetentionDays NumericUpDown, all bound TwoWay to `CanarySettings`. Each change persists immediately.

**Commit 5 — `feat(ui-avalonia): full NavigationView shell + Open Folder toolbar`** (e56aada):
- `MainWindow.axaml` populates `NavigationView.MenuItemsSource` with all five Phase 1 nav items.
- Top toolbar row (Grid Row 0) with **Open workloads folder…** button + workloads-dir indicator. The picker uses Avalonia's `StorageProvider.OpenFolderPickerAsync`.
- `ContentControl.DataTemplates` maps each VM type to its View so `SelectedNavItem.ViewModel` renders automatically.
- `ApplyWorkloadsDir` routes the picked dir into Sessions + Telemetry VMs.

**Commit 6 — `test(ui-avalonia): Phase 1 panel ViewModel tests`** (pending):
- 17 new tests across `LocalhostViewModelTests` (4), `FeedbackViewModelTests` (4), `TelemetryViewModelTests` (3), `SettingsViewModelTests` (6).
- The TelemetryViewModelTests `SourceFilter_NarrowsRowsToSelectedSource` test caught a real bug — the VM's Refresh path short-circuited on unchanged file mtime even when the source filter changed. Fix: invalidate `_currentFile` / `_lastSeenWriteUtc` in `OnSelectedSourceChanged`.

**Commit 7 — `docs(progress): Phase 1`** (pending): this section + CHANGELOG + BUILD_LOG.

### Verification gates

1. ✅ `dotnet build Canary.sln` — 0 warnings, 0 errors. Both `Canary.UI.exe` and `Canary.UI.Avalonia.exe` build.
2. ✅ Unit tests — 270 → 287 (+17 new), all passing.
3. ⏸ **Manual panel-render smoke** — pending operator. Launch the Avalonia exe, click each nav item, confirm each panel renders cleanly on first activation. Click **Open workloads folder…**, pick `C:\Repos\Canary\workloads`, confirm Sessions + Telemetry pick up the new dir.
4. ⏸ **Toolbar visibility smoke** — Open Folder stays visible everywhere. Tests-only buttons defer to Phase 2.
5. ⏸ **CLI regression smoke** — pending; CLI path unaffected.

### Next phase

Phase 2 — Tests tab (~4 days). Workload tree (workloads / suites / tests / recordings hierarchical view), TestRunnerView (live log + per-test status grid + abort hotkey), ResultsViewerView (approve/reject flows), RecordingView. The TestRunnerViewModel is the most stateful — porting `MainForm.OnRunTests` + `TestRunnerPanel.RunAsync` carefully.

### Commits

- `52ad6f8` — `feat(ui-avalonia): port LocalhostView to Avalonia`
- `77cb8f7` — `feat(ui-avalonia): port FeedbackView to Avalonia`
- `7221a50` — `feat(ui-avalonia): port TelemetryView to Avalonia`
- `896f34f` — `feat(ui-avalonia): port SettingsView to Avalonia`
- `e56aada` — `feat(ui-avalonia): full NavigationView shell + Open Folder toolbar`
- `b4d1972` — `test(ui-avalonia): Phase 1 panel ViewModel tests (+TelemetryVM fix)`
- `1df807c` — `docs(progress): Phase 1 — shell + simple panels`

## Phase 2 — Tests tab (2026-05-27)

### Pre-flight

- Phase 1 verified by operator (continue).
- Baseline: 287 unit tests passing, build 0/0.
- Read existing TestRunnerPanel (431 lines), ResultsViewerControl (804 lines), RecordingPanel (446 lines), WelcomePanel (67 lines), MainForm tree-loading code.

### Goals

Port the Tests tab — workload tree + content swap into Welcome / TestRunner / ResultsViewer / Recording. This is "the meat" — most operator workflow lives here.

### What landed

**Commit 1 — `feat(ui-avalonia): port WorkloadTree + Welcome`**: `WorkloadExplorer` (verbatim port), `WorkloadTreeViewModel` (Workload → Suites/Tests/Recordings hierarchy), `WelcomeView` (idle landing pane).

**Commit 2 — `feat(ui-avalonia): port TestRunnerView`**: `AvaloniaTestLogger` (Dispatcher-marshaled ITestLogger), `TestRunnerViewModel` (state machine + RunRequest dispatch on AgentType + ITestProgressEvents implementation), `TestRunnerView` (live log + progress card strip). The orchestrator paths (`RunQualiaAsync` / `RunPenumbraAsync` / `RunSharedSuiteAsync` / `RunSuiteAsync`) port verbatim from the WinForms version.

**Commit 3 — `feat(ui-avalonia): port ResultsViewerView`**: `ResultsViewerViewModel` (`LoadResult` / `LoadSuiteResult` + Approve/Reject/ApproveAll commands wired to `BaselineManager`), `ResultsViewerView` (scrolling list of checkpoint cards with baseline/candidate/diff thumbnails + Approve/Reject buttons). Simpler shape than the 804-line WinForms control — pass-rate bars + expandable test sections deferred to Phase 4 polish.

**Commit 4 — `feat(ui-avalonia): port RecordingView`**: `RecordingViewModel` (Idle/Launching/Recording state machine + Start/Stop commands + DispatcherTimer event-count refresh), `RecordingView` (workload combo + test name + target description + Launch/Stop buttons + log). AbortOverlayForm deferred to Phase 4 polish.

**Commit 5 — `feat(ui-avalonia): TestsView shell + Tests-only toolbar wiring`**: `TestsViewModel` (owns Tree + the four sub-VMs + ActiveContent slot for content swap), `TestsView` (TreeView left + GridSplitter + ContentControl right with per-VM DataTemplates), `MainWindowViewModel` (Tests prepended as first nav item + IsTestsActive + RunSelected/RecordNew commands), `MainWindow.axaml` (Run Tests + Mode picker + Record buttons visible only when IsTestsActive; F5 KeyBinding registers globally).

**Commit 6 — `test(ui-avalonia): Phase 2 VM tests`**: 12 new tests across `WorkloadTreeViewModelTests` (3), `TestRunnerViewModelTests` (4), `ResultsViewerViewModelTests` (5). 287 → 299 total.

**Commit 7 — `docs(progress): Phase 2`**: this section + CHANGELOG + BUILD_LOG.

### Verification gates

1. ✅ `dotnet build Canary.sln` — 0 warnings, 0 errors. Both `Canary.UI.exe` and `Canary.UI.Avalonia.exe` build.
2. ⏸ **End-to-end Run Tests against qualia smoke** — pending operator. Click Tests → expand qualia → All Tests → select `eager-l3-reload-smoke` → Run Tests (or F5). Expect TestRunner pane swap with live log + progress card + final ResultsViewer.
3. ⏸ **Abort hotkey (Pause)** — Pause hotkey wiring lives in Phase 5 (AbortHotkey port). Phase 2's Stop button still works.
4. ⏸ **ResultsViewer Approve/Reject** — covered by unit tests (gate 6) for the disk side; needs operator smoke for the UI side.
5. ⏸ **Drag-and-drop recording** — drag-and-drop wiring is a Phase 5 item; manual recording flow (Record button → workload pick → Launch + Record → Stop & Save) ships in Phase 2.
6. ✅ ViewModel tests — 287 → 299, +12 net new, all passing.
7. ⏸ **CLI regression smoke** — pending; CLI untouched.

### Operator smoke checklist (gates 2 + 3 + 4 + 5)

1. Click the **Tests** nav item; the workload tree loads in the left pane.
2. Expand a workload (e.g. qualia) and double-check the Suites / All Tests / Recordings groups populated.
3. Select a single test → click **Run Tests** (or F5). Expect: ActiveContent switches to TestRunner; status text + progress bar + live log + progress cards populate; on completion ActiveContent switches to ResultsViewer.
4. From ResultsViewer, try **Approve** / **Reject** on a checkpoint card; confirm the candidate→baseline copy (or deletion) happened on disk.
5. Click **Record** in the toolbar; the right pane swaps to RecordingView. Pick a workload, name a test, Launch & Record; the target app should boot + accept input; Stop & Save writes `workloads/<w>/recordings/<test>.input.json` and the tree refreshes.

### Next phase

Phase 3 — editors (~2 days). Port `TestEditorControl`, `SuiteEditorControl`, `WorkloadEditorControl`. Form-heavy, two-way data binding against `Canary.Core` POCOs wrapped in `INotifyPropertyChanged` adapter ViewModels. Bytes-identical edit-and-save round-trip is the bar.

### Commits

- `088b0bd` — `feat(ui-avalonia): port WorkloadTree + Welcome to Avalonia`
- `c12c7d5` — `feat(ui-avalonia): port TestRunnerView (live log + progress feed)`
- `748609c` — `feat(ui-avalonia): port ResultsViewerView (approve / reject flows)`
- `1bb481a` — `feat(ui-avalonia): port RecordingView (input record + save)`
- `fc803f7` — `feat(ui-avalonia): TestsView shell + Tests-only toolbar wiring`
- `c45a501` — `test(ui-avalonia): TestRunner + WorkloadTree + ResultsViewer VM tests`
- `f4bc39f` — `docs(progress): Phase 2 — Tests tab`

## Phase 3 — editors (2026-05-27)

### Pre-flight

- Phase 2 verified by operator (continue).
- Baseline: 299 unit tests passing, build 0/0.
- Read the 811-line TestEditorControl, 167-line SuiteEditorControl, 494-line WorkloadEditorControl + the schema POCOs (`TestDefinition`, `SuiteDefinition`, `WorkloadConfig`).

### Goals

Port the three editors at JSON-round-trip-faithful shape — Load(definition) → BuildDefinition() produces byte-identical JSON to the input. **Unmanaged fields** (Penumbra-specific `Setup.Scene/Canvas/DisplayPreset/Commands`, VLM provider config, per-`TestAction` `Extra` JsonExtensionData) round-trip untouched because the VM mutates the underlying POCO instead of building a fresh one — this is the protection against silent data loss on save.

### What landed

**Commit 1 — `feat(ui-avalonia): port TestEditorView + VM`** (3e74731): `TestEditorViewModel` + `CheckpointRow` + `AssertRow`; tabbed View (Basic / Checkpoints / Actions+Asserts) with DataGrid editing for checkpoints + asserts. Actions remain a raw-JSON TextBox so `TestAction.Extra` JsonExtensionData round-trips.

**Commit 2 — `feat(ui-avalonia): port SuiteEditorView + VM`** (e4995ae): `SuiteEditorViewModel` + `TestPickRow`; scrolling CheckBox list of available tests with `IsSelected` bound TwoWay.

**Commit 3 — `feat(ui-avalonia): port WorkloadEditorView + VM`** (37a0f0a): `WorkloadEditorViewModel` + `SetupCommandRow`; 4-column form layout for related fields, list of `SetupCommands` with per-row remove.

**Commit 4 — `test(ui-avalonia): editor VM tests`** (bbe05d5): 15 new tests covering JSON round-trip idempotence (the core Phase 3 contract), Load/BuildDefinition equivalence, Add/Remove command mutation, Save validation, and — critically — **unmanaged-field preservation** (Penumbra `Scene/Canvas/DisplayPreset/Commands` survive a Load → BuildDefinition cycle even though the editor doesn't surface them).

**Commit 5 — `docs(progress): Phase 3`** (pending): this section + CHANGELOG + BUILD_LOG.

### Verification gates

1. ✅ `dotnet build Canary.sln` — 0 warnings, 0 errors. Both exes build.
2. ✅ Edit-and-save round-trip — covered by `RoundTrip_*_IsIdempotent` unit tests (one per editor) + `UnmanagedFields_RoundTripUntouched` for the Penumbra/VLM/Setup.Commands fields the editor doesn't surface. Manual diff against a WinForms-saved JSON is operator's call.
3. ✅ ViewModel tests — 299 → 314 (+15), all passing.
4. ✅ CLI regression smoke — CLI untouched.

### Wire-in status

Editors are **orphan ViewModels/Views** in Phase 3 — created and tested but not yet routed from the Tests tab. The WinForms shell exposed them via tree-node context menus + double-click; the Avalonia equivalents land in **Phase 5** with `DragDropHandlers` + context-menu wiring. This matches the prompt's phase split (Phase 5 owns "drag-and-drop + tree context menus").

### Next phase

Phase 4 — annotation polish (~2 days). Continue from the Phase 0 `AnnotationCanvas` + `AnnotateWindow` baseline: tighten hit-testing on existing shapes (so the Pointer tool can select / move them), add an undo stack, polish the tool palette + color picker, and make sure the annotate-to-feedback-inbox path has parity with the WPF version.

### Commits

- `3e74731` — `feat(ui-avalonia): port TestEditorView + VM`
- `e4995ae` — `feat(ui-avalonia): port SuiteEditorView + VM`
- `37a0f0a` — `feat(ui-avalonia): port WorkloadEditorView + VM`
- `bbe05d5` — `test(ui-avalonia): editor VM tests (Test / Suite / Workload)`
- (pending) — `docs(progress): Phase 3 — editors`
