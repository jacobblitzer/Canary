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
| CommunityToolkit.Mvvm | latest | MIT | `Canary.UI.Avalonia` | `ObservableObject` / `[ObservableProperty]` / `[RelayCommand]` source generators for the runner view-models. |
| System.Text.Json | (BCL) | MIT | `Canary.Agent` / `Canary.Core` | Test JSON parsing + bridge serialization. |

> The ImageSharp.Drawing license is the "Six Labors Split License" — free for OSS/educational use; commercial use requires a paid licence. Canary's open-source status means it's free for us today, but commercial deployment (e.g. a hosted SaaS that runs Canary) would need a Six Labors agreement. Tracked for visibility; no action today.

## Vendored source

None at present. If a future single-file vendoring lands here (e.g. a tiny GIF89a encoder, a glyph atlas, an LZW codec), add the upstream URL, license, SHA, and `Canary/src/...` location.

---

History:
- 2026-06-01 — created; first entry SixLabors.ImageSharp (Phase 4.6.F Session B, animated-GIF capture).
