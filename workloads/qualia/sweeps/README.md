# Display-behavior sweeps (W1 harness)

Part of the display-sweep campaign —
`Qualia/docs/plans/2026-07-19-display-behavior-sweep.md` · verified ground
rules in `MultiVerse/prompts/qualia-display-sweep-2026-07-19.md` · session
log `Qualia/docs/progress/display-sweep.md`.

Empirically maps Qualia's display pipeline: one-lever mutations from
pinned base states, structural fingerprints (no pixels), derived effect
tables + inconsistency/no-op/state-leak reports.

## Run one sweep

```
# 1. generate micro-tests + suite from a spec
node workloads/qualia/sweeps/generate-sweep.mjs workloads/qualia/sweeps/specs/w1-smoke.json --run-id r1

# 2. run it (ONE Vite+Chrome boot serves every family test)
#    NOTE: port 5173 must be free — stop any dev server first.
.\canary.cmd run --workload qualia --suite sweep-w1-smoke --headless

# 3. derive the reports
node workloads/qualia/sweeps/derive.mjs w1-smoke-r1
```

Observations: `Qualia/debug-logs/<sweepId>/` (one JSON per state via the
dev server's `POST /api/debug/write`). Reports:
`workloads/qualia/sweeps/runs/<sweepId>/{effects.json,effect-table.md,findings.md}`.

## Files

- `sweep-driver.js` — in-page driver (embedded into generated tests by the
  generator; reset → mutate → settle → double-read fingerprint → revert →
  leak-check per state). Keep dependency-free plain JS.
- `generate-sweep.mjs` — spec → `tests/sweep-*.json` + `suites/sweep-*.json`
  + run manifest under `runs/`.
- `derive.mjs` — observations → reports.
- `specs/` — sweep specs (see w1-smoke.json for every mutation kind).

## Ground rules baked into the harness (do not "simplify" away)

- Persona mutations use `preserveProfile: true` — the default path flips
  the profile to `custom` and drops the whole base-profile settings layer.
- Reset = `__canaryClearTouchedPerfFields` + `__canaryApplyProfile(base)`
  + theme/selection/camera restore; re-applying the same profile alone
  does NOT clear touched fields.
- Perf mutations pass `{ markTouched: false }`.
- Chunked RunCommands stay under the agent's 60s CDP evaluate ceiling
  (`chunkSize` states per action).
- Camera pins AFTER first settle (`duration: 0`) — the one-shot auto-fit
  fires ~100ms after first layout.
- Fixtures must be position-deterministic: DDV, workshop-palette,
  qnode-junction-alignment-test, implicit-geometry. NOT minimal.qualia
  (force-d3 + Math.random init).
- Structural only; screenshot checkpoints are meaningful solely on
  Minimal-class frozen states (uTime shader animations never settle).
- Sweeps run on FRESH loads only (the Canary boot guarantees this) —
  after HMR module swaps, hook closures can reference a detached renderer.
- **Perspective-locked contexts cannot be camera-pinned** (the lock
  re-asserts the pose every frame) — `enterContext` families inside a
  locked/facet context show identical camera/frame deltas on EVERY
  state (w2-armed-r1 confound). For such families trust only the
  camera-independent channels (sceneGraph, dom, perf, persona, fades).
- `dom.styleCount` is deliberately not fingerprinted — injected
  stylesheets never retract, making the count monotonic noise.
- **Bucket-materialization ratchet**: a mutation that forces a new
  atom-shape bucket (render.nodes-class mounts under some bases)
  permanently adds one empty Mesh — NodeAtomLayer's bucket cache never
  evicts. sceneGraph leak rows showing +1 Mesh/+1 total from such a
  state onward are CACHE GROWTH, not state leaks (deterministic per
  run, so drift-diff between same-fingerprint runs is unaffected —
  w2-atlas-r5).
