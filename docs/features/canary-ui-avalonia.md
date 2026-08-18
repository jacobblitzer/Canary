---
date: 2026-05-27
tags: [feature, ui, avalonia, migration, canary]
status: shipped
project: canary
component: ui
---

# Canary.UI Avalonia migration

Migrate `Canary.UI` from hand-positioned WinForms to **Avalonia 11 + FluentAvalonia + CommunityToolkit.Mvvm** so layout primitives reflow on resize, modal dialogs size to content, dark theme + DPI scaling come for free, and every UI feature ships without the recurring "clipped button / overlapping tab / hidden status line" pain pattern.

Same .NET 8 runtime, same `Canary.Core` types, same Win32 hotkeys + named-pipe forwarding. The migration touches exactly one project: `Canary.UI` → `Canary.UI.Avalonia`. Everything else (`Canary.Core`, `Canary.Harness`, `Canary.Agent.*`, `Canary.McpServer`, `Canary.Tests`) stays untouched.

## Why

Every UI feature shipped in the last six weeks surfaced fresh layout regressions in WinForms's hand-positioned model:

- Phase 7 debug-overhaul nav tabs needed a polish commit after operator screenshots flagged the tab strip.
- Phase 2 supervised-session shipped 2026-05-27 with three visible defects in the first operator screenshot: "Capture with note" button clipped to "Captur...", inner Live/Past tab strip overlapping the outer nav tab content, status line overlapping the hotkey hint label.
- `AnnotationCanvas` was already a WPF island (via `ElementHost`) — a one-off escape valve from the same underlying problem.

The pain is structural. WinForms's static layout model doesn't reflow; every new control near existing ones forces a manual re-tune; skinning libraries restyle but don't change the layout primitive. Avalonia 11's layout behaves like CSS Flexbox/Grid — containers reflow on resize, controls truncate via `TextTrimming`, modal `Window`s size from content.

## Approach

**Additive then subtractive.** A new `Canary.UI.Avalonia` csproj lives alongside the existing `Canary.UI` for phases 0–5. Both build green throughout. At Phase 6 cutover, `Canary.UI.exe` becomes the Avalonia build and `src/Canary.UI/` is deleted in a single commit.

Driving artifacts:

- **Plan**: [`docs/plans/2026-05-27-canary-ui-avalonia-migration.md`](../plans/2026-05-27-canary-ui-avalonia-migration.md)
- **Implementation prompt**: `C:/Repos/MultiVerse/prompts/canary-ui-avalonia-implement-2026-05-27.md`
- **Per-phase progress log**: [`docs/progress/2026-05-27-canary-ui-avalonia-migration.md`](../progress/2026-05-27-canary-ui-avalonia-migration.md)

## Locked decisions (2026-05-27)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Avalonia 11.2.5 | Current stable LTS, .NET 8 compatible. |
| 2 | FluentAvalonia 2.2.0 | NavigationView + Windows 11 fidelity + community size. |
| 3 | CommunityToolkit.Mvvm 8.3.2 | Microsoft-maintained source generators ([ObservableProperty], [RelayCommand]). |
| 4 | Parallel build phases 0–5 | New project `src/Canary.UI.Avalonia/` alongside existing `src/Canary.UI/`. Both build green throughout. |
| 5 | Windows-only (net8.0-windows) | Matches the rest of Canary. Linux/macOS stretch deferred. |
| 6 | Win32 hotkeys via Comctl32 SetWindowSubclass | Avalonia doesn't expose a WndProc message filter; subclassing intercepts WM_HOTKEY against the main window's HWND. |
| 7 | Single-instance pipe — port verbatim | Transport doesn't care about UI framework. |

## Phase status

- **Phase 0 — spike (shipped, 2026-05-27)** — Sessions panel + annotation canvas + global hotkeys ported to Avalonia. Layout reflow + functional smoke confirmed by operator; go/no-go decision: GO.
- **Phase 1 — shell + simple panels (shipped, 2026-05-27)** — Localhost / Feedback / Telemetry / Settings nav items + Open workloads folder toolbar.
- **Phase 2 — Tests tab (shipped, 2026-05-27)** — workload tree + Welcome/TestRunner/ResultsViewer/Recording sub-panels + Tests-only toolbar items + F5 keybinding.
- **Phase 3 — editors (shipped, 2026-05-27)** — TestEditor / SuiteEditor / WorkloadEditor with JSON round-trip property tests. Editors are orphan ViewModels/Views; wire-in via tree context menus lands in Phase 5.
- **Phase 4 — annotation polish (shipped, 2026-05-27)** — undo stack (Ctrl+Z), tool-palette ToggleButton group, AnnotateWindowViewModel extracted from code-behind, feedback-inbox parity (inbox-mode constructor + FeedbackInboxWriter wiring).
- **Phase 5 — services + glue (shipped, 2026-05-27)** — AbortHotkey (Pause) armed during runs; AutoRunRequestHandler + `HandleAutoRunAsync` route pipe-forwarded `canary run` invocations to a tree-driven run; tree drag-and-drop + context menus; Phase 3 editors wired in via `EditorHostWindow`.
- **Phase 6 — cutover (shipped, 2026-05-27)** — `Canary.UI.Avalonia.csproj` `<AssemblyName>` flipped to `Canary.UI` so the produced exe matches the legacy filename; `Canary.Harness/UiLocator.cs` repointed to the Avalonia sibling; `src/Canary.UI/` deleted entirely; WinForms-coupled tests removed; `Canary.Tests.Integration` repointed to the Avalonia `SingleInstancePipeServer`; cross-repo doc pass. Migration shipped.

