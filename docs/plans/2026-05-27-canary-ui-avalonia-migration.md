---
date: 2026-05-27
tags: [plan, canary, ui, avalonia, migration, draft]
status: draft
project: canary
component: ui
---

> **Status: DRAFT.** Operator has flagged recurring Canary.UI layout regressions (cut-off buttons, overlapping nav tabs, clipped status lines) as a pattern, not one-off bugs. This plan proposes migrating off hand-positioned WinForms to Avalonia 11 + FluentAvalonia. Spike phase is the go/no-go gate; if it passes, the remaining 6 phases ship the migration in ~2–3 weeks of focused work.

# Canary.UI Avalonia migration plan

## The ask, in one sentence

Stop chasing per-feature UI regressions in Canary by moving Canary.UI off hand-positioned WinForms onto Avalonia 11 + FluentAvalonia, where layout primitives (`Grid`, `StackPanel`, `WrapPanel`, `NavigationView`) reflow automatically and dark theme + DPI scaling come for free.

## Why this matters

Every UI feature shipped to Canary in the last six weeks has surfaced fresh cut-off / overflow / overlap regressions:

- Phase 7 nav tabs (debug-overhaul) needed a polish commit after operator screenshots showed the tab strip looked like an afterthought.
- Phase 2 supervised-session shipped 2026-05-27 with three visible defects in the very first operator screenshot: "Capture with note" button clipped to "Captur...", the inner Live / Past tab strip overlapped the outer nav tab content, and the status line overlapped the hotkey hint label.
- `AnnotationCanvas` is already a WPF island via `ElementHost` — one-off escape valve for the same underlying problem.

The pain is structural, not cosmetic. WinForms's static layout model doesn't reflow. Every new control near existing ones forces a manual re-tune. Skinning libraries (MaterialSkin etc.) restyle controls but don't change the layout primitive. The pain compounds with every feature.

Avalonia 11's layout primitives behave like CSS Flexbox/Grid: containers reflow on resize, controls truncate gracefully via `TextTrimming`, modal dialogs size from content. FluentAvalonia provides a Windows-11-styled `NavigationView` that's a drop-in replacement for our nav `TabControl`, plus dark theme + Mica + DPI scaling out of the box. Same .NET runtime, same `Canary.Core` types, no shift in toolchain.

This migration is **additive then subtractive**: a new `Canary.UI.Avalonia` csproj lives alongside the existing `Canary.UI` for ~6 phases of port work, then the old one is deleted at cutover. Both build green throughout — operator work on Canary's CLI / orchestrator / telemetry surfaces never blocks on this.

## Read in this order (5 min)

1. `CLAUDE.md` Quick Reference + the "Cross-Repo Change Protocol" + "Auto-Journaling Rules" sections.
2. `docs/plans/2026-05-24-canary-debug-overhaul.md` § Executive summary + § C4 (nav tabs) — context for what the WinForms shell currently provides.
3. `src/Canary.UI/MainForm.cs` — the shell that's being replaced. Note `_navTabControl` (lines 149–168), `_testsOnlyToolbarItems` visibility juggling (lines 416–443), the lazy `AddNavTab` pattern (444–463), and the WndProc hotkey forward (1455–1460).
4. `src/Canary.UI/Navigation/INavMode.cs` + `NavModes.cs` — the lazy-create + cached-content abstraction that any replacement needs to honor.
5. `src/Canary.UI/Panels/SessionsLiveSubPanel.cs` — most complex panel + canonical example of the layout pain. Phase 0 (spike) reimplements this in Avalonia.
6. `src/Canary.UI/Annotation/AnnotationCanvas.cs` + `AnnotatedImageForm.cs` — the existing WPF island; needs a Canvas-equivalent in Avalonia.
7. https://docs.avaloniaui.net/ + https://github.com/amwx/FluentAvalonia — primary docs for the chosen stack.

## Step 0 — pre-flight (~10 min)

