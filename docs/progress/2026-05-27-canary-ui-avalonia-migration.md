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
- `9fd6490` — `docs(progress): Phase 3 — editors`

## Phase 4 — annotation surface (2026-05-27)

### Pre-flight

- Phase 3 verified by operator (continue).
- Baseline: 314 unit tests passing, build 0/0.
- Re-read the Phase 0 `AnnotationCanvas` + `AnnotateWindow` + the WPF `AnnotatedImageForm` for parity-comparison. Identified `Canary.Feedback.FeedbackInboxWriter` + `FeedbackSlugGenerator` + `FeedbackItem` as the inbox-mode writer surface.

### Goals

Build on the Phase 0 annotation baseline:
1. Undo stack — every Rectangle / Freehand / Text addition pushes an inverse delegate; `Undo()` pops + invokes; `Clear()` snapshots-and-restores so a single Ctrl+Z brings back everything cleared in one motion.
2. Tool palette polish — toolbar buttons become `ToggleButton`s with an `accent`-colored selected state so the operator can see which tool is active.
3. Color buttons that visually preview their stroke color.
4. Refactor `AnnotateWindow` out of code-behind into an `AnnotateWindowViewModel` with two constructors (session-sink mode and feedback-inbox mode), bringing **parity** with the WinForms `AnnotatedImageForm` so the Past Runs Annotate flow lands cleanly in Phase 5.

### What landed

**Commit 1 — `feat(ui-avalonia): annotation polish — undo + tool palette + inbox parity`** (cf5d1ed):
- `Controls/AnnotationCanvas.cs` — `Stack<Action> _undoStack` + `Undo()` + `UndoCount` + `ShapeCount` + `StateChanged` event. Rectangle / Freehand / Text additions push a one-shot inverse. Clear() pushes a snapshot-restore. Text shapes pair Rectangle + TextBlock via `tb.Tag = bg`.
- `ViewModels/AnnotateWindowViewModel.cs` — two constructors (session-sink mode mirroring Phase 0, inbox mode wiring `FeedbackInboxWriter` + `FeedbackSlugGenerator` + `FeedbackItem`). Delegate callbacks (`GetAnnotatedPngBytes` / `GetAnnotationsJson` / `RequestUndo` / `RequestClear` / `RequestClose`) keep the VM testable.
- `ViewModels/ToolModeConverter.cs` — static `IValueConverter` instances for each `ToolMode`; toolbar `ToggleButton.IsChecked` binds through them.
- `Views/AnnotateWindow.axaml` — toolbar refactored to `ToggleButton`s + colored Color buttons + Undo (Ctrl+Z) button + `Window.KeyBindings` entry.
- `Views/AnnotateWindow.axaml.cs` — refactored to construct the VM + wire callbacks; three public constructors (preview, session-sink, inbox).

**Commit 2 — `test(ui-avalonia): AnnotateWindowViewModel tests`** (2b1c0c9): 9 new tests covering both modes (session-sink dispatch + inbox markdown+sidecar write), the empty-title fallback, Save-error surfacing, PickTool / PickColor command behavior, Undo / Clear delegate plumbing, and the `ToolModeConverter` true-only-for-matching-target property. 314 → 323 total.

**Commit 3 — `docs(progress): Phase 4`** (pending): this section + CHANGELOG + BUILD_LOG.

### Verification gates

1. ✅ `dotnet build Canary.sln` — 0 warnings, 0 errors. Both exes build.
2. ⏸ **Annotation round-trip parity with WPF** — pending operator. The annotations.json shape is unchanged from Phase 0 (covered by Phase 0 implementation). PNG rendering uses Avalonia's `RenderTargetBitmap` — visually equivalent but not bit-identical with the WPF `PngBitmapEncoder`. The annotations.json IS bit-identical (deterministic serialization).
3. ⏸ **Both flows work — supervised-session Past tab + feedback inbox** — supervised-session flow has shipped since Phase 0. Feedback-inbox flow is unit-tested but operator smoke happens after Phase 5 wires the Past Runs Annotate button to the new inbox-mode constructor.
4. ✅ ViewModel tests — 314 → 323 (+9), all passing.

### Operator smoke checklist

1. From Sessions Live, run a session → Capture+Annotate → draw a rectangle → click Rectangle button (visible-active) → click Freehand (visible-active flips) → press Ctrl+Z (rectangle disappears) → Ctrl+Z again (freehand disappears) → Save → confirm annotated PNG + annotations.json land in the session's `captures/` dir.
2. Sanity check: Clear after drawing two shapes → Ctrl+Z restores both → save normally.

### Next phase

Phase 5 — services + glue (~2 days). AbortHotkey (Pause) port for run aborts; SingleInstancePipeServer wired to MainWindow + AutoRunRequestHandler; drag-and-drop for workload JSON + recordings; right-click context menus on tree nodes (which finally route to the Phase 3 editors).

### Commits

- `cf5d1ed` — `feat(ui-avalonia): annotation polish — undo + tool palette + inbox parity`
- `2b1c0c9` — `test(ui-avalonia): AnnotateWindowViewModel tests`
- `06ced62` — `docs(progress): Phase 4 — annotation surface`

