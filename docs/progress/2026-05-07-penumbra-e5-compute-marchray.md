---
date: 2026-05-07
status: shipped
source: extracted from Canary/CLAUDE.md per STANDARD.md §3 + §19
---

# Penumbra E5 Compute marchRay (Wave 3 Phase 7, merged 2026-05-07)


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
