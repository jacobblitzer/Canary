# Display-sweep findings — desktop-mini-r1

States: 64 · skipped: 2 · errors: 0 · no-ops: 0 · leaks: 0 · settle failures: 0 · unstable reads: 0 · reset-verify failures: 0 · cross-family inconsistencies: 5 (5 cross-base) · control violations: 0

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

## Control violations (profile-to-self must be 0 effect / 0 leak) (0)

_none_

## Skipped mutations (no derivable alternate — extend the alternates map to cover) (1)

- `perf-labelPlacement` — 2 family(ies)

## Pair interactions (0 with emergent/suppressed paths)

_none — every pair matched the union of its singles_

## Cross-family inconsistencies (5; cross-base first)

Same mutation, different effect signature across families. Cross-BASE rows (same fixture,
different base profile) are the primary "display dynamics inconsistent across personas" signal;
cross-fixture-only rows may just reflect content differences.

- **[cross-base]** `persona-render-graph-scene` — {"kind":"persona","persona":"render.graph-scene","id":"render.graph-scene"}
  - cinematic-ddv: 4 path(s); missing vs union: `sceneGraph.enabledPasses.0`
  - minimal-ddv: 2 path(s); missing vs union: `sceneGraph.enabledPasses.4`, `sceneGraph.enabledPasses.5`, `sceneGraph.enabledPasses.6`
- **[cross-base]** `persona-fx-chromatic-aberration` — {"kind":"persona","persona":"fx.chromatic-aberration","id":"fx.chromatic-aberration"}
  - cinematic-ddv: 9 path(s)
  - minimal-ddv: 3 path(s); missing vs union: `sceneGraph.enabledPasses.2`, `sceneGraph.enabledPasses.3`, `sceneGraph.enabledPasses.4`, `sceneGraph.enabledPasses.5`, `sceneGraph.enabledPasses.6`, `sceneGraph.enabledPasses.7`
- **[cross-base]** `persona-render-penumbra-fallback` — {"kind":"persona","persona":"render.penumbra-fallback","id":"render.penumbra-fallback"}
  - cinematic-ddv: 4 path(s); missing vs union: `sceneGraph.enabledPasses.1`
  - minimal-ddv: 4 path(s); missing vs union: `sceneGraph.enabledPasses.6`
- **[cross-base]** `persona-render-junction-center` — {"kind":"persona","persona":"render.junction.center","id":"render.junction.center"}
  - cinematic-ddv: 28 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.0`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.normal.2`, `frame.junctions.*.target.point.1`
  - minimal-ddv: 39 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `persona.enabled.render.junction.bubble`
- **[cross-base]** `persona-fx-time-lapse` — {"kind":"persona","persona":"fx.time-lapse","id":"fx.time-lapse"}
  - cinematic-ddv: 3 path(s); missing vs union: `dom.hostClasses.0`
  - minimal-ddv: 3 path(s); missing vs union: `dom.hostClasses.2`