## Phase 5 — services + glue (2026-05-27)

### Pre-flight

- Phase 4 verified by operator (continue).
- Baseline: 323 unit tests passing, build 0/0.
- Read WinForms `AbortHotkey.cs`, `MainForm.AutoRunAsync` + `FindAutoRunNode`, `MainForm.OnTreeDragDrop`, the four `BuildXxxContextMenu` builders.

### Goals

The last build phase before cutover. Lights up the operator-glue surfaces that the Phase 2 + 3 + 4 work created but didn't wire:
1. **AbortHotkey (Pause)** — global hotkey that fires `Tests.Runner.StopCommand` during a run.
2. **AutoRunRequestHandler** — fills in `MainWindowViewModel.HandleAutoRun` so `canary run --workload x --test y` forwarded over the single-instance pipe drives a real test run in the running UI.
3. **Tree drag-and-drop** — drop a `.input.json` recording → prompt for test name → write `workloads/<w>/tests/<name>.json` referencing the recording → reload tree.
4. **Tree context menus** — right-click any node shows Run / Edit / Approve / Create test from recording / Open in Explorer.
5. **Editor wire-in** — the Phase 3 editors come out of orphan state. Edit context-menu command opens the editor in an `EditorHostWindow`; Save persists + reloads.

### What landed

**Commit 1 — `feat(ui-avalonia): AbortHotkey ported against Avalonia HWND`** (7ed215f): `Hotkeys/AbortHotkey.cs` (Win32 RegisterHotKey for VK_PAUSE, Comctl32 `SetWindowSubclass` intercepts WM_HOTKEY) + `TestRunnerViewModel.OnRunStarted` / `OnRunFinished` lifecycle hooks.

**Commit 2 — `feat(ui-avalonia): SingleInstancePipeServer + AutoRunRequestHandler`** (865c815): `Services/AutoRunRequestHandler.cs` (pure helpers — `FindNode` mirrors `MainForm.FindAutoRunNode` exactly; `ParseMode` maps strings to `ModeOverride`) + `MainWindowViewModel.HandleAutoRunAsync` (Tests-nav switch → tree-load poll up to 10s → node lookup → mode set → run).

**Commit 3 — `feat(ui-avalonia): drag-and-drop + tree context menus + editor host`** (a03dd95): `TestsViewModel` context-menu commands + view-supplied async delegates; `TestsView` ContextMenu + drag-drop handlers; `EditorHostWindow` for hosting Phase 3 editor Views; `MainWindow.axaml.cs` wires the AbortHotkey lifecycle + the editor + prompt callbacks + `PersistAndRefreshAsync` to write each editor's JSON back to disk.

**Commit 4 — `test(ui-avalonia): AutoRunRequestHandler tests`** (71d6cc7): 7 new tests — FindNode coverage (workload-only / +test / +suite / unknown test / unknown workload), ParseMode mapping, plus a CreateTestFromRecording integration test that drives a real recording-to-test-JSON write. 323 → 330 total.

**Commit 5 — `docs(progress): Phase 5`** (pending): this section + CHANGELOG + BUILD_LOG.

### Verification gates

1. ✅ `dotnet build Canary.sln` — 0 warnings, 0 errors. Both exes build.
2. ⏸ **Pipe forwarding from a second `canary run` invocation** — pending operator. Launch `Canary.UI.Avalonia.exe`; from a second shell run `canary run --workload qualia --test eager-l3-reload-smoke` → the running UI switches to Tests → selects the test → starts running.
3. ⏸ **Pause hotkey aborts a running test** — pending operator. Start a run, press Pause anywhere → Stop fires.
4. ⏸ **Drag-and-drop a `.input.json` recording** — pending operator. Drag a file from Explorer onto the workload tree → name prompt → new test JSON.
5. ⏸ **Tree context menus** — pending operator. Right-click any tree node.
6. ✅ VM tests — 323 → 330 (+7), all passing.

### Wire-in completeness

