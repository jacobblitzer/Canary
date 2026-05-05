---
type: repo
repo: Canary
phase: 13
phase_name: "Phase 13 in progress"
status: active
last_audit: 2026-04-30
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

Two new test fixtures + suite under `workloads/penumbra/`:
- `multi-field-d1-lipschitz.json` — baseline atlas + per-atom path
  with D1 unconditional. 3 checkpoints.
- `multi-field-d1-lipschitz-refined.json` — D1 + sub-brick
  refinement. 2 checkpoints + multiscale signal capture.

Suite: `d1-lipschitz.json` runs both. Pixel-diff baselines
approved on first run.

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
