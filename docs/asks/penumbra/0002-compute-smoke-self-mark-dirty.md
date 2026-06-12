---
id: 0002
peer: penumbra
status: open
requested: 2026-06-11
severity: medium
tags: [canary-hooks, compute-marcher, eventDrivenRender]
---

# Compute-marcher smoke hardening: self-supply render demand (C2-proof) + two residual failures

## Context

Since the 2026-05-08 graduation flipped `eventDrivenRender` ON in the
`default` profile, every Canary `?autostart=true` boot runs with the
event-driven render gate active (fresh Chrome profile → no localStorage →
`bindFeatureLoaderUI()` applies the `default` profile to the launch
checkboxes → `resolveLaunchFeatures()` reads them). The compute-marcher
smoke (`runComputeMarcherSmokeImpl`, `packages/studio/canary-hooks.ts`
~line 973) polls `getMarchComputeDispatchCount()` via rAF and needs ~10
rendered frames, but only generates ONE dirty signal (the
`setDisplayState({render:{computeMarchEnabled:true}})` flip). The render
loop goes quiescent and the smoke times out with `dispatchCount=0` at any
budget — all 13 `atlas-blob-compute-*` Canary tests crashed this way in
the 2026-06-11 sweeps (Canary `docs/bugs/0013`).

Canary-side workaround (shipped 2026-06-11): each compute test JSON now
sets `__canarySetDisplayState({features: {eventDrivenRender: 'off'}})`
before the smoke. This works but means the compute path is never
exercised under the shipping default profile.

## Shape of the answer

In `runComputeMarcherSmokeImpl`'s rAF `tick`, mark the renderer dirty
each frame while the smoke is in flight, e.g.:

```ts
deps.markRenderDirty?.('compute-smoke'); // → featureRegistry.eventDrivenRender.markDirty
```

(plus the matching `CanaryHookDeps` member wired in `main.ts`, same
pattern as the existing snapshot-prep `markDirty('snapshot')` calls at
~line 4621). Same for `__canaryComputeMarcherDeterminismCheck` and the
calibration helper if they also depend on per-frame dispatches.

## What Canary will do once the peer lands it

Remove the `eventDrivenRender: 'off'` setup command from the 13
`atlas-blob-compute-*` test JSONs so the smoke runs under the shipping
default profile (C2 on), restoring coverage of the graduated
configuration.

## Two residual failures the C2-off workaround exposed (2026-06-11)

With render demand restored, 10 of the 13 `atlas-blob-compute-*` tests
pass for the first time. The remaining three fail Penumbra-side:

1. **Device/instance loss under the heaviest variants** —
   `atlas-blob-compute-smoke-bisection` (tileCount 8, D2Cubic+Bisection)
   and `atlas-blob-compute-smoke-d2cubic` (tileCount 4, D2Cubic) die in
   `captureComputeMarchOutput` with `mapAsync: A valid external Instance
   reference no longer exists` — TDR-class loss from per-frame heavy
   compute at 960×540 on Intel iGPU. Suggested shape: run the smoke's
   dispatches at reduced `outputResolutionScale`, or add a
   single-dispatch capture mode that pauses the per-frame dispatch while
   reading back.
2. **Persistent-threading variant never dispatches** —
   `atlas-blob-compute-smoke-persistent` (`enablePersistentThreading:
   true, persistentWorkgroupCount: 32`) times out with `callCount=0`
   while `renderFrameCount` climbs past 571 — frames render continuously
   but `dispatchMarchCompute` is never invoked (or the counter isn't
   plumbed for the persistent path). Either wire the counter or have the
   smoke use a persistent-path-aware progress signal.

## Open questions

None — the markDirty plumbing already exists for snapshots and camera
changes; this extends it to the smoke loop.
