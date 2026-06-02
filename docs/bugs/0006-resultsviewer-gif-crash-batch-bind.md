---
title: "ResultsViewer crashes Canary.UI when cards bind GIF candidates"
date: 2026-06-02
tags:
  - bug
  - canary
  - ui
  - avalonia
  - gif
status: resolved
project: canary
component: ui-results-viewer
severity: high
fix-commit: "pending"
canary-repro: "cpig-kin-15-watt-straight-line"
---

# ResultsViewer crashes Canary.UI when cards bind GIF candidates

## Summary
Navigating to Past Runs (or `🔁 View Latest Run`) for any test whose `result.json` carries a `CheckpointResults[*].GifPath` (e.g. `cpig-kin-15-watt-straight-line`'s scrub-captured `persp.gif`) silently crashes Canary.UI. The crash is an `ArgumentException` ("Unsupported Source object") or `FileNotFoundException` thrown inside `Avalonia.Labs.Gif.GifImage.InitializeGif()` during `OnAttachedToVisualTree`, raised on the layout-pass dispatcher operation → unhandled → process exits. Three defensive XAML attempts failed; the control is fundamentally incompatible with our batch-bind `DataTemplate` scenario.

## Environment
- Canary.UI 1.0, Avalonia 11.2.5, `Avalonia.Labs.Gif` 11.3.1
- Windows 11, .NET 8
- Trigger: opening Past Runs for any cpig-kin-* test (operator confirmed kin-15), where >1 `CheckpointResults` exist and ≥1 has a non-null `GifPath`

## Symptoms
- Canary.UI process silently terminates with no error dialog. Visible only via `C:/Users/Jacob/AppData/Local/CrashDumps/Canary.UI.exe.*.dmp`.
- Reproduced consistently on three commits with three different "fix" attempts (see Investigation).
- The TestRunner card view (live progress) shows the GIF correctly — same `gif:GifImage` element. Difference: cards added one at a time with `GifPath=null` initially, set later via `card.GifPath = …`.

## Root Cause
`Avalonia.Labs.Gif.GifImage` 11.3.1 raises an unhandled exception in `InitializeGif()` (called from `OnAttachedToVisualTree`) when the `Source` binding evaluates to any of:

1. **`null`** → `ArgumentException("Unsupported Source object: only Stream, Uri and absolute uri string are supported.")`
2. **A valid `Uri` whose target file does not exist** → `FileNotFoundException("The resource file:///... could not be found.")` from `AssetLoader.Open`.

The exception is raised during `ContentPresenter.UpdateChild` → `Visual.SetVisualParent` → `OnAttachedToVisualTreeCore`, deep inside the layout pass. Avalonia's dispatcher has no recovery for unhandled exceptions there; the process terminates.

In the ResultsViewer scenario, `ResultsViewerViewModel.LoadResult` calls `Cards.Clear()` then `Cards.Add(BuildCard(...))` for every checkpoint in `result.json`. The DataGrid auto-select fired by `PastRunsViewModel.ReloadAsync` triggers this in one batch. The `gif:GifImage` element inside each card's `DataTemplate` attaches synchronously on the layout pass and immediately reads `Source` — whatever value it has, valid or not. `IsVisible="false"` on the parent does NOT defer attachment.

The TestRunner pattern works by accident: cards are appended (`ProgressCards.Add`) with `GifPath=null` at construction; the outer `StackPanel`'s `IsVisible={GifPath != null}` is `false` at first attachment so the inner `GifImage` is never created. By the time `card.GifPath` is set asynchronously (via `OnGifCaptured` after the test completes), the parent panel has long since arranged with `IsVisible=false` and child instantiation was deferred. When `GifPath` flips, the panel becomes visible, child is now created, `Source` binding evaluates against a valid existing file. No crash.

The research agent's report ([docs/research/2026-06-02-avalonia-animated-gif-options.md](../research/2026-06-02-avalonia-animated-gif-options.md)) found that Avalonia.Labs PR #138 (merged 2026-06-02) fixes a similar custom-visual lifecycle bug class in the SUCCESSOR `AnimatedImage` control — but that ships in `Avalonia.Labs.Gif` 12.x only. There is no 11.4 / 11.5 backport; Avalonia.Labs jumped from 11.3.1 to 12.x. We are pinned to a broken release.

## Investigation
Three attempts at XAML-side defenses, each ending in the same crash class:

1. **`c79b9c1`** (initial `gif:GifImage` add to ResultsViewer card) — Source bound to `GifPath` via `GifPathToSourceConverter`. Past Runs crashed first navigation. Dump `Canary.UI.exe.39112.dmp` (09:05): `FileNotFoundException` on `persp.gif` (file lookup failure at attach).
2. **`092f9e7`** (defensive `IsVisible="{Binding ShowGif}"` opt-in toggle, default false). Past Runs crashed again. Dump `Canary.UI.exe.20312.dmp` (09:15): `ArgumentException: "Unsupported Source object"`. `IsVisible=false` does not prevent `OnAttachedToVisualTree`.
3. **`661a77f`** (computed `Uri? GifSource` returning null while `ShowGif=false`, bound to `Source` instead of via converter). Past Runs crashed again. Dump `Canary.UI.exe.43820.dmp` (09:40): same `ArgumentException`. Null Source IS the trigger — `InitializeGif()`'s switch over Source types has no null case and falls into the `default` throw.

Workaround attempted in `4a24e8f` (removed `gif:GifImage` element entirely; replaced with a "🌐 Open externally" button calling `Process.Start` on the GIF path). Past Runs stopped crashing. The operator wants in-card playback back; this workaround is rejected as a final state.

Stack (top frames) from dump 43820:
```
System.ArgumentException: "Unsupported Source object: only Stream, Uri and absolute uri string are supported."
   at Avalonia.Labs.Gif.GifImage.InitializeGif()                              +0x4c9
   at Avalonia.Labs.Gif.GifImage.OnAttachedToVisualTree(...)                  +0x29
   at Avalonia.Visual.OnAttachedToVisualTreeCore(...)                         +0x6e6
   at Avalonia.Layout.Layoutable.OnAttachedToVisualTreeCore(...)              +0x2d
   at Avalonia.Controls.Control.OnAttachedToVisualTreeCore(...)               +0x2e
   ... (5 more visual-tree-walk frames) ...
   at Avalonia.Visual.SetVisualParent(IList, Visual)                          +0x28a
   at Avalonia.Controls.Presenters.ContentPresenter.UpdateChild(object)       +0xe83
   at Avalonia.Controls.Presenters.ContentPresenter.ApplyTemplate()           +0x11d
   at Avalonia.Layout.Layoutable.MeasureCore(Size)                            +0x7b9
   ... (cascading Grid/Border/StackPanel.MeasureOverride frames) ...
   at Avalonia.Layout.LayoutManager.ExecuteMeasurePass()                      
   at Avalonia.Threading.Dispatcher.MainLoop(...)                             
   at Canary.UI.Avalonia.Program.Main(string[])
```

## Workaround (currently shipped)
`4a24e8f` removed the `gif:GifImage` element from `ResultsViewerView.axaml`. Each card now shows a `🌐 Open GIF externally` button that opens the GIF in the OS default image viewer (a browser, typically — animates full-size there). The static candidate PNG (first scrub frame) remains visible above. The `TestRunner` card retains its in-card playback (works there due to the deferred-attach accident described above).

## Planned fix
Replace `Avalonia.Labs.Gif.GifImage` everywhere with a custom `Canary.UI.Avalonia.Controls.AnimatedImagePanel` UserControl:
- Hosts a plain `Avalonia.Controls.Image` (no `CompositionCustomVisualHandler`).
- Decodes GIF frames via `SixLabors.ImageSharp` (already a Canary.Core dependency) on a worker thread.
- Caches a `List<Avalonia.Media.Imaging.Bitmap>` of decoded frames + a per-frame delay list.
- Drives playback via `DispatcherTimer`; advances `Image.Source` to the next frame on tick.
- `Source` property handles null/empty/missing-file gracefully — no exceptions thrown to the dispatcher.
- `IsPlaying` controls timer; defaults to true once decode completes.

This control is safe in batch-bound `DataTemplate` scenarios because it relies only on `Image` (a well-tested core control) and contains its own lifecycle. Removes the `Avalonia.Labs.Gif` package reference entirely.

See planning doc: `docs/research/2026-06-02-avalonia-animated-gif-options.md` (research) + the implementation plan attached to the fix PR.

## Lessons
- **`IsVisible=false` is not a safety net** for `OnAttachedToVisualTree` work in Avalonia 11. Children of an invisible parent are still attached and their attachment handlers run.
- **`Avalonia.Labs.Gif.GifImage` 11.3.1 fails on null Source and on missing files**, both as unhandled exceptions raised during the layout pass — they propagate to the dispatcher and terminate the process.
- **Crash dumps in `%LOCALAPPDATA%/CrashDumps/`** are the canonical diagnostic when Canary.UI silently terminates. `dotnet-dump analyze <dump>` + `pe -nested` extracts the managed exception even when no error dialog appears.
