---
date: 2026-05-08
status: shipped
source: extracted from Canary/CLAUDE.md per STANDARD.md §3 + §19
---

# Penumbra A3 multiscale foundation + 7 graduated features (PR #6 + #7 + #8 merged 2026-05-08)


Penumbra shipped the A3 progressive coarse → medium → fine brick
baking foundation plus 6 follow-on graduations on top of it. Of
the 14 Phase 0 feature stubs, **7 are now real implementations**:

| Feature | Real impl | Behavior |
|---|---|---|
| A3 progressiveBrickBaking | 2026-05-08 | Async 3-tier bake (2³ → 4³ → 8³) with RAF yields between tiers. ~50ms coarse / ~250ms medium / ~2.5s fine. User sees first chunky pixel ~8× sooner. |
| A4 resolutionRampAtlasBuild | 2026-05-08 | Caps render resolution per A3 tier (0.25× → 0.33× → 0.5× → 1.0×). Composes multiplicatively with motion-aware ProgressiveQualityController. |
| C2 eventDrivenRender | 2026-05-07 | EventEmitter for brick-complete + (new) tier-complete events. Render gates on dirty flag. |
| C4 silhouetteFirstBake | 2026-05-08 | Reorders dispatch list by camera-near priority WITHIN each A3 tier. |
| C9 meshBootstrapRaymarch | 2026-05-07 | CPU dual-contouring mesh provides initial-t guess; rays start from mesh hit. |
| C1 persistentThreadBake | 2026-05-08 | Coarse + medium A3 tier dispatches use atomic-claim work loop (E5 7g pattern). Best on multi-SM discrete; iGPU change negligible. |
| A6 splatCache (foundation) | 2026-05-08 | marchRay atomicOr writes dirty-cell markers on tape-fallback. `cascadeManager.drainDirtyCells()` API. **Backfill-build trigger pending Phase 11.5.** |

Plus **E5 compute marcher early-start** (free-rider on A3): the
compute marcher inherits tier-aware sampling automatically through
`main-atlas-helpers.wgsl` concatenation, so it can render during
A3 build window.

**Compatibility matrix changes** (2026-05-08): A-series and
C-series features all compose post-graduation. Hard mutex pairs
remain only between alternative storage backends (B1 / B2 / B5 —
indirection structures) and alternative classification sources
(B1 / B2 / C8). No soft invalidates remain.

**Storage-buffer budget** (post-PR #8): A3's tier state + tier-
slot translation packed into ONE storage buffer (frees `@group(1)
@binding(16)` for A6's dirty-cells). Total fragment-stage count
stays at 16/16 (the negotiated max).

**Pending Canary work** (Penumbra Phase 12, not yet shipped on
this side):
- `atlas-blob-progressive-{coarse,medium,fine}.json` — capture
  per-tier outputs to verify progressive UX
- `atlas-blob-{a4-res-ramp,c4-silhouette-first,c1-persistent}.json`
  — toggle isolation per graduated feature
- `atlas-blob-a6-splatcache-foundation.json` — A6 ON; verify
  drainDirtyCells returns non-empty after camera reveal
- New CDP hooks: `__canaryWaitForTier(tier)`,
  `__canaryGetTierBuildState()`, `__canarySplatCacheStats()`,
  `__canaryDrainDirtyCells()` (programmatic A6 verification)
- Suite: `progressive-bake.json`

**Debugging-prep doc** at
`Penumbra/docs/debug-sessions/2026-05-08-progressive-bundle-debugging-prep.md`
documents 8 test scenarios + per-feature off-ramps for the next
session.

See `Penumbra/docs/research/2026-05-08-a3-followon-phases.md` for
the resumption pointer; `feature-loader-mutex-rejection` should be
retargeted (the A3+A6 invalidates pair it covered no longer
exists).
