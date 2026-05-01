---
title: "Brick overlay invisible"
date: 2026-04-24
tags:
  - bug
  - penumbra
  - rendering
status: resolved
project: penumbra
component: atlas/brick-overlay
severity: high
fix-commit: ""
---

# Brick overlay invisible

## Summary
Brick wireframe overlay (Alt+B) rendered as invisible despite correct atlas data. Wireframes would flash, appear off-screen, or render behind the camera.

## Environment
- OS: Windows 11
- Browser: Chrome/Edge via CDP
- Penumbra: main branch, WebGPU backend

## Root Cause
`getInvProjection()` in `test/main.ts` returned a **row-major** matrix instead of **column-major**. Indices 11 and 14 in the Float32Array were swapped, causing the inverse projection to place overlay geometry behind the camera.

## Steps to Reproduce
1. Load atlas-blob scene (12-sphere smooth union)
2. Enable brick overlay visualization (Alt+B or `__canaryToggleOverlay`)
3. Observe: overlay is invisible or flashes briefly

## Fix
Swapped indices 11 and 14 in the Float32Array returned by `getInvProjection()` in `test/main.ts`.

## Verification
- Canary visual regression test `atlas-blob-bricks-overlay` captures correct overlay
- Screenshot comparison before/after confirms fix

## Related
- Spec: `specs/v4/03b-brick-overlay-fix.md` (Penumbra repo)
- Test: `workloads/penumbra/tests/atlas-blob-bricks-overlay.json`