Operator review at every phase boundary; no push until Phase 6.

## Environment tab (added 2026-08-18, deployment campaign Phase 5b)

A 7th nav item — `EnvironmentViewModel` + `EnvironmentView` — answering "is this machine set up",
which is the question you ask **before** a test result is worth believing. Placed beside Settings
rather than beside Tests for that reason.

| Grid | Shows |
|---|---|
| **Clashes** | The `EnvironmentReport.Analyse` findings, severity first. Leads, because it is why the tab exists. |
| **Loaded plug-ins** | Every library the host registered, with `Origin` (package / libraries / bundled / developer) and the full path it loaded from. The origin column is the shadowing signal: a developer folder beats a deployed install, so install and update can both report success while old code runs. |
| **Requirements** | What the workload's content declares. `Check requirements` resolves the file and service ones live, with no launch. |
| **Scan folders** | Every folder the host was told to scan, with existence — including anything added by hand in Grasshopper's Developer Settings. |

**It reads `results/environment.json`; it launches nothing.** Every run already writes that file,
and a tab that silently started Rhino to populate itself would be a surprising thing for a click
to do. `CapturedAt` is displayed so a stale report *looks* stale rather than authoritative, and
`Refresh` is wired to `TestRunnerViewModel.RunCompleted` because a finished run is the only thing
that can change the data. (That event is raised on the UI thread — the raise site follows
`ConfigureAwait(true)` and the sibling `RunHistory` handler mutates UI-bound rows from it.)

Two deliberate honesty properties, both tested in
`tests/Canary.Tests/UI.Avalonia/EnvironmentViewModelTests.cs`:

- **No capture is not a clean capture.** An empty grid is ambiguous, so the status line resolves
  it — `No environment.json …` versus `0 loaded`. A corrupt report reports the read failure
  rather than rendering as empty.
- **Unjudgeable is not OK.** `RequirementChecker.CheckOfflineAsync` returns only *misses*, so a
  `plugin` requirement it cannot decide is absent from that list and indistinguishable from a
  pass. Those rows render as `in-app only` instead of being dropped, so a half-checked machine
  cannot read as a fully checked one. Before `Check` is pressed, rows read `not checked`.

Also the QC comparison surface: the same JSON captured on two machines, diffed, is what
"did this install correctly" reduces to in practice.

## Implementation pointers (Phase 0)

- `src/Canary.UI.Avalonia/Canary.UI.Avalonia.csproj` — net8.0-windows + WinExe + Avalonia 11.2.5 + FluentAvaloniaUI 2.2.0 + CommunityToolkit.Mvvm 8.3.2.
- `src/Canary.UI.Avalonia/Program.cs` + `App.axaml` — classic-desktop lifetime, FluentAvalonia dark theme.
- `src/Canary.UI.Avalonia/Views/MainWindow.axaml` — FluentAvalonia `NavigationView` shell (Sessions item only for spike).
- `src/Canary.UI.Avalonia/Views/SessionsLiveView.axaml` — `Grid` + `WrapPanel` for buttons. Reflows on narrow widths; status text wraps via `TextWrapping=Wrap`. The WinForms layout bugs cannot recur in this shape.
- `src/Canary.UI.Avalonia/ViewModels/SessionsLiveViewModel.cs` — state machine `Idle/Starting/Armed/Ending` with `[ObservableProperty]` + `[RelayCommand(CanExecute=...)]`.
- `src/Canary.UI.Avalonia/Controls/AnnotationCanvas.cs` — Avalonia port of the WPF `AnnotationCanvas`. Same four tool modes, same annotations.json shape.
- `src/Canary.UI.Avalonia/Hotkeys/SessionHotkeyHook.cs` — Win32 RegisterHotKey against the Avalonia main window's HWND. Comctl32 SetWindowSubclass intercepts WM_HOTKEY.
- `tests/Canary.Tests/UI.Avalonia/SessionsLiveViewModelTests.cs` + `SessionsPastViewModelTests.cs` — 12 unit tests via the existing StubFactory/StubAgent pattern.

## See also

- Plan: [`docs/plans/2026-05-27-canary-ui-avalonia-migration.md`](../plans/2026-05-27-canary-ui-avalonia-migration.md)
- Progress log: [`docs/progress/2026-05-27-canary-ui-avalonia-migration.md`](../progress/2026-05-27-canary-ui-avalonia-migration.md)
- Predecessor: [`docs/plans/2026-05-24-canary-debug-overhaul.md`](../plans/2026-05-24-canary-debug-overhaul.md) § C4 (WinForms nav tabs being replaced).
