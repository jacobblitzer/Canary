# PEERS.md — Canary Cross-Repo Contracts

Canary is the cross-application visual regression harness. Its peers are the repos whose work it tests, plus Slop (the JSON-to-Grasshopper bridge that authors Canary's Rhino-workload test definitions).

## Architecture rule

Canary owns the **harness** (test orchestration, agent-to-app bridges, pixel-diff + VLM comparison, baseline storage). Each peer repo owns its **subject** (the code being tested) and its **test definitions** (Slop JSON in `<peer>/tests/slop/` or `<peer>/research/slop_tests/`).

The baseline for any cpig-* / pigture-* / penumbra-* test lives in Canary (`workloads/<workload>/baselines/<test-id>.png`) per STANDARD.md §16 Locked Decision 7 (harness owns the baseline file; subject repo owns the bug).

## CPig (`C:\Repos\CPig\`)

**Role:** Computational geometry plugin for Grasshopper. CPig regression tests run under Canary's `rhino` workload using the Slop loader.

**Canary-side files:**
- `workloads/rhino/fixtures/cpig_slop_loader.gh` — loader fixture (binary)
- `workloads/rhino/fixtures/cpig_slop_loader_generator.json` — Slop JSON that generates the fixture
- `workloads/rhino/tests/cpig-*.json` — 34 test definitions (32 active, 2 excluded for crash risk)
- `workloads/rhino/suites/cpig.json` — test suite
- `workloads/rhino/baselines/cpig-*.png` — committed reference images
- `spec/CPIG_WORKLOAD.md` — workload documentation

**CPig-side files:**
- `research/slop_tests/*.json` — 34 Slop JSON test scenarios (one per cpig-NN test)
- `docs/bugs/NNNN-*.md` — per-bug snapshots that reference cpig-NN tests via the `canary-repro:` frontmatter field

**Contract:**
- Test naming: `cpig-NN-slug` mirrors `NN_slug.json` in `CPig/research/slop_tests/`.
- Standard panel nicknames: `JsonPath`, `Build`, `Cleanup`, `SlopLog`, `SlopSuccess`, `SlopCount`.
- Per STANDARD.md §16, baselines update only via explicit "approve baseline" commits answering What/Why/Evidence.
- Mode selection (pixel-diff / VLM / both) per `canary run --mode` flag. CPig tests carry `setup.vlmDescription` so they run as either flavour.
- Excluded tests (cpig-10, cpig-13) require the CPig BUG-004 mitigation; do not re-enable without re-verifying.

## Penumbra (`C:\Repos\Penumbra\`)

**Role:** WebGPU SDF renderer. Penumbra tests run under Canary's `penumbra` workload using a dedicated CDP agent (browser automation).

**Canary-side files:**
- `workloads/penumbra/agent/` — Canary.Agent.Penumbra CDP bridge (TypeScript)
- `workloads/penumbra/tests/penumbra-*.json` — 61 test definitions across 12 suites
- `workloads/penumbra/suites/*.json` — suite configurations
- `workloads/penumbra/baselines/<scene-id>.png` — committed reference images
- `spec/PENUMBRA_WORKLOAD.md` — workload documentation

**Penumbra-side files:**
- `packages/studio/canary-globals.d.ts` + `canary-hooks.ts` — global hooks Canary calls (`__canaryHideUI`, `__canarySetProgressiveQuality`, `__canaryPersona*`, etc.)
- `packages/studio/` (promoted from `test/` in 2026-05-01) — the harness Canary drives

**Contract:**
- Penumbra exposes deterministic test scenes via the harness query string (`?scene=...`).
- Hooks are mandatory — Canary calls `__canaryHideUI(true)` before screenshots; Penumbra MUST honor this without rendering UI overlays.
- Visual regression mode is the default; VLM mode is opt-in per test definition.
- Hook renames (e.g. `__canaryModule*` → `__canaryPersona*` per Canary commit 074196c) trigger the Cross-Repo Change Protocol — both repos update in coordination.

**Addendum (R1.7, 2026-07-03 — the current contract index):**
- **Browser hooks are now a versioned catalog:** `Penumbra/docs/spec/canary-js-api.md` (66
  hooks, v1; AGENT-flagged ones are hard-coded into Canary.Agent.Penumbra, additive-only;
  conformance vitest Penumbra-side).
- **The IN-RHINO contract** (rhino workload, in-process GLSL studio): Canary reflects into
  `Penumbra.Bridge.GetFrameState()` — the PINNED 7-field table (incl. `BakesOutstanding`,
  additive 2026-07-03) lives in `Penumbra/spec/PEERS.md` § GetFrameState; Canary's drift lock
  is `tests/Canary.Tests/Contracts/FrameStateContractTests.cs`. `WaitForPenumbraFrame
  requireSteady` = Status " steady" AND bakes drained.
- **Rhino-workload Penumbra suites:** `penumbra-glsl` + `cpig-display-matrix` (31 tests; 19
  pixel-diff gates at tolerance 0.005). NOTE: shared-runMode suites keep per-test baselines at
  `workloads/rhino/results/<test>/baselines/` (NOT committed — results/ is gitignored;
  archived to Drive per the audit-c rule at approval time).
- **Session flight recorder:** `docs/session-flight-recorder.md` — manifest schema, MCP 12
  tools (`get_session_manifest`/`get_session_telemetry`), snapshot-on-capture (both sides),
  the 186-kind generated event catalog. §5 black-box acceptance PASSED 2026-07-03.

## Qualia (`C:\Repos\Qualia\`)

**Role:** 3D graph editor (React + Vite + Three.js + WebGPU, with Tauri
desktop wrap). Qualia tests run under Canary's `qualia` workload via a
dedicated CDP bridge agent — the same pattern as Penumbra, but with a
much wider hook surface covering LandingScreen, PersonaRegistry,
qualia-v4 pointers/qverse/RAG UI, the Debug Playground (Wave 0.B), the
RH-2 multi-display perf sweep, and pipeline diagnostic dumps.

**Canary-side files:**
- `src/Canary.Agent.Qualia/` — bridge agent (`QualiaBridgeAgent.cs`,
  `QualiaConfig.cs`, `ViteManager.cs`). Spawns `npm run dev`, launches
  Chrome with `--remote-debugging-port=9223`, drives via
  `Runtime.evaluate` against `window.__canary*` hooks.
- `workloads/qualia/workload.json` — wires `agentType: "qualia-cdp"` +
  `qualiaConfig` block (projectDir, vitePort=5173, cdpPort=9223,
  canvas size, ready timeout, `clearLocalStorageOnInit`).
- `workloads/qualia/tests/*.json` — 78 test definitions
  (diag-*, landing-*, main-*, playground-*, qualia-v4-*, rh2-*).
- `workloads/qualia/suites/*.json` — 6 suites: `landing-screen`,
  `display-modes`, `multi-display`, `pencil-diff`, `playground`,
  `qualia-v4-ui`.
- `workloads/qualia/baselines/` — committed reference PNGs per §16.
- `workloads/qualia/AGENT_NOTES.md` — operator-facing hook + action
  inventory.
- `spec/QUALIA_WORKLOAD.md` — workload specification (this file's
  per-workload counterpart).

**Qualia-side files:**
- `packages/ui/src/canary-hooks.ts` — `installCanaryHooks(deps)` +
  ~50+ `window.__canary*` functions mounted once on App mount.
- `packages/ui/src/debug/Playground.tsx` — installs the
  playground-scoped scenario + snapshot hooks while the overlay is
  mounted.
- `packages/ui/src/App.tsx` — calls `installCanaryHooks` once on
  mount with deps closed over the live registry + LandingScreen +
  Playground state + ready-predicate.
- `packages/ui/src/styles.css` — `.qualia-ui-hidden` selector
  triggered by `__canaryHideUI(true)`.
- `examples/sample/` + `examples/minimal/` — test vaults the suites
  load.
- `spec/PEERS.md` — Qualia-side mirror of this contract.

**Contract:**
- **Hook stability.** Adding a `__canary*` hook is non-breaking;
  removing or renaming one without a coordinated Canary update will
  break tests. Deprecation cycle: rename lands with the old name
  preserved as `@deprecated` alias (`win.X = win.Y`) for one
  transition release, then removed. Example: `__canaryModule*` →
  `__canaryPersona*` rename (Phase 7.2, 2026-05-12); legacy aliases
  still present.
- **Co-existence with Penumbra.** Canary's Penumbra workload uses
  CDP port 9222 + Vite port 3000. Qualia uses 9223 + 5173. Both can
  run side-by-side without port conflicts.
- **VLM model.** Tests targeting VLM mode use `gemma4:e4b` (matches
  Canary's existing convention). Per-checkpoint `mode: "vlm"` wins
  over the `--mode` flag.
- **Baselines per §16.** Committed PNGs under
  `workloads/qualia/baselines/`; candidates + diffs gitignored under
  `workloads/qualia/results/`. Baseline approval follows the three-
  question commit-message format.
- **`runMode: shared`** is NOT yet supported by the Qualia agent.
  Each test spawns a fresh Vite + Chrome (~5-10s overhead).
  Queued — see `spec/QUALIA_WORKLOAD.md` § Open questions.

**Adding a hook:**
1. Extend `CanaryHookDeps` + `installCanaryHooks` in
   `Qualia/packages/ui/src/canary-hooks.ts`.
2. Add the action branch in `QualiaBridgeAgent.ExecuteAsync` IF the
   hook is worth a named action; otherwise tests can call it
   directly via `setup.commands` raw JS.
3. Update `workloads/qualia/AGENT_NOTES.md` + (if the surface area
   changed materially) `spec/QUALIA_WORKLOAD.md`.
4. Append a one-line entry to `MultiVerse/BUILD_LOG.md` per §7.

## Pigture (`C:\Repos\Pigture\`)

**Role:** RDK-backed Cycles render plugin. Pigture tests run under Canary's `rhino` workload (shared with CPig) using the same Slop-loader pattern.

**Canary-side files:**
- `workloads/rhino/fixtures/pigture_slop_loader.gh` — loader fixture
- `workloads/rhino/fixtures/pigture_slop_loader_generator.json` — generator
- `workloads/rhino/tests/pigture-*.json` — test definitions
- `workloads/rhino/suites/pigture.json` — test suite
- `workloads/rhino/baselines/pigture-*.png` — committed render reference images
- `spec/PIGTURE_WORKLOAD.md` — workload documentation

**Pigture-side files:**
- `tests/slop/*.json` — Slop JSON test definitions

**Contract:**
- Render tests require 120 s timeout (Cycles convergence) and 0.08 pixel-diff tolerance (denoiser noise variance).
- Two-phase render pattern (see Pigture's `spec/PEERS.md` for full details): `Run=0` during Build, `Run=1` to trigger RenderScene afterward.
- File-source checkpoints (`"source": "file"`) instead of viewport capture; Pigture-side `SavePath` panel determines the canonical output location.
- Cycles non-determinism (denoiser, sampling) means baselines drift gradually; per STANDARD.md §16, OS-update-related baseline movement logs as routine, intentional material model changes require ADR.

## Slop (`C:\Repos\Slop\`)

**Role:** JSON-to-Grasshopper definition builder. Slop is the test infrastructure for every Rhino-workload test (cpig-*, pigture-*, future pigment-*).

**Canary-side files:**
- All `workloads/rhino/fixtures/*_slop_loader.gh` files — generated from Slop JSON
- All `workloads/rhino/tests/*.json` test definitions reference Slop's panel-nickname convention

**Slop-side files:**
- `SLOP_PROMPT.md` — canonical JSON schema (despite the name, this is the schema doc, not a prompt — see Slop's CLAUDE.md)
- `SLOP_STYLE.md` — authoring conventions Canary tests follow
- `fodder/catalog/gh_catalog_*.json` — Grasshopper component catalog Canary tests resolve GUIDs against

**Contract:**
- Standard panel nicknames are Slop's contract: `JsonPath`, `Build`, `Cleanup`, `SlopLog`, `SlopSuccess`, `SlopCount`. Adding a new convention requires updating SLOP_STYLE.md AND every Canary workload doc that references the convention.
- Slop schema changes (new node types, GUID format shifts) trigger the Cross-Repo Change Protocol — Canary's `spec/CPIG_WORKLOAD.md` and `spec/PIGTURE_WORKLOAD.md` cross-reference Slop's schema and must stay in sync.

## MultiVerse (`C:\Repos\MultiVerse\`)

**Role:** Cross-repo coordination room. Canary changes that affect any peer (workload reorganization, agent contract changes, baseline mass-updates) get logged here.

**Contract per STANDARD.md §7 + §18:** every cross-repo change appends a one-line entry to `MultiVerse/BUILD_LOG.md`:

```
YYYY-MM-DD | <kind> | Canary → AffectedRepos | one-line summary
```

`<kind>` ∈ {`cross-repo`, `release`, `canary`}. See Canary's `spec/CPIG_WORKLOAD.md`, `PENUMBRA_WORKLOAD.md`, `PIGTURE_WORKLOAD.md` for the per-peer workload specs that this PEERS.md indexes.

## Related

- Per-workload specs: `spec/CPIG_WORKLOAD.md`, `spec/PENUMBRA_WORKLOAD.md`, `spec/PIGTURE_WORKLOAD.md`
- VLM convention: `MultiVerse/STANDARD.md` §16 (visual artifacts) and `MultiVerse/CLAUDE.md` "Writing good VLM descriptions" (or §16 once the latter promotes).
- Cross-Repo Change Protocol: `MultiVerse/STANDARD.md` §7.
