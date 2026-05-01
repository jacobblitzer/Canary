---
title: "coarseRes initialization ordering"
date: 2026-04-24
tags:
  - bug
  - penumbra
  - atlas
status: resolved
project: penumbra
component: atlas/cascade-manager
severity: high
fix-commit: ""
---

# coarseRes initialization ordering

## Summary
CascadeManager was created with default `coarseRes=16` instead of the computed value (32), causing incorrect cascade classification and atlas cell sizing.

## Environment
- OS: Windows 11
- Penumbra: main branch

## Root Cause
`initAtlas()` was called **before** `computeCascadeParams()` in the initialization sequence. The CascadeManager constructor received the default `coarseRes=16` instead of the computed value of 32.

## Steps to Reproduce
1. Initialize Penumbra renderer with atlas-enabled scene
2. Observe cascade parameters — coarseRes is 16 instead of expected 32
3. Atlas cells are incorrectly sized

## Fix
1. Moved `computeCascadeParams()` call **before** `initAtlas()` in the initialization sequence
2. Added CascadeManager invalidation/rebuild when `coarseRes` changes, as a safety net

## Verification
- coarseRes correctly reads 32 after initialization
- Atlas cell sizing matches expected cascade parameters

## Related
- Files changed: Penumbra renderer initialization code