```bash
cd C:\Repos\Canary
git status                                      # must be clean
dotnet build Canary.sln                         # baseline: must be 0 warnings, 0 errors
dotnet test tests/Canary.Tests/Canary.Tests.csproj --filter "Category=Unit" --logger "console;verbosity=minimal"
git tag pre-impl-ui-avalonia-2026-05-27
```

Record the baseline unit-test count. Migration must net-zero or net-add tests; flag any regression immediately. Snapshot tag is the rollback anchor for the whole migration.

## Step 1 — design lock (15 min, in this prompt)

Decisions baked in before any code is written:

1. **Avalonia 11.x** (current stable LTS, .NET 8 compatible).
2. **FluentAvalonia** for theming + `NavigationView` for the left-nav (replaces the top TabControl). MIT, mature, mirrors Windows 11 design. Semi.Avalonia evaluated as runner-up; FluentAvalonia wins on `NavigationView` + DataGrid maturity.
3. **MVVM via `CommunityToolkit.Mvvm`** (source-generator-based, lightweight, no Rx learning curve). ReactiveUI evaluated and rejected — too much new syntax for the team's current .NET surface.
4. **New project `src/Canary.UI.Avalonia/` (WinExe, net8.0-windows)** alongside the existing `Canary.UI`. Both build green; both produce a usable exe; the choice of which to launch is gated by a `CANARY_UI_BACKEND` env var or a `--ui=avalonia` flag during the parallel period.
5. **Hotkeys**: keep Win32 `RegisterHotKey` P/Invoke for global hotkeys (Ctrl+Shift+C/A in supervised-session, Pause for abort). Avalonia gives us a window handle just like WinForms does. In-app shortcuts (Ctrl+O, Ctrl+R, etc.) move to Avalonia `KeyBindings`.
6. **Single-instance forwarding**: reuse `Canary.UI/SingleInstancePipeServer.cs` verbatim. Transport doesn't care about UI framework.
7. **`AnnotationCanvas` port**: Avalonia `Canvas` + `Path` elements + pointer event handlers. Mechanically similar to WPF; about a day of work.
8. **What does NOT move**: `Canary.Core`, `Canary.Harness`, `Canary.Agent.*`, `Canary.McpServer`, `Canary.Tests`. The migration touches exactly one project (Canary.UI → Canary.UI.Avalonia) plus a `Canary.sln` edit to register it.
9. **Cutover strategy**: when all phases ship + smoke-test green, swap the `Canary.UI.exe` shortcut + `UiLocator` to point at the Avalonia build, then delete `src/Canary.UI/` in a single "remove WinForms shell" commit. The existing csproj name is preserved (`Canary.UI` namespace stays) so external references don't change.
10. **What we lose vs WinForms**: nothing functional. Embedded WinForms designer support (`.Designer.cs`) — fine, the existing controls are coded by hand anyway. Direct `Process.Start("explorer.exe", path)` works the same.

## Step 2 — Phase 0: spike (~2 days)

**Goal**: prove the migration thesis end-to-end on a single non-trivial panel before committing to the full port. The Sessions Live tab is the right spike target — it's the most layout-pained panel we've shipped and exercises hotkeys, modal dialogs, the annotation surface, and the orchestrator.

Files to add:

