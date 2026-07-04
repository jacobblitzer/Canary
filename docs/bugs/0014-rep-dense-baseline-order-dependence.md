---
date: 2026-07-03
tags: [bug, canary, rhino, baseline, test-authoring]
status: open
project: canary
severity: medium
component: "workloads/rhino test authoring"
---

# 0014 — cpig-fieldops-rep-dense-pointcloud-mesh baseline encodes viewport state its own test never establishes

## Symptom

`canary run --workload rhino --suite cpig-fieldops` fails `cpig-fieldops-rep-dense-pointcloud-mesh`
with the degenerate comparison `diff=0.00%, ssim=0.0000, tol=0.50%` — an image-DIMENSION mismatch,
not a pixel regression. Candidate ≈423×326 (quarter-ish viewport, captured at `q=98% … refining`);
baseline 1116×632 (maximized solo perspective). Reproduced twice back-to-back 2026-07-03 (R2.2
verification runs). The same test is expected to pass inside `cpig-display-matrix`.

## Root cause (test authoring, pre-existing — exposed, not caused, by the R2.2 runs)

The test is a member of BOTH `cpig-fieldops` and `cpig-display-matrix`. Its R1.3-approved baseline
was blessed from a **display-matrix suite run**, where the preceding `cpig-repmatrix-*` tests'
camera recipe leaves the shared Rhino in a maximized solo-perspective viewport. The rep-dense test
itself sets only `setup.viewport {800,600}` and never maximizes — so run under `cpig-fieldops`
(whose other five tests are `mode:capture` and never maximize either), the capture context is
whatever the fresh Rhino window provides, and the comparison degenerates on size.

Control experiment (same session): `cpig-booleans-00-union-sphere-sphere` standalone passes at
0.0% with 1116×632 on both sides — its recipe establishes its own camera/maximize state. The
window and the engine are fine; the rep-dense test simply inherits state instead of establishing it.

Secondary defect, same test: its final capture is not steady-gated (candidate shows `refining`) —
the checkpoint relies on the 8000 ms stabilize instead of a `requireSteady` wait.

## Fix direction

Give the test the same self-sufficient camera recipe the repmatrix generator emits (decoy
`_SelSrf` solo-perspective + maximize) + a `requireSteady` final wait, then RE-APPROVE its
baseline (content change ⇒ operator eyeball per the mass-approval guardrail). Alternatively drop
it from `cpig-fieldops` and let it live only in `cpig-display-matrix` where its context holds.
Baseline re-approval makes this an operator-attended fix — parked during R2 (campaign rule: no
test-content changes that need re-approval mid-refactor); candidates at
`workloads/rhino/results/cpig-fieldops-rep-dense-pointcloud-mesh/`.

## Note

Suite-membership order-dependence is a general hazard: any test whose baseline was approved from
a shared-suite run may silently depend on predecessor state. The repmatrix generator's re-select +
camera-recipe discipline exists precisely because of this class; rep-dense predates it.
