---
date: 2026-05-05
status: shipped
source: extracted from Canary/CLAUDE.md per STANDARD.md §3 + §19
---

# Penumbra Tier D1 directional Lipschitz (Wave 3 Phase 3, shipped 2026-05-05)


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
