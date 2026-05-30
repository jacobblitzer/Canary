---
type: spec
project: Canary
component: workload
status: accepted
date: 2026-05-24
peer_doc: ../../Qualia/spec/PEERS.md
related:
  - PEERS.md
  - PENUMBRA_WORKLOAD.md
  - ../workloads/qualia/AGENT_NOTES.md
---

# Qualia Workload — browser-driven CDP harness with persona-rich hook surface

## Purpose

Drive comprehensive auto-tests against Qualia (3D graph editor — React +
Vite + Three.js + WebGPU + Tauri desktop wrap) using the same CDP pattern
as the Penumbra workload, but with a much wider hook surface covering
LandingScreen, PersonaRegistry, the qualia-v4 pointer/qverse/RAG UI,
the Debug Playground (Wave 0.B), the RH-2 multi-display perf sweep, and
diagnostic dumps for pipeline state.

Promoted from `workloads/qualia/AGENT_NOTES.md` per
`MultiVerse/audit/2026-05-24-testing-canary-audit-and-plan.md` (R2 phase
of the testing+Canary reconcile pass). AGENT_NOTES.md stays as the
operator-facing quick reference; this spec is the contract for cross-repo
coordination.

## How it differs from existing workloads

`cpig` + `pigture` workloads drive Rhino + Grasshopper via the Slop
loader (Rhino agent over named pipes, Slop JSON authored from Slop's
catalog). `penumbra` drives Penumbra Studio via CDP. `qualia` drives
Qualia's React + Three.js + WebGPU stack via CDP, but with a much wider
state surface than Penumbra:

- 6 shipped suites, 78 test fixtures (May 2026 snapshot).
- ~50+ `__canary*` hooks across readiness, persona registry, landing
  screen, playground, qualia-v4 pointers / qverse / RAG, RH-2 perf
  snapshot, diagnostic dump.
- Co-existence with Penumbra workload by design — separate CDP port
  (9223 vs 9222) + Vite port (5173 vs 3000).
- `setup.commands` runs raw JS via `Runtime.evaluate` — most new
  fixtures don't need new bridge-agent C# actions (just hook + test
  JSON).

## Workload layout

```
Canary/workloads/qualia/
├── workload.json        # see "Configuration" below
├── AGENT_NOTES.md       # operator-facing hook + action reference
├── tests/               # 78 test definitions (diag-*, landing-*,
│                          main-*, playground-*, qualia-v4-*, rh2-*)
├── suites/              # 6 suites:
│                          landing-screen.json, display-modes.json,
│                          multi-display.json, pencil-diff.json,
│                          playground.json, qualia-v4-ui.json
├── baselines/           # committed reference PNGs per §16
└── results/             # ephemeral; gitignored
```

## Cross-repo file map

**Canary-side:**

- `src/Canary.Agent.Qualia/` — bridge agent (`QualiaBridgeAgent.cs`,
  `QualiaConfig.cs`, `ViteManager.cs`). Spawns `npm run dev`, launches
  Chrome with `--remote-debugging-port=<cdpPort>`, navigates to the
  Vite URL, drives via CDP `Runtime.evaluate` against the `__canary*`
  hooks installed by the app.
- `workloads/qualia/workload.json` — `agentType: "qualia-cdp"`; carries
  the `qualiaConfig` block (see Configuration).
- `workloads/qualia/AGENT_NOTES.md` — per-action mapping table +
  hook surface inventory (operator-facing).
- `workloads/qualia/tests/*.json` — 78 fixtures.
- `workloads/qualia/suites/*.json` — 6 suites.

**Qualia-side (the hook contract):**

- `packages/ui/src/canary-hooks.ts` — `installCanaryHooks(deps)` + the
  full set of `window.__canary*` functions. Mounted from `App.tsx`
  once on first render with deps closed over the live registry, the
  LandingScreen state, the Playground state, and a ready predicate
  (`store.state.nodes.size > 0`).
- `packages/ui/src/debug/Playground.tsx` — installs the
  playground-scoped scenario + snapshot hooks while the overlay is
  mounted. These are NOT always available — call
  `__canaryPlaygroundOpen()` first.
- `packages/ui/src/App.tsx` — calls `installCanaryHooks` on mount.
- `packages/ui/src/styles.css` — `.qualia-ui-hidden` selector that
  `__canaryHideUI(true)` triggers for canvas-only screenshots.

## Configuration

`workload.json` → `qualiaConfig` block:

