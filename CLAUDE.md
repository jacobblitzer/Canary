---
type: repo
repo: Canary
phase: 13
phase_name: "Phase 13 in progress"
status: active
last_audit: 2026-05-08
test_count: 72
component_count: null
peers: [CPig, Penumbra, Slop, Pigture]
tags: [multiverse, repo]
---

# CLAUDE.md

## Project: Canary — Cross-Application Visual Regression Testing Harness

### Quick Reference
- **Build**: `dotnet build Canary.sln` (must be 0 errors, 0 warnings)
- **Test**: `dotnet test tests/Canary.Tests/Canary.Tests.csproj --filter "Category=Unit"`
- **Run Penumbra tests**: `canary run --workload penumbra`
- **Run CPig tests**: `canary run --workload rhino --suite cpig` (from `C:\Repos\Canary`)
- **Status**: Phase 13 in progress (checkpoints 13.1–13.4 complete, 13.5 baselines pending). 72 unit tests + 22 cpig test definitions.

### Active architecture initiative — Penumbra A3 multiscale foundation + 7 graduated features (PR #6 + #7 + #8 merged 2026-05-08)

Penumbra shipped the A3 progressive coarse → medium → fine brick
baking foundation plus 6 follow-on graduations on top of it. Of
the 14 Phase 0 feature stubs, **7 are now real implementations**:

| Feature | Real impl | Behavior |
|---|---|---|
| A3 progressiveBrickBaking | 2026-05-08 | Async 3-tier bake (2³ → 4³ → 8³) with RAF yields between tiers. ~50ms coarse / ~250ms medium / ~2.5s fine. User sees first chunky pixel ~8× sooner. |
| A4 resolutionRampAtlasBuild | 2026-05-08 | Caps render resolution per A3 tier (0.25× → 0.33× → 0.5× → 1.0×). Composes multiplicatively with motion-aware ProgressiveQualityController. |
| C2 eventDrivenRender | 2026-05-07 | EventEmitter for brick-complete + (new) tier-complete events. Render gates on dirty flag. |
| C4 silhouetteFirstBake | 2026-05-08 | Reorders dispatch list by camera-near priority WITHIN each A3 tier. |
| C9 meshBootstrapRaymarch | 2026-05-07 | CPU dual-contouring mesh provides initial-t guess; rays start from mesh hit. |
| C1 persistentThreadBake | 2026-05-08 | Coarse + medium A3 tier dispatches use atomic-claim work loop (E5 7g pattern). Best on multi-SM discrete; iGPU change negligible. |
| A6 splatCache (foundation) | 2026-05-08 | marchRay atomicOr writes dirty-cell markers on tape-fallback. `cascadeManager.drainDirtyCells()` API. **Backfill-build trigger pending Phase 11.5.** |

Plus **E5 compute marcher early-start** (free-rider on A3): the
compute marcher inherits tier-aware sampling automatically through
`main-atlas-helpers.wgsl` concatenation, so it can render during
A3 build window.

**Compatibility matrix changes** (2026-05-08): A-series and
C-series features all compose post-graduation. Hard mutex pairs
remain only between alternative storage backends (B1 / B2 / B5 —
indirection structures) and alternative classification sources
(B1 / B2 / C8). No soft invalidates remain.

