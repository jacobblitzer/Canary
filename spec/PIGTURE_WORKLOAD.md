# PIGTURE_WORKLOAD.md — Canary's Pigture Regression Workload

## Purpose
This file documents the conventions for Canary's Pigture regression tests, which run on top of the existing `rhino` workload. The peer document on the Pigture side is `C:\Repos\Pigture\spec\PEERS.md`. Keep them in sync.

## What this workload tests
Pigture is a 29-component Grasshopper plugin that exposes Rhino's RDK (Rendering Development Kit) to the canvas — PBR materials, textures, display modes, and Cycles renders. Tests verify that render graphs produce stable visual output through the bake-capture-delete pipeline.

This workload reuses Canary's existing Rhino agent + viewport capture + pixel-diff + watchdog. It follows the same Slop-loader pattern as CPig, with adjustments for Cycles render convergence:
1. A loader fixture `.gh` (identical structure to CPig's),
2. The same three agent actions (`GrasshopperSetPanelText`, `GrasshopperSetToggle`, `GrasshopperGetPanelText`),
3. Longer timeouts and higher pixel-diff tolerance.

## Key differences from CPig

| Parameter | CPig | Pigture | Reason |
|-----------|------|---------|--------|
| `timeoutMs` | 30000 | 120000 | Cycles render convergence |
| `tolerance` | 0.02 | 0.08 | Cycles noise variance between runs |
| `atTimeMs` (checkpoint) | 5000 | 15000 | Viewport settle after render completion |

## Fixture
`workloads/rhino/fixtures/pigture_slop_loader.gh` contains:
- One **Slop** component (`51070A00-51DF-4E00-B000-510DDEF00002`).
- One `GH_Panel` nicknamed `JsonPath` wired to Slop's `Files` input — Canary sets this per test to point at a Slop JSON in `C:\Repos\Pigture\tests\slop\`.
- One `GH_BooleanToggle` nicknamed `Build` wired to Slop's `Build` input — Canary pulses this true to trigger build.
- One `GH_BooleanToggle` nicknamed `Cleanup` wired to Slop Cleanup's input — Canary pulses this true to tear down the previous graph.
- One `Crash Guard` (GUID `51070A00-51DF-4E00-C000-510DDEF00012`) with `Enable=true`.
- One `Log Hub` (GUID `51070A00-51DF-4E00-C000-510DDEF00011`) with `Enable=true`.
- Three `GH_Panel`s nicknamed `SlopLog`, `SlopSuccess`, `SlopCount` wired to Slop's `Log` / `Success` / `Count` outputs — tests assert on these.

The fixture is a binary `.gh` file generated from `pigture_slop_loader_generator.json` by feeding it to a Slop component in Grasshopper. It loads once per test process. Tests differ only in which JSON path they shove into the `JsonPath` panel.

## Test JSON schema
Same as CPig — see `spec/CPIG_WORKLOAD.md` for the full schema. Pigture tests follow the same `setup` + `actions[]` + `checkpoints[]` + `asserts[]` structure.

Example:
```json
{
  "name": "pigture-01-render-rounded-box",
  "workload": "rhino",
  "setup": {
    "file": "fixtures/pigture_slop_loader.gh",
    "viewport": { "projection": "Perspective", "displayMode": "Shaded", "width": 800, "height": 600 }
  },
  "actions": [
    { "type": "GrasshopperSetToggle",    "nickname": "Cleanup", "value": true },
    { "type": "WaitForGrasshopperSolution", "timeoutMs": 5000 },
    { "type": "GrasshopperSetToggle",    "nickname": "Cleanup", "value": false },
    { "type": "GrasshopperSetPanelText", "nickname": "JsonPath", "text": "C:/Repos/Pigture/tests/slop/01_render_rounded_box.json" },
    { "type": "GrasshopperSetToggle",    "nickname": "Build",   "value": true },
    { "type": "WaitForGrasshopperSolution", "timeoutMs": 120000 }
  ],
  "checkpoints": [
    { "name": "post-render", "atTimeMs": 15000, "tolerance": 0.08 }
  ],
  "asserts": [
    { "type": "PanelEquals", "nickname": "SlopSuccess", "text": "True" },
    { "type": "PanelDoesNotContain", "nickname": "SlopLog", "text": "FATAL" }
  ]
}
```

## Naming convention
Test name `pigture-NN-slug` mirrors the Slop JSON filename `NN_slug.json` in `C:\Repos\Pigture\tests\slop\`. Underscores in the JSON name become hyphens in the test name.

## Crash detection
Same as CPig — Canary's `Watchdog` covers this with heartbeats every 2s, dead after 3 misses. When Rhino dies during a Pigture test, `result.json` gets `Status=Crashed`.

For Pigture-specific diagnostics:
- Slop's `Crash Guard` writes `slop_crash_*.txt` to Desktop on managed exceptions.
- Canary stores per-checkpoint baseline / candidate / diff PNGs.

## Workflow when adding a new Pigture test
1. Pigture side: commit Slop JSON at `Pigture/tests/slop/NN_slug.json`.
2. Canary side: create `workloads/rhino/tests/pigture-NN-slug.json` (copy an existing test, edit the `JsonPath` text field).
3. Add the test name to `workloads/rhino/suites/pigture.json`.
4. Run once — first run records a candidate but no baseline exists, so it fails. Inspect the candidate PNG.
5. If correct: `canary baseline approve pigture-NN-slug` to lock the baseline.
6. Commit both the Slop JSON (Pigture repo) and the Canary test JSON.

## Manual step: generating the fixture `.gh`
The `pigture_slop_loader.gh` fixture is a binary GH file that must be generated interactively:
1. Open Grasshopper in Rhino.
2. Drop a Slop component on the canvas.
3. Feed `pigture_slop_loader_generator.json` to Slop's Files input, set Build=true.
4. Save the resulting document as `workloads/rhino/fixtures/pigture_slop_loader.gh`.

Until this is done, the test definitions are valid but won't run.

## New machine / environment checklist
1. Verify `C:\Repos\Pigture\` exists and Slop JSON paths resolve.
2. Verify `GrasshopperRender.gha` is built and installed (`dotnet build GrasshopperRender.csproj`).
3. Verify Rhino 8 is installed and the Canary Rhino agent plugin (`.rhp`) loads.
4. Verify Slop plugin is installed in Grasshopper.
5. Run `canary run --workload rhino --filter "pigture-01-render-rounded-box"` as a smoke test.
6. **Expect baseline mismatches** — different GPUs produce different Cycles output. Re-approve baselines after visual inspection.

## Cross-references
- Peer doc: `C:\Repos\Pigture\spec\PEERS.md`
- CPig workload (same pattern): `spec/CPIG_WORKLOAD.md`
- Slop authoring rules: `C:\Repos\Slop\SLOP_STYLE.md`
- Existing Canary spec: `spec/SUPERVISOR.md`, `spec/PHASES.md`, `spec/TESTS.md`