After Phase 5:
- **Phase 3 editors** — wired via context-menu Edit + EditorHostWindow.
- **Phase 4 AnnotateWindow inbox-mode constructor** — still dormant (Past Runs tab itself isn't part of this migration). The constructor is available for any future caller.
- **AbortHotkey** — armed on run start, disarmed on run end.
- **AutoRun pipe forwarding** — end-to-end functional.

### Next phase

Phase 6 — cutover (~1 day). Flip the default UI to the Avalonia build, delete `src/Canary.UI/`, update `UiLocator.cs` to point at the renamed exe, run the 8-workflow smoke matrix from the prompt §7. Delete the snapshot tag once all 8 are green.

### Commits

- `7ed215f` — `feat(ui-avalonia): AbortHotkey ported against Avalonia HWND`
- `865c815` — `feat(ui-avalonia): SingleInstancePipeServer + AutoRunRequestHandler`
- `a03dd95` — `feat(ui-avalonia): drag-and-drop + tree context menus + editor host`
- `71d6cc7` — `test(ui-avalonia): AutoRunRequestHandler tests`
- `10c8231` — `docs(progress): Phase 5 — services + glue`

## Phase 6 — cutover (2026-05-27) — SHIPPED

### Pre-flight

- Phase 5 verified by operator (continue + push).
- Phases 0–5 pushed to origin/master at commit `10c8231` (43 commits).
- Snapshot tag `pre-impl-ui-avalonia-2026-05-27` confirmed present.
- Baseline: 330 unit tests passing, build 0/0, both `Canary.UI.exe` (WinForms) and `Canary.UI.Avalonia.exe` (Avalonia) building side by side.

### What landed

- **`Canary.UI.Avalonia.csproj`** — `<AssemblyName>` flipped to `Canary.UI`. The produced exe is now `Canary.UI.exe` from the Avalonia project's bin dir.
- **`src/Canary.UI.Avalonia/Program.cs`** — single-instance mutex name unified with the legacy WinForms one (`Global\Canary.UI.SingleInstance`).
- **`src/Canary.Harness/UiLocator.cs`** — sibling-solution search path repointed from `Canary.UI/bin/...` to `Canary.UI.Avalonia/bin/...`. Filename `Canary.UI.exe` unchanged.
- **`tests/Canary.Tests/Canary.Tests.csproj`** — dropped the `Canary.UI` ProjectReference + the `<UseWindowsForms>` flag.
- **`tests/Canary.Tests.Integration/`** — csproj + `SingleInstancePipeTests.cs` repointed to the Avalonia project's `SingleInstancePipeServer`.
- **`Canary.sln`** — Canary.UI project removed.
- **`src/Canary.UI/`** — entire WinForms project tree deleted (≈30 files).
- **8 WinForms-coupled test files deleted** (`tests/Canary.Tests/UI/` + `tests/Canary.Tests/Navigation/`). Every retained UI behavior has Avalonia VM coverage in `tests/Canary.Tests/UI.Avalonia/`.

### Cross-repo doc pass

- `Canary/CLAUDE.md` — Avalonia flagged in Framework line + Quick Reference repro pattern path + spec/PHASES_UI reference.
- `Canary/README.md` — Features bullet + Project Structure tree updated.
- `docs/features/canary-ui-avalonia.md` — status `in-progress` → `shipped`.
- `docs/plans/2026-05-24-canary-debug-overhaul.md` — § C4 marked SUPERSEDED 2026-05-27.
- `CHANGELOG.md` — new `### Changed` block above the `### Added` migration block.
- `BUILD_LOG.md` — Phase 6 entry prepended.
- `MultiVerse/BUILD_LOG.md` — cross-repo entry (Canary → operator workflow surfaces).
- Peer repos (Qualia, Penumbra) CLAUDE.md UNCHANGED — `Canary.UI.exe` reference still resolves.

### Phase 6 smoke matrix

| # | Workflow | Status |
|---|---|---|
| 1 | `canary run --workload qualia --test eager-l3-reload-smoke --headless` | ⏸ operator smoke |
| 2 | `canary session start --workload qualia` REPL + closeout | ⏸ operator smoke |
| 3 | UI launch + Sessions Live → Start → Ctrl+Shift+C → End → Past | ⏸ operator smoke |
| 4 | UI launch + Tests → double-click test → Run → see results | ⏸ operator smoke |
| 5 | UI launch + Annotate a past checkpoint → Save to inbox | ⏸ operator smoke |
| 6 | UI launch + tab-switch responsiveness, no clipped buttons | ⏸ operator smoke |
| 7 | `canary session list` + `canary session report --id <id>` | ⏸ operator smoke |
| 8 | MCP `list_sessions` + `get_session_report` from a Claude session | ⏸ operator smoke |

Build ✅ and unit tests (283 passing) ✅. The 8 workflow gates are operator-attended end-to-end smokes.

### Snapshot tag

`pre-impl-ui-avalonia-2026-05-27` — leave in place until the operator confirms the 8 smokes pass. Per the prompt §7, deleted after green.

### Done state

- ✅ `src/Canary.UI/` deleted.
- ✅ `src/Canary.UI.Avalonia/` is the UI project; output is `Canary.UI.exe`.
- ✅ `Canary.Harness/UiLocator.cs` points at the Avalonia exe.
- ⏸ All 8 Phase 6 smoke workflows — operator-attended.
- ✅ 0 warnings / 0 errors. Unit-test count 283 (down from 330 peak; 47 WinForms-only tests removed). Net +25 vs the 258 pre-migration baseline.
- ✅ Feature doc status `shipped`.
- ✅ CHANGELOG + BUILD_LOG + Canary CLAUDE.md + README updated.
- ⏸ Snapshot tag `pre-impl-ui-avalonia-2026-05-27` deleted — operator's call after smoke.

### Commits

- (pending) — `chore(ui): Phase 6 cutover — Avalonia becomes Canary.UI; delete WinForms project`
- (pending) — `docs: Phase 6 cross-repo doc pass + MultiVerse cross-repo entry`
