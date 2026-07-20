# Display-sweep findings — w4-fix-verify-r1

States: 18 · skipped: 0 · errors: 0 · no-ops: 0 · leaks: 0 · settle failures: 0 · unstable reads: 0 · reset-verify failures: 0 · cross-family inconsistencies: 5 (5 cross-base) · control violations: 2

## No-op mutations (dead knobs OR probe gaps) (0)

_none_

## State leaks (reverted fingerprint != family base) (0)

_none_

## Settle failures (0)

_none_

## Unstable double-reads (0)

_none_

## Reset-verify failures (profile/touched not clean) (0)

_none_

## Driver errors (0)

_none_

## Control violations (profile-to-self must be 0 effect / 0 leak) (2)

- `workshop-ddv` — effect 1, leak 0
- `workshop-ddv` — effect 1, leak 0

## Skipped mutations (no derivable alternate — extend the alternates map to cover) (0)

_none_

## Pair interactions (2 with emergent/suppressed paths)

- `paper-x-workshop` (minimal-ddv) — a:1 b:52 ab:52; SUPPRESSED: `perf.paperVisible`
- `paper-x-workshop` (workshop-ddv) — a:1 b:0 ab:0; SUPPRESSED: `perf.paperVisible`

## Cross-family inconsistencies (5; cross-base first)

Same mutation, different effect signature across families. Cross-BASE rows (same fixture,
different base profile) are the primary "display dynamics inconsistent across personas" signal;
cross-fixture-only rows may just reflect content differences.

- **[cross-base]** `profile-workshop-roundtrip` — {"kind":"profile","to":"workshop"}
  - minimal-ddv: 105 path(s)
  - workshop-ddv: 1 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
- **[cross-base]** `profile-cinematic-roundtrip` — {"kind":"profile","to":"cinematic"}
  - minimal-ddv: 108 path(s); missing vs union: `halo.perNode.*.radius`, `nubs.shaded-sphere`, `perf.debugPalette`, `perf.outlineMix`, `perf.outlineThickness`, `perf.staticArrowheadsAsFallback`, `persona.enabled.compute.metrics`, `persona.enabled.compute.rag.eager-l3`
  - workshop-ddv: 48 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
- **[cross-base]** `perf-halo-aurora` — {"kind":"perf","field":"nodeHaloVariant","value":"aurora-vent"}
  - minimal-ddv: 9 path(s)
  - workshop-ddv: 2 path(s); missing vs union: `halo.count`, `halo.perNode.0`, `halo.perNode.1`, `halo.perNode.2`, `halo.perNode.3`, `halo.radiusMul`, `halo.visible`
- **[cross-base]** `paper-x-workshop--b` — {"kind":"profile","to":"workshop"}
  - minimal-ddv: 105 path(s)
  - workshop-ddv: 1 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
- **[cross-base]** `paper-x-workshop--ab` — {"kind":"pair","a":{"kind":"persona","persona":"render.paper","id":"render.paper"},"b":{"kind":"profile","to":"workshop"}}
  - minimal-ddv: 105 path(s)
  - workshop-ddv: 1 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
