---
title: "Atlas cell boundary rectangular artifact"
date: 2026-04-24
tags:
  - bug
  - penumbra
  - atlas
status: resolved
project: penumbra
component: atlas
severity: medium
fix-commit: "590184b"
---

# Atlas cell boundary rectangular artifact

## Summary
A rectangular lighting artifact appears at coarse cell boundaries in the atlas rendering path. The artifact was always present but hidden by the HUD overlay; it became visible after the HUD toggle feature (Q key) was added.

## Environment
- Penumbra WebGPU renderer, atlas eval mode
- 64-Sphere Stress test scene (scene 9)
- Most visible from top-down (elevation 80) and three-quarter views

## Symptoms
- Axis-aligned rectangular artifact on the rendered surface
- Only visible in atlas mode (disappears when switching to tape-eval with T key)
- Fixed world-space position between sphere groups
- Independent of interpolation method (trilinear or tricubic)
- Independent of apron data (persisted even with apron texels set to 999.0)

## Root Cause
When `estimateNormal()` evaluates `sceneSDF` at 6 offset points (p ± epsilon), some of those points can land in an adjacent coarse cell. If the adjacent cell has no brick allocated in the atlas, the evaluation falls through to tape mode, producing a slightly different SDF value than the atlas path. This gradient mismatch creates an incorrect surface normal at cell faces, visible as a rectangular lighting discontinuity.

The fundamental issue: mixed evaluation methods (atlas vs tape) within a single normal estimation produce inconsistent gradients.

## Investigation
Systematic bisection testing over multiple sessions:
1. Reverted tricubic → trilinear: artifact persisted
2. Set apron texels to 999.0: artifact persisted (not apron bleed)
3. Reverted all 5 shader/runtime files to original: artifact STILL persisted
4. Confirmed artifact was pre-existing, hidden by HUD overlay
5. Tape-eval mode (press T): artifact disappeared — confirmed atlas-specific

## Fix
Added 6-face-neighbor expansion in `cascade-manager-webgpu.ts` after narrow-band classification. After classifying surface cells, all 6 face-neighbors of each surface cell are added to the allocation list (with lower priority than surface cells). This ensures:
- Every cell adjacent to a surface cell also has a brick
- The ±epsilon samples in normal estimation always find a brick (never fall through to tape)
- The SDF gradient is evaluated consistently via the atlas path across cell boundaries

### Files Changed
- `packages/runtime/src/cascade-manager-webgpu.ts` — face-neighbor expansion after classification

### Related Changes (same commit)
- `packages/shaders/src/wgsl/populate.wgsl` — 10×10×10 apron bricks
- `packages/runtime/src/atlas-webgpu.ts` — texture size for apron
- `packages/shaders/src/wgsl/main-atlas.wgsl` — brickUV mapping + adaptive epsilon
- `packages/runtime/src/atlas-diagnostic.ts` — updated texel offsets

## Verification
Ran Canary stress-test-orbit workload with HUD hidden (Q key). All 4 camera angles (front, three-quarter, top-down, wide-angle) are artifact-free.
