# Third-party licenses

This file records the licenses of third-party libraries Canary depends on at runtime
or vendors source from. New libraries added to any `Canary.*.csproj` MUST get an entry
here before the corresponding commit.

Format: library name + version + license + URL + what Canary uses it for. Vendored
source (single-file ports, snippets) gets its own section below the NuGet table.

---

## NuGet runtime dependencies

| Library | Version | License | Used by | Purpose |
|---|---|---|---|---|
| SixLabors.ImageSharp | 3.1.x | Apache-2.0 | `Canary.Core` | Pixel-diff (`PixelDiffComparer`), SSIM (`SsimComparer`), pixel-diff overlay rendering, animated-GIF encoding (`AnimatedGifEncoder`, Phase 4.6.F Session B). |
| SixLabors.ImageSharp.Drawing | 2.1.x | Six Labors Split License | `Canary.Core` | Diff-overlay rendering (rectangles + text on the diff PNG). |
| Avalonia | 11.x | MIT | `Canary.UI.Avalonia` | Cross-platform desktop UI for the test runner. |
| Avalonia.Labs.Gif | 11.3.1 | MIT | `Canary.UI.Avalonia` | Animated-GIF playback control in the runner card (Phase 4.6.F Session B++). Official Avalonia Labs incubation project. |
| CommunityToolkit.Mvvm | latest | MIT | `Canary.UI.Avalonia` | `ObservableObject` / `[ObservableProperty]` / `[RelayCommand]` source generators for the runner view-models. |
| System.Text.Json | (BCL) | MIT | `Canary.Agent` / `Canary.Core` | Test JSON parsing + bridge serialization. |

> The ImageSharp.Drawing license is the "Six Labors Split License" — free for OSS/educational use; commercial use requires a paid licence. Canary's open-source status means it's free for us today, but commercial deployment (e.g. a hosted SaaS that runs Canary) would need a Six Labors agreement. Tracked for visibility; no action today.

## Vendored source

- **`Canary.UI.Avalonia/Converters/GifPathToSourceConverter.cs`** —
  pattern adapted from Avalonia.Labs.Catalog's sample
  `GifSourceConverter` (Avalonia.Labs repo, MIT, 11.3.1 tag). Wraps
  a `string` file path into a `file://` `Uri` so
  `Avalonia.Labs.Gif.GifImage.Source` (typed `object`) routes through
  `AssetLoader.Open(uri)` to a local file stream. Inline rather than
  vendored verbatim because the upstream class is internal to the
  Labs sample app, not shipped from the NuGet.

---

History:
- 2026-06-01 — created; first entry SixLabors.ImageSharp (Phase 4.6.F Session B, animated-GIF capture).
- 2026-06-02 — added Avalonia.Labs.Gif (Phase 4.6.F Session B++, in-card animated-GIF playback).
