---
date: 2026-06-02
tags:
  - research
  - canary
  - ui
  - avalonia
  - gif
status: complete
project: canary
component: ui-results-viewer
related-bug: 0006-resultsviewer-gif-crash-batch-bind
---

# Animated-GIF playback options for Avalonia 11.x (Canary.UI)

## Motivation
`docs/bugs/0006-resultsviewer-gif-crash-batch-bind.md` documents three consecutive crash signatures stemming from `Avalonia.Labs.Gif.GifImage` 11.3.1 in a batch-bound `DataTemplate` scenario. This report surveys alternatives so the eventual fix can be picked from informed options rather than further blind XAML defenses.

## 1. Known `Avalonia.Labs.Gif` issues (11.3.1)

- `GifImage.OnAttachedToVisualTree` calls `InitializeGif()` synchronously; `InitializeGif()` switches over `Source` types (Stream / Uri / string) and **throws `ArgumentException` for any other value, including `null`** ([GifImage.cs source](https://github.com/AvaloniaUI/Avalonia.Labs/blob/main/src/Avalonia.Labs.Gif/GifImage.cs)).
- `GifInstance` constructor decodes every frame synchronously on the UI thread ([GifInstance.cs source](https://github.com/AvaloniaUI/Avalonia.Labs/blob/main/src/Avalonia.Labs.Gif/GifInstance.cs)); batch-bind of N GIF cards serializes N decodes on the UI thread.
- PR #138 ([Avalonia.Labs](https://github.com/AvaloniaUI/Avalonia.Labs/pulls?q=is%3Apr+gif), merged 2026-06-02) fixes a four-bug cluster matching our symptoms — `IAnimatedBitmap` missing `IDisposable`, no Measure/Arrange invalidate on Source change, `InvalidOperationException` "Visual already has a parent" when parent container changes, `InvalidOperationException` on dispose-before-init. PR #138 patches the SUCCESSOR `AnimatedImage` control (Avalonia 12.x only); `GifImage` 11.x stays broken.
- Version map ([NuGet](https://www.nuget.org/packages/Avalonia.Labs.Gif)): 11.3.1 (Jul 2025) is the last 11.x. The package then jumped to 12.0.0-rc1 / 12.0.2; **there is no 11.4 / 11.5**. Canary.UI is pinned to a known-broken release as long as Avalonia 11.x is its target.
- Open issues #116 (decode fails on some files, June 2025) and #141 (Stretch=UniformToFill broken) are independent of our crash but reinforce the maintenance picture.

## 2. Alternative libraries

| Package | Latest | License | Last commit | Targets | Notes |
|---|---|---|---|---|---|
| **Avalonia.Labs.Gif** | 11.3.1 / 12.0.2 | MIT | 2026-06-02 (12.x line) | 11.x stuck; 12.x active | Current; PR #138 fixes only land in 12.x. |
| **AnimatedImage.Avalonia** ([whistyun](https://github.com/whistyun/AnimatedImage)) | 2.1.4 | Apache-2.0 | 2026-02-07 | .NET 9 + Framework 4.7.2; Avalonia 11 compatible | Attached-property API on plain `Image`: `<Image anim:ImageBehavior.AnimatedSource="..." />`. 4 open issues. No documented `ItemsControl` guidance. ([nuget](https://www.nuget.org/packages/AnimatedImage.Avalonia)) |
| Avalonia.Gif (original) | never released | — | — | — | Deprecated upstream: "Deprecated in favor of the GIF function in Avalonia.Labs." ([repo](https://github.com/AvaloniaUI/Avalonia.GIF/)) |
| Custom UserControl | n/a | n/a | n/a | any | Build on `Avalonia.Controls.Image` + `SixLabors.ImageSharp` (already in `Canary.Core`) + `DispatcherTimer`. ~150 LOC. No custom-visual lifecycle hazard. |

Thread-safety: `Avalonia.Labs.Gif` uses `CompositionCustomVisualHandler` (Avalonia's official render-off-UI-thread primitive) for the renderer, but the DECODER runs on UI thread inside the `GifInstance` ctor — batch attach blocks. `AnimatedImage.Avalonia` does not document off-thread playback; based on source inspection it appears to decode synchronously as well, but it hosts a plain `Image` so the custom-visual lifecycle bug class is absent. Custom panel built on `ImageSharp` can decode on `Task.Run` then post frames to the UI thread.

## 3. Pattern surveys for "animation in DataTemplate inside ItemsControl"

WebSearch did not surface concrete OSS examples of `gif:GifImage` inside `ItemsControl` / `DataGrid` / `ListBox`. Closest community signal: [Avalonia discussion #19194](https://github.com/AvaloniaUI/Avalonia/discussions/19194) (sanctioned answer is "use Avalonia.Labs.Gif"; no guidance for collection scenarios). Avalonia core devs in [discussion #14005](https://github.com/AvaloniaUI/Avalonia/discussions/14005) acknowledge that custom-visual controls in `ItemsRepeater` are a known soft-spot. **No evidence found** of a battle-tested binding pattern for our exact scenario.

## 4. Avalonia native primitives

- **`Image.Source` accepts `IImage`** but there is no animated `IImage`. `Bitmap` / `WriteableBitmap` are single-frame.
- **Manual scheme**: decode frames once (ImageSharp or `SKCodec`), keep `List<Bitmap>`, advance index on `DispatcherTimer.Tick`, assign to `Image.Source`. This is what `AnimatedImage.Avalonia` does internally.
- No `IBitmap`-style animated bitmap exists in `Avalonia.Media.Imaging` ([Avalonia docs surveyed via search, no evidence found](https://docs.avaloniaui.net/)).

## 5. Recommendation

For Canary.UI's batch-bound DataTemplate scenario (1-4 cards, 500 KB – 1.5 MB GIFs, 11 frames at 900×900, decoded once per Past Runs navigation), ranked:

1. **Custom `Canary.UI.Avalonia.Controls.AnimatedImagePanel` UserControl** — hosts plain `Image`, decodes via `SixLabors.ImageSharp` on `Task.Run`, caches `List<Bitmap>`, `DispatcherTimer` drives playback. Highest reliability (no `CompositionCustomVisualHandler`), modest code (~150 LOC), full control over null/missing/oversized source handling. Drops `Avalonia.Labs.Gif` dependency entirely.
2. **Swap to `AnimatedImage.Avalonia` 2.1.4** — `<Image anim:ImageBehavior.AnimatedSource="..." />` attached-property API. ~3 lines of XAML change per usage. Apache-2.0. 4 open issues + maintenance unknown long-term. Quick swap but inherits third-party risk.
3. **Upgrade to Avalonia 12.x + Avalonia.Labs.Gif 12.0.3+** — most direct fix for the actual bug (PR #138 patches the successor control). Out of band; Avalonia 11 → 12 migration is a separate project of unknown scope.
4. **Stay on 11.3.1 with more defenses** (Dispatcher.UIThread.Post the Add, code-behind lazy add) — lowest effort but the underlying bug is in unowned code and recurrence risk is high.

**Going with #1.** Justification: PR #138's existence is proof the labs team knows custom-visual controls in collection scenarios are unsafe; rather than wait for an Av12 migration, owning our own animation primitive is the smaller commitment and removes the dependency on a known-broken library. ImageSharp's GIF decoder is mature and already in our dependency graph.

## Sources cited

- [Avalonia.Labs GifImage.cs](https://github.com/AvaloniaUI/Avalonia.Labs/blob/main/src/Avalonia.Labs.Gif/GifImage.cs)
- [Avalonia.Labs GifInstance.cs](https://github.com/AvaloniaUI/Avalonia.Labs/blob/main/src/Avalonia.Labs.Gif/GifInstance.cs)
- [Avalonia.Labs PR list (#138 fix-cluster)](https://github.com/AvaloniaUI/Avalonia.Labs/pulls?q=is%3Apr+gif)
- [Avalonia.Labs issues #116, #141](https://github.com/AvaloniaUI/Avalonia.Labs/issues)
- [Avalonia discussion #14005 — custom visuals in ItemsRepeater](https://github.com/AvaloniaUI/Avalonia/discussions/14005)
- [Avalonia discussion #19194 — animated GIF guidance](https://github.com/AvaloniaUI/Avalonia/discussions/19194)
- [Avalonia.Gif deprecation notice](https://github.com/AvaloniaUI/Avalonia.GIF/)
- [whistyun/AnimatedImage repo](https://github.com/whistyun/AnimatedImage)
- [NuGet — Avalonia.Labs.Gif](https://www.nuget.org/packages/Avalonia.Labs.Gif)
- [NuGet — AnimatedImage.Avalonia](https://www.nuget.org/packages/AnimatedImage.Avalonia)
- [Canary commits c79b9c1 / 092f9e7 / 661a77f / 4a24e8f](https://github.com/local/canary) (local repo)
- Crash dumps `Canary.UI.exe.{39112,20312,43820}.dmp` (local: `C:/Users/Jacob/AppData/Local/CrashDumps/`)
