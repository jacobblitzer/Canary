---
date: 2026-05-27
tags: [feature, ui, avalonia, migration, canary]
status: in-progress
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
- **Phase 2 — Tests tab (in-progress, 2026-05-27)** — workload tree + Welcome/TestRunner/ResultsViewer/Recording sub-panels + Tests-only toolbar items (Run Tests + Mode picker + Record) + F5 keybinding. Verification gates 1, 6 ✅; gates 2–5, 7 ⏸ pending operator smoke.
- **Phase 3 — editors (queued)** — TestEditor / SuiteEditor / WorkloadEditor.
- **Phase 4 — annotation polish (queued)** — hit-testing, undo, tool palette polish.
- **Phase 5 — services + glue (queued)** — Abort hotkey, drag-and-drop, context menus, AutoRun forwarding.
- **Phase 6 — cutover (queued)** — flip default UI, delete `src/Canary.UI/`, full smoke matrix.

Operator review at every phase boundary; no push until Phase 6.

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
