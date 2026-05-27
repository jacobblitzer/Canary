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
- (pending) — `test(ui-avalonia): Phase 1 panel ViewModel tests`
- (pending) — `docs(progress): Phase 1 — shell + simple panels`
