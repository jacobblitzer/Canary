# Display-sweep findings — w2-armed-r1

States: 15 · skipped: 0 · errors: 0 · no-ops: 0 · leaks: 10 · settle failures: 0 · unstable reads: 0 · reset-verify failures: 0 · cross-family inconsistencies: 5 (0 cross-base) · control violations: 0

## No-op mutations (dead knobs OR probe gaps) (0)

_none_

## State leaks (reverted fingerprint != family base) (10)

- `persona-fx-group-atmosphere` — 190 path(s) leaked, e.g. `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`
- `persona-fx-group-atmosphere` — 46 path(s) leaked, e.g. `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`
- `persona-render-context-jewel-scene` — 190 path(s) leaked, e.g. `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`
- `persona-render-context-jewel-scene` — 46 path(s) leaked, e.g. `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`
- `persona-render-context-navigator` — 190 path(s) leaked, e.g. `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`
- `persona-render-context-navigator` — 46 path(s) leaked, e.g. `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`
- `persona-render-cross-context-portal` — 190 path(s) leaked, e.g. `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`
- `persona-render-cross-context-portal` — 46 path(s) leaked, e.g. `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`
- `persona-render-qverse-graph-nav` — 190 path(s) leaked, e.g. `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`
- `persona-render-qverse-graph-nav` — 46 path(s) leaked, e.g. `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`

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

## Skipped mutations (no derivable alternate — extend the alternates map to cover) (0)

_none_

## Pair interactions (0 with emergent/suppressed paths)

_none — every pair matched the union of its singles_

## Cross-family inconsistencies (5; cross-base first)

Same mutation, different effect signature across families. Cross-BASE rows (same fixture,
different base profile) are the primary "display dynamics inconsistent across personas" signal;
cross-fixture-only rows may just reflect content differences.

- [cross-fixture] `persona-fx-group-atmosphere` — {"kind":"persona","persona":"fx.group-atmosphere","id":"fx.group-atmosphere"}
  - minimal-implicit-geometry-in-ctx-fundamentals: 191 path(s)
  - minimal-qnode-junction-alignment-test-in-ctx-active: 1 path(s); missing vs union: `frame.cameraDirection.0`, `frame.cameraDirection.1`, `frame.cameraDirection.2`, `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`
  - minimal-workshop-palette-in-ctx-showcase: 47 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.1`, `frame.junctions.*.source.point.2`
- [cross-fixture] `persona-render-context-jewel-scene` — {"kind":"persona","persona":"render.context-jewel-scene","id":"render.context-jewel-scene"}
  - minimal-implicit-geometry-in-ctx-fundamentals: 191 path(s)
  - minimal-qnode-junction-alignment-test-in-ctx-active: 1 path(s); missing vs union: `frame.cameraDirection.0`, `frame.cameraDirection.1`, `frame.cameraDirection.2`, `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`
  - minimal-workshop-palette-in-ctx-showcase: 47 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.1`, `frame.junctions.*.source.point.2`
- [cross-fixture] `persona-render-context-navigator` — {"kind":"persona","persona":"render.context-navigator","id":"render.context-navigator"}
  - minimal-implicit-geometry-in-ctx-fundamentals: 191 path(s)
  - minimal-qnode-junction-alignment-test-in-ctx-active: 1 path(s); missing vs union: `frame.cameraDirection.0`, `frame.cameraDirection.1`, `frame.cameraDirection.2`, `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`
  - minimal-workshop-palette-in-ctx-showcase: 47 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.1`, `frame.junctions.*.source.point.2`
- [cross-fixture] `persona-render-cross-context-portal` — {"kind":"persona","persona":"render.cross-context.portal","id":"render.cross-context.portal"}
  - minimal-implicit-geometry-in-ctx-fundamentals: 191 path(s)
  - minimal-qnode-junction-alignment-test-in-ctx-active: 1 path(s); missing vs union: `frame.cameraDirection.0`, `frame.cameraDirection.1`, `frame.cameraDirection.2`, `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`
  - minimal-workshop-palette-in-ctx-showcase: 47 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.1`, `frame.junctions.*.source.point.2`
- [cross-fixture] `persona-render-qverse-graph-nav` — {"kind":"persona","persona":"render.qverse-graph-nav","id":"render.qverse-graph-nav"}
  - minimal-implicit-geometry-in-ctx-fundamentals: 191 path(s)
  - minimal-qnode-junction-alignment-test-in-ctx-active: 1 path(s); missing vs union: `frame.cameraDirection.0`, `frame.cameraDirection.1`, `frame.cameraDirection.2`, `frame.cameraPosition.0`, `frame.cameraPosition.1`, `frame.cameraPosition.2`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`
  - minimal-workshop-palette-in-ctx-showcase: 47 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.1`, `frame.junctions.*.source.point.2`
