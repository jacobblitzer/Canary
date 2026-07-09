---
date: 2026-05-07
status: shipped
source: extracted from Canary/AGENTS.md per STANDARD.md §3 + §19
---

# Penumbra Phase 0 feature loader (ADR 0015, shipped 2026-05-07)


Penumbra now ships a `DisplayState.features` axis with 14 toggles for
research / progressive features (A3, A4, A6, B1, B2, B4, B5, C1–C4, C6,
C8, C9). The Canary surface for this is:

- **5 new `__canary*` hooks** wired in `packages/studio/canary-hooks.ts`:
  `__canaryGetFeatureStatus(key)`, `__canaryGetAllFeatureStatus()`,
  `__canaryLoadFeatureProfile(name)`,
  `__canaryValidateCurrentFeatures()`,
  `__canaryCaptureFeatureEvents(durationMs)`. All follow the
  never-rejects pattern (`{ok, ...}` envelope).
- **`__canaryGetEvalSnapshot()` extended** with a `featureToggles` field
  so every screenshot's metadata includes which feature stubs were on
  at capture time.
- **5 new fixtures** under `workloads/penumbra/tests/feature-loader-*.json`:
  `feature-loader-all-off` (pixel-identical regression baseline),
  `feature-loader-performance-profile`,
  `feature-loader-quality-profile`,
  `feature-loader-mutex-rejection` (verifies B1+B2 mutex; A3+A6 soft
  invalidates were removed 2026-05-08 when both graduated and were
  found to compose — the fixture should be retargeted at a still-
  extant invalidates pair when one is added in the future),
  `feature-loader-stub-wiring` (toggles each of 14
  features one-at-a-time, asserts `activations >= 1`).
- **Suite** `workloads/penumbra/suites/feature-loader.json` runs all 5.

Feature toggles flow through the existing generic `__canarySetDisplayState`
hook — no per-feature setter needed. Profile-name shortcuts available
via `__canaryLoadFeatureProfile('default'|'performance'|'quality')`. The
matrix rejects mutex pairs at toggle time; soft invalidates are accepted
but produce a `DisplayBlocker` entry. See Penumbra ADR 0015 + the
explained doc at
`C:/Repos/Penumbra/docs/research/2026-05-06-progressive-atlas-options-explained.md`.