```
src/Canary.UI.Avalonia/Canary.UI.Avalonia.csproj
src/Canary.UI.Avalonia/Program.cs                                  ← StartupUri + Avalonia app builder
src/Canary.UI.Avalonia/App.axaml                                   ← FluentAvalonia theme registration
src/Canary.UI.Avalonia/App.axaml.cs
src/Canary.UI.Avalonia/Views/MainWindow.axaml                      ← shell with NavigationView (just the Sessions item for now)
src/Canary.UI.Avalonia/Views/MainWindow.axaml.cs
src/Canary.UI.Avalonia/Views/SessionsView.axaml                    ← TabView with Live + Past sub-tabs
src/Canary.UI.Avalonia/Views/SessionsView.axaml.cs
src/Canary.UI.Avalonia/Views/SessionsLiveView.axaml                ← workload picker + buttons + thumbnail strip
src/Canary.UI.Avalonia/Views/SessionsLiveView.axaml.cs
src/Canary.UI.Avalonia/Views/SessionsPastView.axaml                ← list + report preview
src/Canary.UI.Avalonia/Views/SessionsPastView.axaml.cs
src/Canary.UI.Avalonia/Views/AnnotateWindow.axaml                  ← Avalonia port of AnnotatedImageForm
src/Canary.UI.Avalonia/Views/AnnotateWindow.axaml.cs
src/Canary.UI.Avalonia/Controls/AnnotationCanvas.cs                ← Avalonia Canvas + Path port
src/Canary.UI.Avalonia/ViewModels/MainWindowViewModel.cs
src/Canary.UI.Avalonia/ViewModels/SessionsViewModel.cs
src/Canary.UI.Avalonia/ViewModels/SessionsLiveViewModel.cs         ← state machine: Idle / Starting / Armed / Ending
src/Canary.UI.Avalonia/ViewModels/SessionsPastViewModel.cs
src/Canary.UI.Avalonia/Hotkeys/SessionHotkeyHook.cs                ← P/Invoke against Window.PlatformImpl.Handle.Handle
tests/Canary.Tests/UI.Avalonia/SessionsLiveViewModelTests.cs       ← state-machine tests independent of view
```

Key shapes:

```csharp
// SessionsLiveViewModel.cs
public partial class SessionsLiveViewModel : ObservableObject
{
    [ObservableProperty] private SessionState _state = SessionState.Idle;
    [ObservableProperty] private string? _selectedWorkloadName;
    [ObservableProperty] private ObservableCollection<WorkloadOption> _workloads = new();
    [ObservableProperty] private ObservableCollection<CaptureThumbnail> _captures = new();
    [ObservableProperty] private string _statusText = "Pick a workload, then Start session.";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync() { /* SupervisedSession.StartAsync via ISessionAgentFactory */ }

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private async Task CaptureAsync() { /* session.CaptureAsync + add thumbnail */ }

    // ...
}

public enum SessionState { Idle, Starting, Armed, Ending }
```

```xml
<!-- SessionsLiveView.axaml — note Grid columns auto-reflow + ToolTip on every button -->
<Grid RowDefinitions="Auto,Auto,*" ColumnDefinitions="*">
  <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="8" Margin="12">
    <ComboBox ItemsSource="{Binding Workloads}" SelectedValueBinding="..." Width="220"/>
    <Button Content="Start session" Command="{Binding StartCommand}" Classes="accent"/>
    <Button Content="Capture (Ctrl+Shift+C)" Command="{Binding CaptureCommand}"/>
    <Button Content="Capture + Annotate (Ctrl+Shift+A)" Command="{Binding CaptureAnnotateCommand}"/>
    <Button Content="Capture with note" Command="{Binding CaptureWithNoteCommand}"/>
    <Button Content="End session" Command="{Binding EndCommand}" Classes="danger"/>
  </StackPanel>
  <TextBlock Grid.Row="1" Text="{Binding StatusText}" TextWrapping="Wrap" Margin="12,0"/>
  <ItemsControl Grid.Row="2" ItemsSource="{Binding Captures}">
    <ItemsControl.ItemsPanel>
      <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate><DataTemplate>
      <Border Width="160" Height="100" Margin="4"><Image Source="{Binding Thumbnail}"/></Border>
    </DataTemplate></ItemsControl.ItemTemplate>
  </ItemsControl>
</Grid>
```

