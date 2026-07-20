# Display-sweep findings — w1-personas-r1

States: 153 · skipped: 0 · errors: 0 · no-ops: 0 · leaks: 26 · settle failures: 0 · unstable reads: 1 · reset-verify failures: 0 · cross-family inconsistencies: 26 (0 cross-base) · control violations: 0

## No-op mutations (dead knobs OR probe gaps) (0)

_none_

## State leaks (reverted fingerprint != family base) (26)

- `persona-fx-label-bloom` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-fx-magnetic-snap` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-fx-node-pulse` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-fx-outline` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-fx-post-process` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-fx-selection-halo` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-fx-sounds` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-fx-time-lapse` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-fx-vignette` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-interaction-box-select` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-interaction-drag` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-interaction-fly-to` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-interaction-hover` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-context-jewel-hud` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-context-jewel-scene` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-context-navigator` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-cross-context-portal` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-curl-noise-field` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-edges` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-graph-scene` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-gumball` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-labels` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-nodes` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-penumbra-backdrop` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-penumbra-fallback` — 1 path(s) leaked, e.g. `dom.styleCount`
- `persona-render-qverse-graph-nav` — 1 path(s) leaked, e.g. `dom.styleCount`

## Settle failures (0)

_none_

## Unstable double-reads (1)

- `persona-render-penumbra-backdrop` — mutated:UNSTABLE reverted:ok

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

## Cross-family inconsistencies (26; cross-base first)

Same mutation, different effect signature across families. Cross-BASE rows (same fixture,
different base profile) are the primary "display dynamics inconsistent across personas" signal;
cross-fixture-only rows may just reflect content differences.

- [cross-fixture] `persona-fx-label-bloom` — {"kind":"persona","persona":"fx.label-bloom","id":"fx.label-bloom"}
  - minimal-ddv: 4 path(s)
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 3 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-fx-magnetic-snap` — {"kind":"persona","persona":"fx.magnetic-snap","id":"fx.magnetic-snap"}
  - minimal-ddv: 4 path(s)
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 3 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-fx-node-pulse` — {"kind":"persona","persona":"fx.node-pulse","id":"fx.node-pulse"}
  - minimal-ddv: 5 path(s)
  - minimal-qnode-junction-alignment-test: 4 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 4 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-fx-outline` — {"kind":"persona","persona":"fx.outline","id":"fx.outline"}
  - minimal-ddv: 4 path(s)
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 3 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-fx-post-process` — {"kind":"persona","persona":"fx.post-process","id":"fx.post-process"}
  - minimal-ddv: 4 path(s)
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 3 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-fx-selection-halo` — {"kind":"persona","persona":"fx.selection-halo","id":"fx.selection-halo"}
  - minimal-ddv: 4 path(s)
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 3 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-fx-sounds` — {"kind":"persona","persona":"fx.sounds","id":"fx.sounds"}
  - minimal-ddv: 4 path(s)
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 3 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-fx-time-lapse` — {"kind":"persona","persona":"fx.time-lapse","id":"fx.time-lapse"}
  - minimal-ddv: 4 path(s)
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 3 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-fx-vignette` — {"kind":"persona","persona":"fx.vignette","id":"fx.vignette"}
  - minimal-ddv: 4 path(s)
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 3 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-interaction-box-select` — {"kind":"persona","persona":"interaction.box-select","id":"interaction.box-select"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-interaction-drag` — {"kind":"persona","persona":"interaction.drag","id":"interaction.drag"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-interaction-fly-to` — {"kind":"persona","persona":"interaction.fly-to","id":"interaction.fly-to"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-interaction-hover` — {"kind":"persona","persona":"interaction.hover","id":"interaction.hover"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-context-jewel-hud` — {"kind":"persona","persona":"render.context-jewel-hud","id":"render.context-jewel-hud"}
  - minimal-ddv: 3 path(s)
  - minimal-qnode-junction-alignment-test: 2 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 2 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-context-jewel-scene` — {"kind":"persona","persona":"render.context-jewel-scene","id":"render.context-jewel-scene"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-context-navigator` — {"kind":"persona","persona":"render.context-navigator","id":"render.context-navigator"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-cross-context-portal` — {"kind":"persona","persona":"render.cross-context.portal","id":"render.cross-context.portal"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-curl-noise-field` — {"kind":"persona","persona":"render.curl-noise-field","id":"render.curl-noise-field"}
  - minimal-ddv: 6 path(s)
  - minimal-qnode-junction-alignment-test: 5 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 5 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-edges` — {"kind":"persona","persona":"render.edges","id":"render.edges"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-graph-scene` — {"kind":"persona","persona":"render.graph-scene","id":"render.graph-scene"}
  - minimal-ddv: 3 path(s)
  - minimal-qnode-junction-alignment-test: 2 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 2 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-gumball` — {"kind":"persona","persona":"render.gumball","id":"render.gumball"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-labels` — {"kind":"persona","persona":"render.labels","id":"render.labels"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-nodes` — {"kind":"persona","persona":"render.nodes","id":"render.nodes"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-penumbra-backdrop` — {"kind":"persona","persona":"render.penumbra-backdrop","id":"render.penumbra-backdrop"}
  - minimal-ddv: 5 path(s)
  - minimal-qnode-junction-alignment-test: 4 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 2 path(s); missing vs union: `dom.styleCount`, `sceneGraph.byType.Mesh`, `sceneGraph.total`
- [cross-fixture] `persona-render-penumbra-fallback` — {"kind":"persona","persona":"render.penumbra-fallback","id":"render.penumbra-fallback"}
  - minimal-ddv: 5 path(s)
  - minimal-qnode-junction-alignment-test: 4 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 4 path(s); missing vs union: `dom.styleCount`
- [cross-fixture] `persona-render-qverse-graph-nav` — {"kind":"persona","persona":"render.qverse-graph-nav","id":"render.qverse-graph-nav"}
  - minimal-ddv: 2 path(s)
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `dom.styleCount`
  - minimal-workshop-palette: 1 path(s); missing vs union: `dom.styleCount`
