---
date: 2026-05-05
status: in-progress
source: extracted from Canary/AGENTS.md per STANDARD.md §3 + §19
---

# Penumbra per-atom brick allocator (Wave 1 / E1+E2, in-progress 2026-05-05)


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