The buttons reflow into multiple rows on narrow widths (use `WrapPanel` instead of `StackPanel` if that's wanted). Text in `Button` content gets `TextTrimming="CharacterEllipsis"` from the theme. No hardcoded pixel positions anywhere.

Verification gates for Phase 0:

- `dotnet build Canary.sln` — 0 warnings, 0 errors. Both `Canary.UI` and `Canary.UI.Avalonia` build green side by side.
- New unit tests pass: `SessionsLiveViewModelTests` covers state transitions (Idle→Starting→Armed→Ending→Idle) with a stub `ISessionAgentFactory`.
- **Manual layout smoke**: launch `Canary.UI.Avalonia.exe`. Resize the window from 800x600 to 1920x1080. Buttons never clip or overflow. Tabs never overlap. Status text wraps cleanly. Compare to a side-by-side `Canary.UI.exe` running the original Sessions panel — the Avalonia version visibly reflows, the WinForms one shows its current truncation behaviour.
- **Functional smoke**: from the Avalonia exe, pick qualia → Start session → wait for Vite + Chrome → press Ctrl+Shift+C → see thumbnail appear → press Ctrl+Shift+A → annotate → Save → see annotated thumbnail → End session → close-out → switch to Past sub-tab → confirm the just-finished session row + report preview render correctly.
- **Decision gate**: if both smokes pass, commit + proceed to Phase 1. If layout still has pain points, course-correct (different theme, different layout primitive) within Phase 0 before moving on. If Avalonia genuinely can't model something we need, abandon migration + take the defensive WinForms cleanup path instead.

Commit shape:

- `feat(ui-avalonia): spike — Sessions panel in Avalonia (Phase 0)`
- `test(ui-avalonia): SessionsLiveViewModel state-machine tests`
- `docs(plans): supervised-session Phase 0 spike — go/no-go evidence`

## Step 3 — Phase 1: shell + simple panels (~3 days)

**Goal**: stand up the full nav shell + port the read-only panels (Localhost, Feedback, Telemetry, Settings). These are mostly mechanical AXAML conversions of existing WinForms layouts.

Files to add / modify:

- `src/Canary.UI.Avalonia/Views/MainWindow.axaml` — promote from spike. `NavigationView` with one `NavigationViewItem` per nav tab. Toolbar above (Open Folder + Mode picker — Tests-tab-only, hidden when not on Tests tab via binding).
- `src/Canary.UI.Avalonia/Views/{Localhost,Feedback,Telemetry,Settings}View.axaml` + ViewModels.
- `src/Canary.UI.Avalonia/Services/WorkloadsLocator.cs` — ports `MainForm.AutoDetectWorkloadsDir`.
- `src/Canary.UI.Avalonia/Services/CanaryUiSettings.cs` — wraps the existing `Canary.Settings.CanarySettings`.

Verification gates: each panel renders cleanly on first load and on workloads-dir change; the toolbar items that only apply to Tests tab hide when the operator switches off.

Commit shape: one commit per nav tab port (`feat(ui-avalonia): port LocalhostPanel to Avalonia`, etc.) so reverts are surgical.

## Step 4 — Phase 2: Tests tab (~4 days)

**Goal**: port the workload tree + content panel + the panels that the tree clicks into (WelcomePanel, TestRunnerPanel, ResultsViewerControl, RecordingPanel). This is the meat — most operator workflow lives here.

Files to add:

- `src/Canary.UI.Avalonia/Views/TestsView.axaml` — `SplitView` (Avalonia's `GridSplitter` between two `Grid`s) with `TreeView` left + `ContentPresenter` right.
- `src/Canary.UI.Avalonia/Views/{Welcome,TestRunner,ResultsViewer,Recording}View.axaml` + ViewModels.
- `src/Canary.UI.Avalonia/Controls/WorkloadTree.axaml` — the workload + suite + test + recording hierarchical view, ported from `MainForm.LoadWorkloadsDirAsync` + the `_treeView.Nodes` population logic.
- `src/Canary.UI.Avalonia/Services/WorkloadExplorer.cs` — already a service-shaped class; either reuse `Canary.UI/Services/WorkloadExplorer.cs` (move it to `Canary.Core` so both UIs share it) or port verbatim.

The TestRunnerPanel is the most stateful — port the streaming-log textbox + per-test progress + the orchestrator wiring carefully. Use `ObservableCollection<TestResultRow>` for the live results table.

Verification gates: end-to-end `Run Tests` works against a real workload (qualia smoke); pass/fail/crash/new statuses color correctly; abort hotkey (Pause) still works; ResultsViewerControl's approve / reject flows still write to the right places.

## Step 5 — Phase 3: editors (~2 days)

**Goal**: port `TestEditorControl`, `SuiteEditorControl`, `WorkloadEditorControl`. These are form-heavy + bind to `TestDefinition` / `SuiteDefinition` / `WorkloadConfig` POCOs from `Canary.Core`.

Avalonia bindings handle this cleanly via `TwoWay` bindings + `INotifyPropertyChanged` on the POCOs (which don't currently have it — either add it or wrap each in a ViewModel adapter; the latter is cleaner).

Verification gates: edit-and-save round-trips for all three editor types produce JSON identical to what the WinForms editor wrote (compare files byte-for-byte on a known test definition).

## Step 6 — Phase 4: annotation surface (~2 days)

**Goal**: port `AnnotationCanvas` (WPF) → Avalonia. Drop the `AnnotatedImageForm` WinForms wrapper entirely; use a standalone Avalonia `Window`.

Avalonia's `Canvas` accepts `Path` + `Polygon` + `Line` + `TextBlock` children with arbitrary X/Y. The pointer event API is `PointerPressed` / `PointerMoved` / `PointerReleased` — analogous to WPF's `MouseDown` / `MouseMove` / `MouseUp`. The freehand / rectangle / text tool modes port mechanically.

The "session-sink" overload from Phase 2 (the `Action<sourcePng, annotatedPng, annotationsJson>` callback) becomes a regular ViewModel callback in `AnnotateWindowViewModel`.

Verification gates: round-trip annotation against a session capture — the annotated PNG + annotations JSON shapes are byte-identical to what the WPF version produces.

## Step 7 — Phase 5: services + glue (~2 days)

**Goal**: port the misc Canary.UI services that don't fit neatly into a single panel:

- `AbortHotkey.cs` — Win32 P/Invoke against the Avalonia main window's HWND. Pattern: `TopLevel.GetTopLevel(this).TryGetPlatformHandle()` returns the HWND.
- `SessionHotkeyHook.cs` — same pattern; reuse the supervised-session Phase 2 work.
- `SingleInstancePipeServer.cs` — transport-only, reuse verbatim.
- `GuiTestLogger.cs` — write to an `ObservableCollection<string>` instead of a `RichTextBox`; bind in the TestRunnerView.
- Drag-and-drop (workload JSON + recordings) — Avalonia `DragDrop` events.
- Context menus — Avalonia `ContextMenu` attached to relevant items.

Verification gates: all of: drag-and-drop a `.json` recording onto the tree → creates a test; Pause hotkey aborts a running test; second `canary run` invocation forwards to the running UI; right-click on a workload node shows the correct context menu.

## Step 8 — Phase 6: cutover + cleanup (~1 day)

**Goal**: flip the default UI, kill the old one.

- Update `src/Canary.Harness/UiLocator.cs` to look for `Canary.UI.Avalonia.exe` (or rename the Avalonia project's output to `Canary.UI.exe` and replace the old one). The CLI's `TryLaunchUi` path becomes the Avalonia path.
- Update STANDARD.md §16 rule 8 reference to point at the new exe path.
- Delete `src/Canary.UI/` entirely. Single commit, large diff but mechanical.
- Update `Canary.sln`, `tests/Canary.Tests/Canary.Tests.csproj`, `CLAUDE.md` Quick Reference, `README.md`, `docs/features/*.md`, `docs/plans/2026-05-24-canary-debug-overhaul.md` (mark §C4 as superseded).
- Update `BUILD_LOG.md` + `docs/progress/2026-05-27-canary-ui-avalonia-migration.md` (final phase entry).
- Cross-repo: `MultiVerse/BUILD_LOG.md` cross-repo entry. No Penumbra / Qualia code changes needed.

Verification gates: re-run all three smokes from the supervised-session implementation prompt (CLI session start, UI workflow end-to-end, MCP `list_sessions` integration) under the Avalonia build. All three must produce on-disk artifacts identical in shape to the WinForms baseline.

Delete the snapshot tag once everything's green:

```bash
git tag -d pre-impl-ui-avalonia-2026-05-27
```

Don't push from this session — operator pushes the whole branch after a final review pass.

## Things to avoid

- **Don't migrate `Canary.Core`, `Canary.Harness`, `Canary.Agent.*`, `Canary.McpServer`, or `Canary.Tests`.** This plan touches exactly one project and its references. Cross-cutting refactors expand scope and break the rollback plan.
- **Don't introduce ReactiveUI.** Stick with `CommunityToolkit.Mvvm`. The team's `.NET` surface is `async / await` + LINQ, not Rx.
- **Don't try to share AXAML across light + dark themes.** FluentAvalonia handles both via theme variants; just pick `Theme="Dark"` in `App.axaml` and let the controls inherit.
- **Don't preserve the WinForms behaviour of "hardcoded modal dialog `Top=10, Left=10`".** Modal `Window`s in Avalonia size to content via `SizeToContent="WidthAndHeight"`. That's the whole point.
- **Don't push until cutover.** Each phase commits but stays local until Phase 6 completes; the operator pushes the whole branch in one motion.
- **Don't run more than one phase at a time.** Each phase has a verification gate; honour it.
- **Don't delete `src/Canary.UI/` until Phase 6's smokes pass.** The parallel-build period is what makes this safe.

## Stretch / nice-to-haves (only if Phase 6 lands fast)

- **Cross-platform Linux + macOS build of Canary.UI.Avalonia.** Avalonia is cross-platform by design; the test runner stays Windows-only because workload agents are Windows-bound (Rhino, CDP via Chrome), but a Linux build of the UI shell could let operators triage sessions from a non-Windows dev box.
- **Avalonia's `DataGrid` for the Past Runs + Past Sessions tables.** Better than `ListView` for sorting + column filtering. Phase 2's `SessionsPastSubPanel` and `PastRunsPanel` both gain a free upgrade.
- **Bring the `AnnotationCanvas` toolset up.** Once it's Avalonia-native, adding tools (arrows, callouts, blur regions for sensitive screenshots) becomes cheap. The current WPF island makes this awkward.
- **Theme switch in Settings tab.** FluentAvalonia supports light / dark / system-follow at runtime; expose it in `SettingsView` instead of the current hardcoded dark.

## Estimated effort

| Phase | Calendar | Effort | Risk |
|---|---|---|---|
| 0. Spike — Sessions panel | 2 days | 12–16 hrs | High — go/no-go gate |
| 1. Shell + simple panels | 3 days | 16–20 hrs | Low — mechanical port |
| 2. Tests tab | 4 days | 20–28 hrs | Medium — TestRunnerPanel statefulness |
| 3. Editors | 2 days | 10–14 hrs | Low |
| 4. Annotation surface | 2 days | 10–14 hrs | Low — WPF → Avalonia is mechanical |
| 5. Services + glue | 2 days | 10–14 hrs | Medium — global hotkey HWND interop |
| 6. Cutover | 1 day | 4–6 hrs | Low — final smoke |
| **Total** | **~2–3 weeks** | **~80–110 hrs** | **gate at Phase 0** |

Calendar with breaks + review cycles: 4–6 weeks. Honest worst-case if Phase 0 surfaces issues + needs rework: 8 weeks.

## Open questions for the operator

1. **FluentAvalonia vs Semi.Avalonia** — defaulting to FluentAvalonia for `NavigationView` + Windows-11 styling. Semi.Avalonia has a fresher look but smaller community. Confirm or override.
2. **CommunityToolkit.Mvvm vs ReactiveUI** — defaulting to CommunityToolkit. Confirm or override.
3. **Parallel-build duration** — happy with both `Canary.UI.exe` and `Canary.UI.Avalonia.exe` building during phases 0–5? Or want a faster cutover with a feature branch instead?
4. **Stretch goal: Linux / macOS Canary.UI shell** — yes / no / not now?
5. **Driving prompt** — should I write this plan into a `MultiVerse/prompts/canary-ui-avalonia-implement-2026-05-DD.md` companion (same shape as the supervised-session prompt) so a fresh Claude session can execute it, or are you driving the work directly from here?