**Storage-buffer budget** (post-PR #8): A3's tier state + tier-
slot translation packed into ONE storage buffer (frees `@group(1)
@binding(16)` for A6's dirty-cells). Total fragment-stage count
stays at 16/16 (the negotiated max).

**Pending Canary work** (Penumbra Phase 12, not yet shipped on
this side):
- `atlas-blob-progressive-{coarse,medium,fine}.json` — capture
  per-tier outputs to verify progressive UX
- `atlas-blob-{a4-res-ramp,c4-silhouette-first,c1-persistent}.json`
  — toggle isolation per graduated feature
- `atlas-blob-a6-splatcache-foundation.json` — A6 ON; verify
  drainDirtyCells returns non-empty after camera reveal
- New CDP hooks: `__canaryWaitForTier(tier)`,
  `__canaryGetTierBuildState()`, `__canarySplatCacheStats()`,
  `__canaryDrainDirtyCells()` (programmatic A6 verification)
- Suite: `progressive-bake.json`

**Debugging-prep doc** at
`Penumbra/docs/debug-sessions/2026-05-08-progressive-bundle-debugging-prep.md`
documents 8 test scenarios + per-feature off-ramps for the next
session.

See `Penumbra/docs/research/2026-05-08-a3-followon-phases.md` for
the resumption pointer; `feature-loader-mutex-rejection` should be
retargeted (the A3+A6 invalidates pair it covered no longer
exists).

### Active architecture initiative — Penumbra Phase 0 feature loader (ADR 0015, shipped 2026-05-07)

Penumbra now ships a `DisplayState.features` axis with 14 toggles for
research / progressive features (A3, A4, A6, B1, B2, B4, B5, C1–C4, C6,
C8, C9). The Canary surface for this is:

- **5 new `__canary*` hooks** wired in `packages/studio/canary-hooks.ts`:
  `__canaryGetFeatureStatus(key)`, `__canaryGetAllFeatureStatus()`,
  `__canaryLoadFeatureProfile(name)`,
  `__canaryValidateCurrentFeatures()`,
  `__canaryCaptureFeatureEvents(durationMs)`. All follow the
  never-rejects pattern (`{ok, ...}` envelope).
- **`__canaryGetEvalSnapshot()` extended** with a `featureToggles` field
  so every screenshot's metadata includes which feature stubs were on
  at capture time.
- **5 new fixtures** under `workloads/penumbra/tests/feature-loader-*.json`:
  `feature-loader-all-off` (pixel-identical regression baseline),
  `feature-loader-performance-profile`,
  `feature-loader-quality-profile`,
  `feature-loader-mutex-rejection` (verifies B1+B2 mutex; A3+A6 soft
  invalidates were removed 2026-05-08 when both graduated and were
  found to compose — the fixture should be retargeted at a still-
  extant invalidates pair when one is added in the future),
  `feature-loader-stub-wiring` (toggles each of 14
  features one-at-a-time, asserts `activations >= 1`).
- **Suite** `workloads/penumbra/suites/feature-loader.json` runs all 5.

Feature toggles flow through the existing generic `__canarySetDisplayState`
hook — no per-feature setter needed. Profile-name shortcuts available
via `__canaryLoadFeatureProfile('default'|'performance'|'quality')`. The
matrix rejects mutex pairs at toggle time; soft invalidates are accepted
but produce a `DisplayBlocker` entry. See Penumbra ADR 0015 + the
explained doc at
`C:/Repos/Penumbra/docs/research/2026-05-06-progressive-atlas-options-explained.md`.

### Active architecture initiative — Penumbra display-preset workload (Phase 5 shipped 2026-05-03)

`TestSetup.DisplayPreset` (string, optional) wires named Penumbra
`DisplayState` presets into the test harness. When set, `TestRunner`
dispatches `LoadDisplayPreset` on the agent before the first checkpoint;
`PenumbraBridgeAgent` evaluates `pass.loadDisplayPreset(name)` (or
`renderer.loadDisplayPreset(name)` in Studio mode) via CDP and records
the resulting `displayMode` / `atomMode` / `vizMode` in the run log for
repro. Unknown preset names log a warning + no-op rather than crash the
run.

8 preset-driven tests under `workloads/penumbra/tests/preset-*.json`,
two suites: `display-smoke` (3-test fail-fast subset for CI) and
`display-matrix` (full 8-preset sweep). Both modes (`--mode pixel-diff`
and `--mode vlm`) supported; per-preset `vlmDescription` baked into
every test.

Run:
```
canary run --workload penumbra --suite display-matrix
canary run --workload penumbra --suite display-smoke --mode vlm
```

See `spec/PENUMBRA_WORKLOAD.md` for the contract and Penumbra ADR 0011
for the parent design.

### Active architecture initiative — Penumbra per-atom brick allocator (Wave 1 / E1+E2, in-progress 2026-05-05)

Penumbra is shipping a per-atom brick allocator (Phase 11.4 M2++,
ADR 0012 + 0014 follow-on) on the `feat/e1-per-atom-brick-allocator`
branch. The flag `render.useAtomBrickAllocator: boolean` (default
`false` until soak completes) routes atlas-eval atoms through a
multi-slot per-cell indirection so atoms sharing a coarse cell each
own their own brick — fixing the multi-field-three-spheres
last-write-wins bug. With sub-brick refinement on, each atom can
independently subdivide a shared cell (per-atom `SubBrickPool`
nodes via the `ownerAtomIndex` API).

Two new test fixtures under `workloads/penumbra/tests/`:
- `multi-field-atom-brick-allocator-on.json` — flag on, no
  refinement. Pins the multi-slot per-cell list path on the
  Multi-Field scene.
- `multi-field-atom-brick-allocator-refined.json` — flag on, +
  `subBrickRefinement: true`. Pins the per-atom subdivision path.

Suite: `atom-brick-allocator.json` runs both. Pixel-diff baselines
are approved on first run.

Run:
```
canary run --workload penumbra --suite atom-brick-allocator
```

Comparison baseline: `multi-field-orbit` runs the same scene with
the flag default off; pixel-diffing the two confirms the per-atom
path produces correct output. Step 8 of the E1+E2 plan flips the
default — at which point this suite continues to pin the same
behavior under the new default.

### Active architecture initiative — Penumbra Tier D1 directional Lipschitz (Wave 3 Phase 3, shipped 2026-05-05)

Penumbra now writes per-brick directional Lipschitz polynomials
into a 64-coefficient buffer per atlas slot during populate.wgsl
(Bernstein-trivariate degree-3 tensor product, sampled gradient
magnitudes at 4×4×4 = 64 brick-local grid points). main-atlas.wgsl's
marchRay reads `lipschitzAtBrick(slotIdx, rd)` and divides stepDist
by the per-direction L (with `max(L, 1.0)` sphere-tracing safety
floor). Replaces the legacy global-Lipschitz constant; expected
visual benefit is tighter step sizes through bricks where local L
varies by direction (CSG kinks, noise-displaced surfaces).

Four test fixtures + expanded suite under `workloads/penumbra/`:
- `multi-field-d1-lipschitz.json` — baseline atlas + per-atom path
  with D1 unconditional. 3 checkpoints.
- `multi-field-d1-lipschitz-refined.json` — D1 + sub-brick
  refinement. 2 checkpoints + multiscale signal capture.
- `assembly-d1-lipschitz.json` (Phase 5c, 2026-05-05) — D1 on the
  Mechanical Assembly scene. Exercises CSG-edge thin features
  where D1's per-direction L should benefit most. 3 checkpoints.
- `atlas-blob-d1-lipschitz.json` (Phase 5c, 2026-05-05) — D1 on
  the 12-Sphere smooth blob. Sanity for no-regression on
  uniform-Lipschitz geometry. 2 checkpoints, tight tolerance.

Suite: `d1-lipschitz.json` runs all four. Pixel-diff baselines
approved on first run.

### Active architecture initiative — Penumbra D3 tricubic + Sturm root isolation (Wave 3 Phase 6, shipped 2026-05-05)

Penumbra's hero-quality tier: replace trilinear SDF interpolation
with Catmull-Rom tricubic (4-point cubic per axis, 64 atlas
samples per query) AND replace sphere-trace step convergence
with analytic Sturm-sequence root isolation on the cubic-along-
ray polynomial (degree 9). ~10× perf cost vs trilinear; visibly
sharper CSG edges + sub-pixel detail on archviz-grade scenes.
Per Ban et al. 2025 / Sigg & Hadwiger 2005.

Phase 6 sub-phases (all shipped 2026-05-05):
- 6a: tricubic CPU foundation — catmullRomBasis,
  tricubicEvaluate, tricubicGradient. 22 unit tests.
- 6b: tricubic WGSL port at `wgsl/tricubic.wgsl`.
- 6c: Sturm-sequence root isolation CPU — buildSturmSequence,
  countRealRoots, findFirstRoot, findAllRoots. 35 unit tests.
- 6d: Sturm WGSL port at `wgsl/sturm.wgsl` + CPU cubic-along-
  ray polynomial extraction (`runtime/src/ray-polynomial.ts`).
  17 unit tests.
- 6e: DisplayState `render.tricubicEnabled` axis + AtlasUniforms
  uniform plumbing (struct grew 80 → 96 bytes for the toggle +
  16-byte alignment padding).
- 6f: Three Canary fixtures + suite locking the toggle's no-
  regression contract.

The actual main-atlas.wgsl marchRay branch wiring (sample-fetch
helper + tricubic+Sturm refinement at hit detection) is a
deferred follow-on. Today the toggle is INERT — pixel-matches
the corresponding toggle-OFF baselines. When the marchRay branch
wiring ships, baselines re-approve to reflect the quality lift.

Three new test fixtures + suite under `workloads/penumbra/`:
- `multi-field-d3-tricubic-on.json` — Multi-Field, 2 checkpoints.
- `assembly-d3-tricubic-on.json` — Mechanical Assembly,
  2 checkpoints (highest-impact scene for D3).
- `atlas-blob-d3-tricubic-on.json` — 12-Sphere Blob, 1 checkpoint
  (sanity no-regression on smooth unions).

Suite: `d3-tricubic.json` runs all three. Pixel-diff baselines
approved on first run; pixel-match the corresponding toggle-OFF
fixtures.

Run:
```
canary run --workload penumbra --suite d3-tricubic
```

### Active architecture initiative — Penumbra E6 mesh-export determinism (Wave 3 Phase 5d, shipped 2026-05-05)

Penumbra's E6 mesh-extraction pipeline (cascade → samplers →
marching cubes → welding → normals → OBJ/STL) is pure CPU. Phase
5d locks it against silent regressions via a programmatic
(numeric) determinism check — separate from the GPU pixel-diff
path covered by `d1-lipschitz` + `multiscale-overlays`.

New CDP hook: `__canaryExtractMeshDeterminism()` returns:
- `vertexCount`, `triangleCount`, `brickCount`
- `bbox: { min, max }` (rounded to 4 decimals)
- `vertexHash`, `indexHash`, `normalHash` — FNV-1a 32-bit hashes
  of the raw Float32Array / Uint32Array bytes; cross-hardware
  safe because the pipeline is CPU-only with the same tape
  evaluator everywhere
- `sampleVertices: [9 floats]`, `sampleNormals: [9 floats] | null`
  — first 3 vertices/normals (rounded), for human inspection
- `elapsedMs`, `cellsPerBrickEdge: 8`, `weldEpsilon` — runtime
  parameters used (matches Studio export defaults)

Three new fixtures + suite under `workloads/penumbra/`:
- `multi-field-mesh-determinism.json` — Multi-Field scene
- `atlas-blob-mesh-determinism.json` — 12-Sphere Blob (smooth
  union; densest MC vertex emission)
- `assembly-mesh-determinism.json` — Mechanical Assembly (CSG
  difference; multi-atom dispatch path)

Suite: `mesh-export-determinism.json` runs all three. Each test's
`commands` array invokes the hook and captures the JSON-stringified
summary — that summary IS the golden. Hash divergence on any
field → test failure. The companion pixel-diff suites cover GPU
output; this suite catches CPU-side regressions like swapped MC
tri-table entries, off-by-one in welding's grid key, or flipped
normal directions.

Run:
```
canary run --workload penumbra --suite mesh-export-determinism
```

### Active architecture initiative — Penumbra E5 Compute marchRay (Wave 3 Phase 7, merged 2026-05-07)

Penumbra ported the fragment-stage marchRay to a WebGPU **compute**
pipeline so workgroup-level ray batching, persistent threading, and
GPU marching-cubes mesh extraction become possible. Sub-phases 7b/3
→ 7g shipped on main; 7h (GPU marching cubes) and 7i (default flip
+ retire fragment marchRay) pending.

**Critical constraint**: per-dispatch GPU time on Intel iGPU is at
91% of the Windows TDR ceiling (1367ms of 1500ms safe budget).
Adding features must fit under that ceiling per-hardware. Several
features ship behind compile-time override constants (D2 cubic,
bisection, D5 back-face, materials) — infrastructure-correct but
firing crashes the iGPU; stronger-GPU smokes regression-gate them.

**3 new `__canary*` hooks** wired in `packages/studio/canary-hooks.ts`:
- `__canarySetComputeMarchToggles(partial)` — granular per-feature
  enables (`tileCount`, `enablePersistentThreading`,
  `persistentWorkgroupCount`, `enableD2Cubic`, `enableBisection`,
  `enableD7bCsgStep`, etc.). Routes through
  `displayState.render.computeMarchToggles`.
- `__canaryRunComputeCalibration()` — runs the calibration phase
  that measures sceneSDF cost per hit on the current hardware,
  caches the result for the budget allocator.
- `__canaryRunComputeMarcherDeterminism(samples)` — runs N
  consecutive compute dispatches and returns hash-divergence + first
  diverging-byte info; locks the GPU path against
  determinism regressions.

**12 new fixtures** under `workloads/penumbra/tests/atlas-blob-compute-*.json`:
- `atlas-blob-compute-smoke.json` — baseline minimal compute
  marcher (gate that the path live + counter increments).
- `-tiled.json` — Strategy S2 tile dispatch (multiple sequential
  dispatches per frame).
- `-d2cubic.json`, `-bisection.json` — D2 cubic refinement +
  6-iter bisection fallback (override-constant gated).
- `-d7c.json`, `-3b3a.json` — D7c smooth-min uplift + 3B/3A
  safety layers.
- `-determinism.json` — wraps `__canaryRunComputeMarcherDeterminism`.
- `-d3tricubic.json` — D3 tricubic + Sturm at hit detection.
- `-wg4.json`, `-wg16.json` — Phase 7f workgroup-size variants.
- `-lighting.json`, `-materials.json` — Phase 7e+ Lambert lighting
  + material lookup (override-constant gated).

Suite: `compute-marcher.json` runs all 12.

Run:
```
canary run --workload penumbra --suite compute-marcher
```

Resumption pointer for 7h/7i:
`Penumbra/docs/research/2026-05-06-compute-marcher-tdr-strategies.md`
(comprehensive strategy + task tables S1-S17, T1-T21 + toggle
infrastructure). Companion docs:
`2026-05-06-7d-tdr-budget-plan.md` (calibration + adaptive allocator
design).

Run:
```
canary run --workload penumbra --suite d1-lipschitz
```

Cross-pixel comparison baseline: `multi-field-orbit` ran with
constant-Lipschitz pre-D1; D1 should produce near-identical
output (per-direction L is tighter or equal to global L; never
larger when geometry is uniform). Visible improvements expected
on thin features and CSG-edge surfaces.

### Test mode duality (`--mode` flag)
Every Canary test definition is mode-agnostic. The runtime selects how to evaluate:

```
canary run ... --mode pixel-diff   # default — visual regression vs baseline
canary run ... --mode vlm          # semantic correctness via Ollama / Claude
canary run ... --mode both         # run each checkpoint twice; report both verdicts
```

Visual regression is the unit-test-equivalent (catches code-stability deltas); VLM is the correctness oracle (catches semantic errors a baseline would silently encode). Per-checkpoint `mode: "vlm"` in the test JSON still wins over the flag. See [`MultiVerse/CLAUDE.md` § Testing modes](../MultiVerse/CLAUDE.md#testing-modes--vlm-vs-visual-regression) for the canonical when-to-use-which guidance. Implementation lives in `src/Canary.Harness/Cli/RunCommand.cs` (flag) + `src/Canary.Core/Orchestration/TestRunner.cs` (`ModeOverride` enum + dispatcher). Mode override resolution: per-checkpoint `mode == "vlm"` wins, otherwise `--mode` applies, otherwise pixel-diff.

### Logging — Slop test JSONs auto-tap every component output
Both modes need behavioural visibility into the canvas, not just final viewport pixels. Slop's `Log Tap` is a pass-through wiretap that records each cpig-component output as it flows downstream. Test authors should wrap every "subject of test" component output with a tap; CPig's retopo generator does this automatically (`CPig/scripts/gen_retopo_slop_tests.py` `_auto_insert_log_taps`). When debugging a failing Canary run, the per-stage tap entries in Slop's `LogHub` file are the first place to look — they show where the data flow went empty / wrong before the screenshot capture. See [`MultiVerse/CLAUDE.md` § Logging in Slop tests](../MultiVerse/CLAUDE.md#logging-in-slop-tests--every-components-behaviour-every-run).

### Running CPig Tests
Always use `--suite cpig` to run CPig tests, not individual `--test` invocations. All CPig tests declare `runMode: shared`, which means Canary launches Rhino **once** and runs all tests sequentially in that single instance. Running tests individually with `--test` defeats this — each invocation opens and closes Rhino separately. The suite approach is faster and matches the intended workflow.

```bash
# Correct — single Rhino instance, all tests sequential
cd C:\Repos\Canary
canary run --workload rhino --suite cpig

# Wrong — opens/closes Rhino for each test
canary run --workload rhino --test cpig-19-noise-field
canary run --workload rhino --test cpig-20-domain-modifiers
```

### Running Pigture Tests
Same shared-suite pattern as CPig: `canary run --workload rhino --suite pigture`.

Pigture checkpoints use `"source": "file"` instead of `"source": "viewport"` (the default). Viewport screenshots capture Rhino's Shaded display mode, not the Cycles render. The rendered image is saved to disk by RenderViewer, and its path flows through a `RenderFilePath` GH panel. At checkpoint time, the runner reads that panel via `GrasshopperGetPanelText`, copies the file to `candidates/`, and runs normal pixel-diff comparison.

Key `TestCheckpoint` fields for file-source:
- `"source": "file"` — use file instead of viewport capture
- `"panelNickname": "RenderFilePath"` — which GH panel holds the file path

See `spec/PIGTURE_WORKLOAD.md` for the full pattern.

### Before Any Work
Read `spec/SUPERVISOR.md` — single source of truth for build decisions.

### Spec Files (read in order)
1. `spec/SUPERVISOR.md` — Orchestration, constraints, gate checklists, dependency matrix
2. `spec/ARCHITECTURE.md` — System design, IPC protocol, comparison engine, two-process model
3. `spec/PHASES.md` — Build phases with checkpoints (0–7)
4. `spec/PHASES_UI.md` — Build phases with checkpoints (8–13: Core extraction + WinForms GUI + CPig workload)
5. `spec/TESTS.md` — Unit and integration test specifications (0–7)
6. `spec/TESTS_UI.md` — Test specifications (8–12)
7. `spec/CPIG_WORKLOAD.md` — Conventions for the CPig regression workload (Phase 13). Peer doc: `C:\Repos\CPig\spec\CANARY.md`.
8. `spec/PIGTURE_WORKLOAD.md` — Conventions for the Pigture render workload. Peer doc: `C:\Repos\Pigture\spec\PEERS.md`.

### Key Rules
- **Namespace**: `Canary` (core + harness), `Canary.Agent` (shared), `Canary.Agent.*` (per-app)
- **Framework**: `net8.0-windows` (Core, Harness, UI), `net8.0;net48` (Agent), `net48` (Rhino)
- **IPC**: Named pipes + JSON-RPC only — no HTTP, no sockets
- **Screenshots**: Captured by agent inside the app, not by the harness
- **Ctrl+C**: Must always work. Display "Press Ctrl+C to abort" in status output
- **Tests**: `[Trait("Category", "Unit")]` headless, `[Trait("Category", "Integration")]` needs app

### Documentation Structure
```
docs/
  bugs/           # One .md per bug (Dataview-queryable frontmatter)
  debug-sessions/ # Investigation journals
  decisions/      # Architecture Decision Records (MADR format)
  features/       # Feature status tracking
  research/       # Deep-dive research reports (techniques, literature, trade-offs)
  templates/      # Reusable templates for all doc types
CHANGELOG.md      # Keep a Changelog format
BUILD_LOG.md      # Phase checkpoint records
```

### Cross-Repo Change Protocol

**This rule is mandatory.** When your session's changes affect other repos (new features they consume, contract changes, schema changes, corrected documentation):

1. **Update `CLAUDE.md` in every affected repo.** This is the #1 priority — CLAUDE.md is what the next Claude Code session reads first. Add or revise the section describing the capability/contract. If a new pattern was established (like file-source checkpoints), document it where future sessions will find it.

2. **Update `spec/PEERS.md`** in every affected repo that has one. Keep contract descriptions, input/output mappings, and GUID tables current.

3. **Log to MultiVerse.** Append a one-line entry to `C:\Repos\MultiVerse\BUILD_LOG.md`:
   ```
   YYYY-MM-DD | cross-repo | Canary → AffectedRepos | one-line summary
   ```

**What triggers this:** Any change that would leave another repo's CLAUDE.md or PEERS.md stale. Adding a `TestCheckpoint` field → update Pigture/CPig CLAUDE.md. Adding an agent action → update repos whose tests use it. Changing test conventions → update Slop CLAUDE.md if it affects JSON authoring.

### Auto-Journaling Rules

**These rules are mandatory.** When working in this repo, maintain living documentation:

1. **After fixing a bug**: Create `docs/bugs/NNNN-short-name.md` using the template. Include frontmatter with `status`, `severity`, `project`, `component`. Add a `### Fixed` entry to `CHANGELOG.md` under `[Unreleased]`.

2. **After completing a feature or significant change**: Add entry to `CHANGELOG.md` under the appropriate category (`Added`, `Changed`, `Removed`, `Fixed`). Update `docs/features/FEATURE_STATUS.md` if a feature's status changed.

3. **After a debug investigation** (whether or not it leads to a fix): Create `docs/debug-sessions/YYYY-MM-DD-short-name.md` using the template. Document hypothesis, evidence, conclusion.

4. **After a significant architectural decision**: Create `docs/decisions/NNNN-short-name.md` using the MADR template. Reference the relevant spec section.

5. **After a research deep-dive** (literature review, technique survey, performance analysis, or architecture exploration): Create `docs/research/YYYY-MM-DD-short-name.md` using the research template. Document the question, sources consulted, findings, and actionable conclusions. Link to any resulting decisions or bugs.

6. **After a build/test run**: Append to `BUILD_LOG.md` following existing format (date, status, tests run/passed/failed, issues, resolution).

7. **Frontmatter schema** (use consistently across all docs):
   ```yaml
   date: YYYY-MM-DD          # ISO 8601
   tags: [bug, feature, ...]  # From: bug, feature, decision, debug-session, research, penumbra, canary, rhino
   status: open | in-progress | resolved | accepted | deprecated
   project: canary | penumbra  # Which project this relates to
   severity: critical | high | medium | low  # Bugs only
   component: "..."           # Subsystem (e.g., atlas, cdp, tape-compiler, comparison)
   ```

### Commit Messages
Use conventional commits: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`.
