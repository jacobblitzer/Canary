# CPIG_WORKLOAD.md — Canary's CPig Regression Workload

## Purpose
This file documents the conventions for Canary's CPig regression tests, which run on top of the existing `rhino` workload. The peer document on the CPig side is `C:\Repos\CPig\spec\CANARY.md`. Keep them in sync.

## What this workload tests
CPig is a Grasshopper plugin with 142 components, three of which (Field Evaluate, Mesh Shell, Alpha Wrap) hard-crashed Rhino in their first audit. The CPig project maintains repro-ready Slop JSON definitions under `C:\Repos\CPig\research\slop_tests\` — one per scenario. Each Slop JSON instantiates a complete CPig pipeline when fed into the Slop component.

This workload reuses Canary's existing Rhino agent + viewport capture + pixel-diff + watchdog. The only CPig-specific surface is:
1. A loader fixture .gh,
2. Three new agent actions (`GrasshopperSetPanelText`, `GrasshopperSetToggle`, `GrasshopperGetPanelText`),
3. A test naming convention.

## Fixture
`workloads/rhino/fixtures/cpig_slop_loader.gh` contains:
- One **Slop** component (`51070A00-51DF-4E00-B000-510DDEF00002`).
- One `GH_Panel` nicknamed `JsonPath` wired to Slop's `Files` input — Canary sets this per test to point at a Slop JSON.
- One `GH_BooleanToggle` nicknamed `Build` wired to Slop's `Build` input — Canary pulses this true to trigger build.
- One `Crash Guard` (CPig diagnostic component, GUID `51070A00-51DF-4E00-C000-510DDEF00012`) with `Enable=true`.
- One `Log Hub` (GUID `51070A00-51DF-4E00-C000-510DDEF00011`) with `Enable=true`.
- Three `GH_Panel`s nicknamed `SlopLog`, `SlopSuccess`, `SlopCount` wired to Slop's `Log` / `Success` / `Count` outputs — tests assert on these.
- Document-level viewport set to a deterministic projection + display mode for stable pixel diffs.

The fixture loads once per test process. Tests differ only in which JSON path they shove into the `JsonPath` panel.

## Test JSON schema (CPig-specific extensions)
Beyond Canary's existing `setup` + `recording` + `checkpoints`, CPig tests use `actions[]` and `asserts[]`:

```json
{
  "name": "cpig-16-field-evaluate",
  "workload": "rhino",
  "description": "Sphere ∪ Box → offset → evaluate at point. Tracks BUG-004 regression.",
  "setup": {
    "file": "fixtures/cpig_slop_loader.gh",
    "viewport": { "projection": "Perspective", "displayMode": "Shaded", "width": 800, "height": 600 }
  },
  "actions": [
    { "type": "GrasshopperSetPanelText", "nickname": "JsonPath", "text": "C:/Repos/CPig/research/slop_tests/16_field_evaluate.json" },
    { "type": "GrasshopperSetToggle",    "nickname": "Build",    "value": true },
    { "type": "WaitForGrasshopperSolution", "timeoutMs": 30000 }
  ],
  "checkpoints": [
    { "name": "post-build", "atTimeMs": 5000, "tolerance": 0.02 }
  ],
  // Optional Phase 4.6.F Session B GIF capture — see the "capture.gif" section below.
  "asserts": [
    { "type": "PanelEquals", "nickname": "SlopSuccess", "text": "True" },
    { "type": "PanelDoesNotContain", "nickname": "SlopLog", "text": "FATAL" },
    { "type": "PanelDoesNotContain", "nickname": "SlopLog", "text": "CRASH" }
  ]
}
```

`actions[]` runs sequentially before checkpoint capture. `asserts[]` runs after each checkpoint. Both extensions are dispatched in `Canary.Harness/TestRunner.cs`.

## Optional GIF capture on a checkpoint (Phase 4.6.F Session B, 2026-06-01)

Each checkpoint can opt into an animated-GIF capture sibling to the static PNG by adding a `capture` sub-object:

```json
{
  "name": "post-build",
  "atTimeMs": 5000,
  "tolerance": 0.02,
  "capture": {
    "gif": true,
    "frameCount": 12,
    "intervalMs": 150
  }
}
```

Field semantics:
- `gif` (bool, default `false`) — turn the GIF path on. When `false`, behaviour is unchanged (single PNG + optional `.fullscreen.png`).
- `frameCount` (int, default `30`) — number of additional viewport frames to grab beyond the main PNG. Each is written as a temporary sibling `{name}.frame{NN}.png` and deleted after encoding.
- `intervalMs` (int, default `100`) — sleep between consecutive frame captures, in milliseconds. The orchestrator converts to GIF centiseconds (rounded, min 1cs = 10ms).

Output: a single animated GIF at `candidates/{name}.gif`, with `loop = ∞` and a uniform per-frame delay. The path is plumbed through `CheckpointResult.GifPath` (orchestrator side) and `ProgressCard.GifPath` (UI). The Avalonia runner card shows the GIF file path as a `🎞️ GIF: …` label; in-card animation playback needs the [AvaloniaGif](https://github.com/AvaloniaUI/avalonia-gif) community package, which is not adopted by this phase.

**Useful when** the viewport changes during the capture window — Grasshopper Animate-style timelines, slider scrub, render progressive reveal. **Useless when** the viewport is static (you'll get N copies of the same frame).

### Per-frame slider scrub (Session B+, 2026-06-01)

Adding a `scrub` sub-object to `capture` switches Canary from the agent-side timer loop (above) to an orchestrator-driven per-frame loop: for each value, the orchestrator calls `GrasshopperSetSlider(nickname, value)` + `WaitForGrasshopperSolution` + a single-frame screenshot, then assembles the resulting PNGs into the GIF:

```json
{
  "name": "post-build",
  "atTimeMs": 5000,
  "tolerance": 0.02,
  "capture": {
    "gif": true,
    "intervalMs": 150,
    "scrub": {
      "nickname": "AnimSlider",
      "values": [0, 200, 400, 600, 800, 1000, 1200, 1400, 1600, 1800, 2000],
      "settleMs": 50,
      "solveTimeoutMs": 10000
    }
  }
}
```

Field semantics:
- `nickname` (string, required) — the Grasshopper slider's NickName. Match is case-insensitive. Slop slider nodes' `name` field becomes the GH NickName.
- `values` (number[], required) — slider values to step through. One frame per entry; `values.Length` becomes the frame count (overrides `frameCount`).
- `settleMs` (int, default 0) — extra wait after `WaitForGrasshopperSolution` returns, before capturing. Use when the canvas has deferred preview / display-cache updates that don't trigger the solve-complete signal.
- `solveTimeoutMs` (int, default 10000) — per-frame upper bound on the solve wait.

The static `post-build.png` is captured normally first; the GIF is built from the N scrub frames. The agent-side timer loop is bypassed when scrub is set (`RecordGif=false` is passed to the agent in that case).

**Fixture-side requirement**: the canvas must have a Grasshopper slider with a stable NickName wired to whatever drives the visible-change. For CPig.Kinematics animated fixtures, this means dropping a Slop `{ type: "slider", name: "AnimSlider", min: 0, max: ... }` node and wiring it to `Animate Bound`'s `Index` input (input 2). `Animate Bound`'s auto-spawned slider has no NickName, so it can't be a scrub target — wire your own. `kin_18_2link_arm.json` is the canonical example.

VLM mode does not consume the GIF (no current LLM provider takes animated input). Pixel-diff mode compares only the static PNG. The GIF is evidence-only.

Encoder: `Canary.Comparison.AnimatedGifEncoder` (wrapping `SixLabors.ImageSharp.Formats.Gif.GifEncoder` — already a `Canary.Core` dependency for pixel-diff, so no new NuGet was added). See `THIRD_PARTY_LICENSES.md`.

Demo fixture: `cpig-kin-18-2link-arm` has `capture.gif=true` + `capture.scrub` on its post-build checkpoint as the end-to-end smoke of the scrub pipeline.

VLM mode does not consume the GIF (no current LLM provider takes animated input). Pixel-diff mode compares only the static PNG. The GIF is evidence-only.

Encoder: `Canary.Comparison.AnimatedGifEncoder` (wrapping `SixLabors.ImageSharp.Formats.Gif.GifEncoder` — already a `Canary.Core` dependency for pixel-diff, so no new NuGet was added). See `THIRD_PARTY_LICENSES.md`.


## Naming convention
Test name `cpig-NN-slug` mirrors the Slop JSON filename `NN_slug.json`. Underscores in the JSON name become hyphens in the test name. Numbering is shared so users can correlate at a glance.

## Crash detection
Canary's existing `Watchdog` already covers this — heartbeats every 2s, dead after 3 misses. When Rhino dies during a CPig test, the result.json gets `Status=Crashed` and `ErrorMessage="Application crashed during test."` That's the original BUG-004/006/007 signature; the test fails red, the next CI run flags it.

For deeper diagnostics when a crash happens:
- CPig writes breadcrumbs to `C:\Repos\CPig\cpig_debug.log` and `%LOCALAPPDATA%\CPig\trace.log`.
- Slop's `Crash Guard` writes `slop_crash_*.txt` to Desktop on managed exceptions.
- Canary stores the test's `result.json` plus per-checkpoint baseline / candidate / diff PNGs.

These artifacts together let a fix-side Claude session reproduce + diagnose without re-running the test by hand.

## New agent actions (added in `RhinoAgent.cs`)

| Action | Parameters | Returns | Purpose |
|---|---|---|---|
| `GrasshopperSetToggle` | `nickname`, `value` (bool) | `actualValue` | Drive Slop's `Build` and `Crash Guard.Enable` from tests. |
| `GrasshopperSetPanelText` | `nickname`, `text` | `length` | Drive Slop's `JsonPath` panel and any other text inputs. |
| `GrasshopperGetPanelText` | `nickname` | `text`, `length` | Read panel content for assertions on Slop's `Log` / `Success`. |

Each follows the existing `HandleGrasshopperSetSlider` pattern: find by case-insensitive nickname, mutate, call `ExpireSolution(true)`. Marshaled to the UI thread via the existing `InvokeOnUi` helper.

## Workflow when adding a new CPig test
1. CPig side: Slop JSON committed at `CPig/research/slop_tests/NN_slug.json` per `Slop/SLOP_STYLE.md`.
2. Canary side: emit `workloads/rhino/tests/cpig-NN-slug.json` from the helper script (`scripts/cpig-test-from-slop.ps1`) or copy an existing test and edit the `path` field.
3. Run the test once. First run records the candidate but no baseline exists, so it fails. Inspect the candidate PNG.
4. If it looks correct, `canary baseline approve cpig-NN-slug` to lock the image as the baseline.
5. Commit both the Slop JSON (CPig repo) and the Canary test JSON.

## New machine / environment checklist
When setting up on a new machine or after a major OS/driver update:
1. Verify `C:\Repos\CPig\` exists and Slop JSON paths in test definitions resolve.
2. Verify `cpig_native.dll` loads — build CPig native (`cmake --preset x64-release && cmake --build --preset x64-release`) and C# (`dotnet build CPig.sln`).
3. Verify Rhino 8 is installed and the Canary Rhino agent plugin (`.rhp`) loads.
4. Verify Slop plugin is installed in Grasshopper.
5. Run `canary run --workload rhino --filter "cpig-00-smoke-ping"` as a smoke test.
6. **Expect baseline mismatches** — different display drivers produce different pixel output. Re-approve all baselines with `canary approve` after visual inspection.
7. Check `cpig_debug.log` and `%LOCALAPPDATA%\CPig\trace.log` if any test crashes.

## Cross-references
- Peer doc: `C:\Repos\CPig\spec\CANARY.md`
- Slop authoring rules: `C:\Repos\Slop\SLOP_STYLE.md`
- Existing Canary spec: `spec/SUPERVISOR.md`, `spec/PHASES.md`, `spec/TESTS.md`