| Field | Default | Purpose |
|---|---|---|
| `projectDir` | `C:\Repos\Qualia` | Qualia repo root (where the agent runs `npm run dev`). |
| `vitePort` | 5173 | Vite dev server port (default matches Qualia's `vite.config.ts`). |
| `cdpPort` | 9223 | Chrome remote-debugging port. Differs from Penumbra (9222) so both workloads coexist. |
| `defaultCanvasWidth` | 1280 | Initial document width. LandingScreen needs more vertical space than Penumbra's 960×540. |
| `defaultCanvasHeight` | 720 | Initial document height. |
| `defaultStabilizeMs` | 500 | Settle delay after each navigation/reload. |
| `readyTimeoutSec` | 30 | Max wait for `__canaryWaitForReady` to resolve. |
| `clearLocalStorageOnInit` | true | Ensures first-launch behavior (LandingScreen visible, default profile, no stale provider config) is reproducible. |
| `chromeFlags` | `['--force-device-scale-factor=1', '--hide-scrollbars', '--disable-smooth-scrolling']` | Determinism flags. Add more carefully — flags that affect rendering invalidate baselines. |
| `chromePath` | (auto-detect) | Override path to chrome.exe if standard Windows install isn't found. |

## Hook surface (full inventory)

Operator-facing reference: `workloads/qualia/AGENT_NOTES.md`. Spec-level
groupings:

### Readiness (always available after `__canaryHooksReady === true`)

- `__canaryWaitForReady(timeoutMs)` — resolves once demo data has loaded.
- `__canaryGetAppInfo()` — `{ ready, theme, moduleCount, profile, landingOpen, playgroundOpen }`.
- `__canaryHideUI(hidden)` — hides toolbar / sidebar / panels for
  canvas-only screenshots.
- `__canarySetTheme('dark' | 'light')`.
- `__canaryHooksReady` — boolean marker set true after install.

### Persona registry (live, not draft)

- `__canaryGetPersonaConfig()` / `__canaryListPersonas()` — registry
  inspection.
- `__canarySetPersonaEnabled(id, enabled)` — per-persona mutation.
- `__canaryApplyProfile(name)` — bulk-apply
  (`minimal | standard | cinematic | workshop`).
- **Legacy aliases:** `__canaryGetModuleConfig` / `__canaryListModules` /
  `__canarySetModuleEnabled` (renamed in Qualia Phase 7.2, 2026-05-12;
  preserved as `@deprecated` for one transition release).

### Landing screen modal

- `__canaryShowLandingScreen()` / `__canaryCloseLandingScreen()` /
  `__canaryGetLandingState()` — DOM-driven inspection.
- `__canaryClickProfilePill(name)` /
  `__canaryToggleLandingPersona(id)` / `__canaryClickLandingApply()` /
  `__canaryClickLandingCancel()`.
- **Legacy alias:** `__canaryToggleLandingModule` (same rename window).

### Playground (Wave 0.B, gated)

Only available after `__canaryPlaygroundOpen()` succeeds. The opener
returns `{ ok: false, reason: 'module-disabled' }` when
`debug.playground` isn't enabled — tests must
`ApplyProfile('workshop')` (or `SetPersonaEnabled('debug.playground',
true)`) first.

- `__canaryPlaygroundOpen()` / `__canaryPlaygroundClose()` /
  `__canaryPlaygroundIsOpen()`.
- `__canaryPlaygroundGetState()` / `__canaryPlaygroundListScenarios()` /
  `__canaryPlaygroundLoadScenario(id, paramsOverride?)` /
  `__canaryPlaygroundSetParam(key, value)`.
- `__canaryPlaygroundListSnapshots()` /
  `__canaryPlaygroundSaveSnapshot(label)` /
  `__canaryPlaygroundRestoreSnapshot(id)` /
  `__canaryPlaygroundDeleteSnapshot(id)`.

### qualia-v4 (pointers / qverse / RAG UI)

Added 2026-05-12. Covers the pointer + qverse + RAG UI surface for
VLM testing (`suites/qualia-v4-ui.json`):

- Fixturing: `__canaryAddNode`, `__canarySelectNode`, `__canaryAddPointer`,
  `__canarySetPointerStatus`, `__canarySpawnChildContext`.
- Driving: `__canaryRefreshPointers`, `__canarySwitchContext`,
  `__canaryClickRefresh`, `__canaryOpenAddPointerForm`,
  `__canaryClickPropTab`.
- Reading: `__canaryGetPointerSection`, `__canaryGetDeadNodePrompt`,
  `__canaryGetCrossContextBadge`, `__canaryGetContextNavigator`,
  `__canaryGetBreadcrumb`, `__canaryGetRagIndicator`.
- **Legacy aliases:** `__canaryGetCrossQverseBadge`,
  `__canaryGetQverseNavigator` (renamed during the qverse/context
  split, Phase 7.2, 2026-05-12).

### RH-2 multi-display sweep (2026-05-14)

For the perf/viewer/theme sweep suite (`suites/multi-display.json`):

- `__canaryApplyPerfSnapshot({ theme?, perfSettings?, viewerSettings? })`
  — applies a settings bundle in one call. Schema mirrors what
  `Qualia/packages/ui/src/snapshot.ts` emits, so an existing snapshot
  can be replayed.
- `__canaryWaitForRenderSettled(timeoutMs = 5000)` — resolves once two
  consecutive frames have the same LOD digest. Use after
  `ApplyPerfSnapshot` before screenshotting.
- `__canaryLoadMinimalSample()` — loads `examples/minimal/.qualia` via
  the dev FS plugin or the static mount. Lighter than driving the
  Load-sample button.

### Diagnostic dump (Pencil profile debugging, 2026-05-19)

For test fixtures that need to assert pipeline state alongside the
screenshot:

- `__canaryDumpDiagnostics({ hideOverlay? })` — captures persona enable
  map + edge type colors + renderer tone mapping / fog / background +
  bucket count, renders a top-right monospace overlay into the next
  screenshot, dumps to console for offline capture, returns the full
  state object.
- `__canaryClearDiagnosticsOverlay()` — removes the overlay if present.

### Camera control

- `__canaryResetAndRepel(settleMs = 1000)` — resets camera + runs a
  Repel spread. Equivalent to clicking the toolbar's Reset + Repel
  buttons in sequence. Use after `__canarySwitchContext` to ensure
  positions settle before screenshotting.

## Agent action mapping

`Canary.Agent.Qualia.ExecuteAsync` action → hook routing:

| Action | What it does |
|---|---|
| `RunCommand` | Evaluate an arbitrary JS expression. Catch-all for anything not covered by a named action. |
| `WaitForReady` | Poll `__canaryWaitForReady` until app reports ready or timeout. |
| `WaitForStable` | `Task.Delay(ms)`. |
| `Reload` | Re-navigate to the current Vite URL + re-wait for `__canaryHooksReady`. Preserves localStorage (intentionally — the calling test's `setup.commands` seed survives the React re-mount). Mirrors steps 5-7 of `InitializeAsync` minus the storage clear. Use in `actions[]` (NOT `setup.commands`) to retrigger GRAPH_LOAD mid-test. Added 2026-05-25 to unblock the eager-L3 cold-launch / warm-launch / provider-swap fixtures; the pure-JS `window.location.reload()` alternative kills the CDP execution context ("Inspected target navigated or closed"). |
| `SetCanvasSize` | Set `documentElement` size — used to control screenshot dimensions. |
| `HideUI` | Toggle the chrome-hide CSS class via `__canaryHideUI`. |
| `ApplyProfile` | `__canaryApplyProfile(name)`. |
| `SetModuleEnabled` | `__canarySetPersonaEnabled(id, enabled)` (action name preserved for backwards compat per Phase 7.2 rename). |
| `ShowLandingScreen` / `CloseLandingScreen` | Open / close the modal. |
| `ClickProfilePill` | Click a pill by name. |
| `ToggleLandingModule` | Toggle a persona checkbox by id inside the modal (action name preserved). |
| `ClickLandingApply` / `ClickLandingCancel` | Footer buttons. |
| `ClearStorage` | `localStorage.clear() + sessionStorage.clear()`. |
| `PlaygroundOpen` / `PlaygroundClose` | Toggle the Wave 0.B overlay (gated by `debug.playground`). |
| `PlaygroundLoadScenario` / `PlaygroundSetParam` | Scenario control. |
| `PlaygroundSaveSnapshot` / `PlaygroundRestoreSnapshot` / `PlaygroundDeleteSnapshot` / `PlaygroundListSnapshots` / `PlaygroundGetState` | Snapshot lifecycle + inspection. |

New hooks landing on the Qualia side can usually skip the C# action
mapping — `setup.commands` runs raw JS via `Runtime.evaluate`, so a
test JSON can call any `__canary*` directly. The action mapping is
syntactic sugar for hooks frequently used + worth surfacing as
named actions.

## Mode selection

Standard Canary mode flags work as expected:

```bash
cd C:\Repos\Canary
canary run --workload qualia --suite landing-screen              # pixel-diff (default)
canary run --workload qualia --suite landing-screen --mode vlm   # Gemma 4 via Ollama
canary run --workload qualia --suite landing-screen --mode both  # both verdicts
```

VLM mode requires a local Ollama instance with `gemma4:e4b` pulled
(`ollama pull gemma4:e4b`). Per-checkpoint `mode: "vlm"` always wins
over the flag.

VLM descriptions live in `setup.vlmDescription` (test-wide context)
or per-checkpoint `description` (the prompt sent to Gemma) — that's
the editable expectation surface. Tweak the description as the UI
evolves; no code change required.

## Logging

Per the MultiVerse logging convention (Slop log-tap pattern), Qualia
workload tests should surface:

- The configured persona profile + which personas are live-enabled
  (capture via `__canaryGetPersonaConfig()`).
- The current theme + perf settings at checkpoint time (via
  `__canaryGetAppInfo()` / `__canaryDumpDiagnostics()`).
- Any `[BehaviorExtractor]` / `[qualia/rag]` / `[RagOrchestrator]` /
  `[compute.simulation]` info-level console messages (captured by
  Qualia's dev-test harness `consoleCapture` ring buffer; surfaced
  by the `env.console-recent` dev test). When ingesting dev-test
  reports as a Canary checkpoint type (queued — see audit doc
  Phase M2), these messages become part of the run record.

No Slop graphs in this workload, so no `Log Tap` components. The
dev-test harness + console ring buffer are the substitute.

## Caveats

- **Cold-launch cost.** Each suite spawns a fresh Vite + Chrome
  (~5-10s startup overhead). `runMode: shared` (single-launch for
  an entire suite — CPig's pattern) isn't yet supported by the
  Qualia agent. Queued.
- **`__canaryHideUI(true)` doesn't kill the LandingScreen.** The
  modal sits on top with its own z-index (200). Use
  `__canaryCloseLandingScreen` first if you want a chrome-free
  viewport screenshot.
- **Penumbra-specific actions** (`LoadScene`, `SetCamera`,
  `LoadDisplayPreset`) are NOT wired in the Qualia agent. The
  qualia-v4 RH-2 sweep covers most camera + perf use cases via
  `__canaryApplyPerfSnapshot` + `__canaryResetAndRepel`.

## Cross-repo dependencies

| Repo | Surface this workload depends on |
|---|---|
| Qualia | `packages/ui/src/canary-hooks.ts` (the entire `__canary*` surface), `packages/ui/src/App.tsx` (install site), `packages/ui/src/debug/Playground.tsx` (scoped hooks), `packages/ui/src/styles.css` (`.qualia-ui-hidden`), `examples/sample/` + `examples/minimal/` (test vaults). |
| Penumbra | Indirectly — Qualia consumes `@penumbra/three` for SDF backdrop rendering, so Penumbra version bumps that change visible output may require Qualia baseline updates (per §16 cross-repo baseline ownership). |
| Slop | Not used. |
| CPig | Not used. |

Per the Cross-Repo Change Protocol (`MultiVerse/CLAUDE.md` § 7), any
hook addition, rename, or removal lands as a coordinated commit pair
(Qualia hook change + Canary agent / `AGENT_NOTES.md` / this spec
update) plus a one-line entry in `MultiVerse/BUILD_LOG.md`.

## Suite roster (2026-05-30 snapshot)

| Suite | Tests | Coverage |
|---|---|---|
| `landing-screen.json` | LandingScreen modal first-launch states (profile pills, persona checkboxes, apply/cancel, overlap bug regression) |
| `display-modes.json` | Rendering modes (mesh, wireframe, shaded, rendered, cinematic, blueprint, neon, artistic, pencil, aurora) |
| `multi-display.json` | RH-2 sweep — 11 tests covering theme/junction/edge-width/bloom/particulate variants on `examples/minimal/.qualia` (9 nodes / 4 contexts / 1 ghost / 1 cross-context edge) |
| `pencil-diff.json` | Pencil profile debugging (mount trace, no-X variants, only-X variants, standard+debug overlay) |
| `playground.json` | Wave 0.B Playground — one test per scenario (random / grid / tree / scale-free / stress-1k) + snapshot round-trip |
| `qualia-v4-ui.json` | Pointer / qverse / RAG UI fixtures (pointers empty/populated/add-form-open, cross-qverse badge, dead-node prompt, breadcrumb-nested, ghost-node, qverse-navigator, refresh-toolbar/enabled, perfpanel-rag-section) |
| `eager-l3.json` | RAG eager-L3 extraction (Phase M1 + Moves 2-4 — 2026-05-25 → 2026-05-27). Six fixtures: `reload-smoke` (validates the `Reload` action preserves localStorage + re-establishes hooks), `no-provider-noop` (silent-no-op per ADR 0031), `progress-badge` (Move 3 — asserts the EagerExtractionProgressBadge becomes visible during a live sweep; ~10s), `cold-launch` (sweep enqueues N > 0 with Ollama provider seeded; ~95s incl. one extraction), `warm-launch` (idempotency check; ~30s), `provider-swap` (Move 4 — Ollama extraction lands then swaps to OpenAI-compat-at-Ollama and re-extracts under the new provider id; asserts cache gains entries under both providers via the new `provider[ollama=N,openai-compat=M]` breakdown in the sidecar.behavior-cache dev test; ~150s). Cold/warm pair has an intentional dependency. Sweep-dependent fixtures explicitly enable the `compute.rag.eager-l3` persona via `__canarySetPersonaEnabled` before the Reload — required after Move 3 added the persona gate. |
| `settings-resolver.json` | ADR 0037 Phase V verification (2026-05-30). 13 tests across five groups: **A precedence** (4 — profile beats factory, user beats profile, untouch reverts, user beats persona), **B switch determinism** (3 — A→B→A byte-identity, out-of-band axis reset, edgeRouting struct reset), **C declarative persona intent** (3 — laser-rat enable/disable, junction-preset revert, multi-intent last-wins), **D push channel** (1 — `onSettingsChange` fires per write), **E persistence** (2 — `qualia.settings` round-trip + legacy migration). Structural assertions only (no pixel-diff). Each test isolates one architectural claim so a Fail points at a specific invariant. Requires Qualia >= commit `caa3ae0` for the four new `__canary*` hooks (`setPerfSettings`, `untouchField`, `reapplyProfile`, `getSettingsChangeCount`). |

The above suites cover most fixtures; a `diag-*` diagnostic family
(~25 tests) is used for ad-hoc debugging arcs and doesn't yet belong
to a parent suite — queued for cleanup.

## Open questions

- **`runMode: shared` for the Qualia agent.** CPig's shared-Rhino
  launch pattern saves ~30s/suite. Qualia would benefit but the
  CDP/Vite lifecycle needs careful handling (the dev server fights
  HMR + storage hydration across tests). Worth a separate ADR.
- **Diag-* test family suiting.** ~25 `diag-*` tests have no parent
  suite. Either bundle into a `diag.json` suite or promote
  individual tests into the suites that own their feature area.
- **Eager L3 extraction suite — Moves 2-4 closed (2026-05-25 → 2026-05-27).** The
  `Reload` action was added to `QualiaBridgeAgent.cs` per Move 2's
  path (a); the three deferred fixtures (`reload-smoke`,
  `cold-launch`, `warm-launch`) shipped. Move 3 added the
  `progress-badge` fixture asserting the EagerExtractionProgressBadge
  surfaces during a live sweep via the `__canaryGetProgressBadgeState`
  hook, and updated the existing sweep-dependent fixtures to enable
  the new `compute.rag.eager-l3` persona before the Reload. Move 4
  closed Phase 2 v2 with the `provider-swap` fixture — exercises
  the new provider-id-aware cache key by swapping Ollama →
  OpenAI-compat-at-Ollama (same underlying model, different
  provider id) and asserting the cache file's provider breakdown
  shows both providers' entries via the extended `sidecar.behavior-cache`
  dev test's `provider[ollama=N,openai-compat=M]` summary
  fragment.
- **Dev-test harness as Canary checkpoint type.** Per the same
  audit doc Phase M2: add `"source": "dev-test"` checkpoint type
  that ingests `__qualiaRunDevTests()` markdown alongside the
  screenshot. Once landed, the M1 eager-l3 fixture (which
  hand-rolls the dev-tests call inside `setup.commands`) collapses
  to a one-liner checkpoint definition; future cold/warm/swap
  fixtures (post-Reload) inherit the same simplification.

## Related

- `Canary/spec/PEERS.md` — cross-repo contract (Qualia section).
- `Canary/spec/PENUMBRA_WORKLOAD.md` — sibling spec; the CDP pattern
  this workload extends.
- `Canary/workloads/qualia/AGENT_NOTES.md` — operator-facing
  quick reference.
- `Qualia/spec/PEERS.md` — Qualia-side mirror of the contract.
- `MultiVerse/STANDARD.md` § 16 — visual artifacts + baseline
  conventions (Qualia baselines under `workloads/qualia/baselines/`
  per the §16 storage rule).
- `MultiVerse/audit/2026-05-24-testing-canary-audit-and-plan.md` —
  the audit + plan that drove this spec's promotion from
  AGENT_NOTES.md.
