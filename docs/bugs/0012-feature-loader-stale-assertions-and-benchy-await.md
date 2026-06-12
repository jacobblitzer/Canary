---
date: 2026-06-11
tags: [bug, canary, penumbra]
status: resolved
project: canary
severity: medium
component: "penumbra-workload tests, cdp"
---

# Four penumbra tests crash "JavaScript evaluation failed: Uncaught" — stale feature-loader assertions + a bare top-level `await`

## Symptom

`feature-loader-all-off`, `feature-loader-mutex-rejection`,
`feature-loader-quality-profile` (~70–80s in) and `stl-import-benchy`
(~17s in) crashed every run with `JavaScript evaluation failed: Uncaught` —
including on clean solo spawns, so unrelated to BUG-0011 (load-timeout).
The error text carried no detail (see Diagnosability below).

## Root causes

Three of four are **Canary-side test JSONs stale against Penumbra's
2026-05-08 graduation rework** (`b82251d` flipped 7 graduated A/C features
ON in the `default` profile and reworked the profile catalog + compat
matrix); the fourth was broken at authoring:

1. **feature-loader-all-off** — loaded the `default` profile and asserted
   every feature off; default now ships 7 features ON. Fixed (first, in a
   parallel session) by explicitly setting all 14 toggles off via
   `__canarySetDisplayState`.
2. **feature-loader-quality-profile** — asserted `meshDrivenClassification`
   on after the `quality` profile; current `quality.json` keeps it OFF
   (still a stub). Now asserts the shipped contract: `progressiveBrickBaking`
   + `eventDrivenRender` + `meshBootstrapRaymarch` on, stubs off.
3. **feature-loader-mutex-rejection** — expected a soft `invalidates`
   violation between progressiveBrickBaking (A3) and splatCache (A6); the
   graduation removed every `invalidates` edge from `FEATURE_COMPAT`
   (A3 = proactive pre-bake and A6 = lazy fill now compose). Now asserts
   zero violations for A3+A6. The B1↔B2 (`svoStorageBackend` ↔
   `vdbBackend`) hard-mutex half of the test was still valid and is kept.
4. **stl-import-benchy** — setup command `await new Promise(...)` is a bare
   top-level `await`: a SyntaxError under CDP `Runtime.evaluate` without
   `replMode` (Canary never sets replMode). The test has zero successful
   runs and no baselines — broken since authoring. Wrapped in an async
   IIFE (`awaitPromise: true` handles the rest). Bonus fix: the STL-load
   command swallowed failures by *returning* an `'STL_ERROR: …'` string the
   runner treats as success (would have baselined an empty scene); it now
   throws.

## Diagnosability fix

`CdpClient.EvaluateAsync` (both overloads) surfaced only
`exceptionDetails.text`, which is literally `"Uncaught"` for thrown errors —
the real message lives in `exception.description`. New `DescribeException`
helper appends the description's first 3 lines (message + top stack
frames), so future failures name the assertion that threw.

## Not Penumbra's bug

No Penumbra-side change needed: the profiles, compat matrix, and canary
hooks all behave as documented in their sources
(`packages/runtime/src/feature-profiles/*.json`,
`packages/runtime/src/feature-compat.ts`,
`packages/studio/canary-hooks.ts`). The tests encoded pre-graduation
contracts.

## Validation

All four pass post-fix as solo headless runs on 2026-06-11 (status New =
ran cleanly, baselines created):

- feature-loader-all-off: New (confirmed in the BUG-0011 validation sweep,
  `.retry-validation.log`)
- feature-loader-mutex-rejection: New (76s)
- feature-loader-quality-profile: New (76s)
- stl-import-benchy: New (17s — first successful run ever; STL-load
  failures now throw, so a clean pass also proves `/3dbenchy.stl` loaded)

Unit suite 309/309 with the `DescribeException` change.

## Related but separate

The full-workload sweep the same evening crashed the entire
`atlas-blob-compute-*` family (13 tests) with
`__canaryRunComputeMarcherSmoke: timed out … waiting for compute dispatch`
(`pipelineBuildPromisePending: true` at budget expiry — the in-page 15–60s
helper budgets are shorter than the ~90s compute pipeline build on this
iGPU, or the build never completes). Tracked as its own follow-up task,
not part of this bug.
