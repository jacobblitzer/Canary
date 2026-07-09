---
date: 2026-05-05
status: shipped
source: extracted from Canary/AGENTS.md per STANDARD.md §3 + §19
---

# Penumbra D3 tricubic + Sturm root isolation (Wave 3 Phase 6, shipped 2026-05-05)


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
