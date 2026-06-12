---
date: 2026-06-11
tags: [bug, canary, penumbra]
status: resolved
project: canary
severity: medium
component: "penumbra-workload tests, compute-marcher"
---

# All 13 `atlas-blob-compute-*` tests time out — event-driven render starves the compute smoke

## Symptom

Every `atlas-blob-compute-*` test crashed in both 2026-06-11 sweeps with
`__canaryRunComputeMarcherSmoke: timed out after 15015ms/60009ms waiting
for compute dispatch`. Diagnostic state at expiry: `dispatchCount=0`,
`callCount=0`, `renderFrameCount=1` after 3601 rAF ticks — i.e. the
renderer rendered ~one frame across the whole 60s wait.

## Wrong first theory (the original task premise)

"The 15s/60s helper budget is shorter than the ~90s compute pipeline
build." Disproven by reading the tests: every JSON already calls
`__canaryPrebuildComputeMarchPipeline()` (the dedicated build-await hook,
240s CDP ceiling) *before* the smoke, and the 60s run made literally zero
dispatch progress — the smoke was starved, not slow.

## Actual root cause — a three-link chain

1. **Penumbra 2026-05-08 graduation** flipped `eventDrivenRender` (C2) ON
   in the shipped `default` profile.
2. **Canary always boots with that profile**: fresh temp Chrome profile →
   no localStorage → Studio's `bindFeatureLoaderUI()` initializes the
   launch checkboxes from the `default` profile, and `?autostart=true`
   (Canary's URL) without `?features=` reads those checkboxes
   (`resolveLaunchFeatures()`, `packages/studio/main.ts`).
3. **The compute smoke is incompatible with an event-driven loop**: with
   C2 on, Studio's `frame()` skips `renderer.render()` unless something
   marked dirty. `dispatchMarchCompute` only runs inside `render()`. The
   smoke produces exactly ONE dirty signal (the `computeMarchEnabled`
   toggle) but needs ~10 rendered frames to reach its dispatch target —
   so it idles forever; no timeout value can fix it.

The tests were authored 2026-05-07 (E5 Phase 7b/3), one day before the
graduation, and passed then; nothing ran them again until the 2026-06-11
sweeps (zero baselines, zero successful run dirs).

## Fix (Canary-side, 2026-06-11)

Inserted into all 13 `atlas-blob-compute-*` test JSONs, right after the
atlas-ready wait:

```json
"window.__canarySetDisplayState({features: {eventDrivenRender: 'off'}})",
```

C2 off → `shouldRender()` returns true unconditionally → continuous rAF
render → dispatches accumulate. Per-test `setup.backend` reloads the page
each test, so the toggle never leaks across tests. Helper budgets left
unchanged (the probe passed with the original 15s budget once unstarved).

Upstream improvement filed as ask
[`docs/asks/penumbra/0002-compute-smoke-self-mark-dirty.md`](../asks/penumbra/0002-compute-smoke-self-mark-dirty.md):
the smoke helper should `markDirty` per rAF tick so it works under the
shipping default profile; once landed, the 13 `eventDrivenRender: 'off'`
lines should be removed to restore C2-on coverage.

## Validation

Probe (`atlas-blob-compute-smoke-3b3a`): Crashed → **New (125s)** with
only the C2-off line added. Full 13-test headless re-run
(`.compute-validation.log`, 2026-06-11 20:38–21:04): **10 of 13 pass**
(New, 96–173s each) — first-ever passes for the family.

## Residual failures (different bugs, Penumbra-side)

The starvation fix exposes three deeper issues, now with actionable
errors instead of silent timeouts:

- `atlas-blob-compute-smoke-bisection`, `atlas-blob-compute-smoke-d2cubic`
  — `mapAsync: A valid external Instance reference no longer exists`
  during `captureComputeMarchOutput` readback: Dawn device/instance loss
  under the two heaviest per-frame variants (D2Cubic, tileCount 8/4) on
  Intel iGPU — TDR-class. Needs Penumbra-side mitigation (reduced smoke
  resolution / single-dispatch capture mode).
- `atlas-blob-compute-smoke-persistent` — dispatch-wait timeout with
  `renderFrameCount` climbing (571+) but `callCount=0`: frames render
  continuously, yet the `enablePersistentThreading` variant never reaches
  `dispatchMarchCompute`. The E5 persistent-threading path appears not to
  drive the per-frame dispatch counter at all.

Both folded into ask
[`docs/asks/penumbra/0002-compute-smoke-self-mark-dirty.md`](../asks/penumbra/0002-compute-smoke-self-mark-dirty.md).
