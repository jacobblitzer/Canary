---
date: 2026-04-26
tags: [bug, test-design, penumbra]
status: open
project: canary
severity: low
component: "workloads/penumbra/tests"
---

# 0006 — STL Import Test Checkpoints Indistinguishable

## Summary
The `stl-import-benchy` test has 4 camera checkpoints (front, side, three-quarter, top-down) but because the current STL import renders the Benchy as a featureless bounding sphere, all 4 angles produce nearly identical images. The test provides no additional coverage from multiple viewpoints.

## Observations
- Front, side, and three-quarter screenshots are visually indistinguishable (sphere looks the same from every angle)
- Only top-down is slightly different due to the steeper 80-degree elevation
- The 4 checkpoints add ~12 seconds of test time for zero additional coverage

## Root Cause
The STL import uses `primitive-fit` which produces a sphere. A sphere is rotationally symmetric — multiple camera angles are redundant.

## Proposed Fix
This bug is downstream of Penumbra bug 0021 (STL renders as featureless sphere). Once the STL import produces actual mesh geometry, the 4 angles will provide meaningful coverage. No changes needed to the test definition — fix the renderer instead.

If the sphere approximation is kept long-term, reduce to 2 checkpoints (front + top-down) to save test time.

## Related
- Penumbra bug 0021: STL primitive-fit renders featureless sphere
- Test definition: `workloads/penumbra/tests/stl-import-benchy.json`
