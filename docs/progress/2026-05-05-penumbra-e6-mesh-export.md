---
date: 2026-05-05
status: shipped
source: extracted from Canary/AGENTS.md per STANDARD.md §3 + §19
---

# Penumbra E6 mesh-export determinism (Wave 3 Phase 5d, shipped 2026-05-05)


Penumbra's E6 mesh-extraction pipeline (cascade → samplers →
marching cubes → welding → normals → OBJ/STL) is pure CPU. Phase
5d locks it against silent regressions via a programmatic
(numeric) determinism check — separate from the GPU pixel-diff
path covered by `d1-lipschitz` + `multiscale-overlays`.

New CDP hook: `__canaryExtractMeshDeterminism()` returns:
- `vertexCount`, `triangleCount`, `brickCount`
- `bbox: { min, max }` (rounded to 4 decimals)
- `vertexHash`, `indexHash`, `normalHash` — FNV-1a 32-bit hashes
  of the raw Float32Array / Uint32Array bytes; cross-hardware
  safe because the pipeline is CPU-only with the same tape
  evaluator everywhere
- `sampleVertices: [9 floats]`, `sampleNormals: [9 floats] | null`
  — first 3 vertices/normals (rounded), for human inspection
- `elapsedMs`, `cellsPerBrickEdge: 8`, `weldEpsilon` — runtime
  parameters used (matches Studio export defaults)

Three new fixtures + suite under `workloads/penumbra/`:
- `multi-field-mesh-determinism.json` — Multi-Field scene
- `atlas-blob-mesh-determinism.json` — 12-Sphere Blob (smooth
  union; densest MC vertex emission)
- `assembly-mesh-determinism.json` — Mechanical Assembly (CSG
  difference; multi-atom dispatch path)

Suite: `mesh-export-determinism.json` runs all three. Each test's
`commands` array invokes the hook and captures the JSON-stringified
summary — that summary IS the golden. Hash divergence on any
field → test failure. The companion pixel-diff suites cover GPU
output; this suite catches CPU-side regressions like swapped MC
tri-table entries, off-by-one in welding's grid key, or flipped
normal directions.

Run:
```
canary run --workload penumbra --suite mesh-export-determinism
```
