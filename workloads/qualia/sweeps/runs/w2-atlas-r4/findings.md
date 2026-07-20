# Display-sweep findings — w2-atlas-r4

States: 1106 · skipped: 28 · errors: 0 · no-ops: 12 · leaks: 0 · settle failures: 0 · unstable reads: 0 · reset-verify failures: 0 · cross-family inconsistencies: 21 (21 cross-base) · control violations: 2

## No-op mutations (dead knobs OR probe gaps) (12)

- `junction-bubble` — {"kind":"junction","preset":"bubble"} changed nothing observable from base `cinematic` *(target already at base value — spec artifact, not a finding)*
- `junction-bubble` — {"kind":"junction","preset":"bubble"} changed nothing observable from base `standard` *(target already at base value — spec artifact, not a finding)*
- `junction-bubble` — {"kind":"junction","preset":"bubble"} changed nothing observable from base `standard` *(target already at base value — spec artifact, not a finding)*
- `junction-bubble` — {"kind":"junction","preset":"bubble"} changed nothing observable from base `workshop` *(target already at base value — spec artifact, not a finding)*
- `junction-surface` — {"kind":"junction","preset":"surface"} changed nothing observable from base `minimal` *(target already at base value — spec artifact, not a finding)*
- `junction-surface` — {"kind":"junction","preset":"surface"} changed nothing observable from base `minimal` *(target already at base value — spec artifact, not a finding)*
- `junction-surface` — {"kind":"junction","preset":"surface"} changed nothing observable from base `minimal` *(target already at base value — spec artifact, not a finding)*
- `profile-minimal` — {"kind":"profile","to":"minimal"} changed nothing observable from base `minimal`
- `profile-minimal` — {"kind":"profile","to":"minimal"} changed nothing observable from base `minimal`
- `profile-standard` — {"kind":"profile","to":"standard"} changed nothing observable from base `standard`
- `profile-workshop` — {"kind":"profile","to":"workshop"} changed nothing observable from base `workshop`
- `profile-cinematic` — {"kind":"profile","to":"cinematic"} changed nothing observable from base `cinematic`

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

- `minimal-workshop-palette` — effect 1, leak 0
- `standard-workshop-palette` — effect 1, leak 0

## Skipped mutations (no derivable alternate — extend the alternates map to cover) (4)

- `perf-labelPlacement` — 7 family(ies)
- `perf-edgeDashPatternId` — 7 family(ies)
- `perf-edgeDashSpeed` — 7 family(ies)
- `perf-curlNoisePalette` — 7 family(ies)

## Pair interactions (0 with emergent/suppressed paths)

_none — every pair matched the union of its singles_

## Cross-family inconsistencies (21; cross-base first)

Same mutation, different effect signature across families. Cross-BASE rows (same fixture,
different base profile) are the primary "display dynamics inconsistent across personas" signal;
cross-fixture-only rows may just reflect content differences.

- **[cross-base]** `persona-render-cross-context-qnode` — {"kind":"persona","persona":"render.cross-context.qnode","id":"render.cross-context.qnode"}
  - cinematic-ddv: 1 path(s); missing vs union: `perNode.*.rendered`, `socket.count`, `stats.nodeCount`, `nubs.shaded-sphere`
  - minimal-ddv: 1 path(s); missing vs union: `perNode.*.rendered`, `socket.count`, `stats.nodeCount`, `nubs.shaded-sphere`
  - minimal-qnode-junction-alignment-test: 4 path(s); missing vs union: `nubs.shaded-sphere`
  - minimal-workshop-palette: 4 path(s); missing vs union: `nubs.shaded-sphere`
  - standard-ddv: 1 path(s); missing vs union: `perNode.*.rendered`, `socket.count`, `stats.nodeCount`, `nubs.shaded-sphere`
  - standard-workshop-palette: 4 path(s); missing vs union: `socket.count`
  - workshop-ddv: 1 path(s); missing vs union: `perNode.*.rendered`, `socket.count`, `stats.nodeCount`, `nubs.shaded-sphere`
- **[cross-base]** `persona-render-junction-bubble` — {"kind":"persona","persona":"render.junction.bubble","id":"render.junction.bubble"}
  - cinematic-ddv: 1 path(s); missing vs union: `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `perf.activeJunction`, `persona.enabled.render.junction.surface`, `frame.junctions.*.source.intersection`
  - minimal-ddv: 16 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.point.0`, `frame.junctions.*.target.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.point.2`
  - minimal-qnode-junction-alignment-test: 10 path(s); missing vs union: `frame.junctions.*.target.intersection`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.point.2`
  - minimal-workshop-palette: 67 path(s)
  - standard-ddv: 1 path(s); missing vs union: `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `perf.activeJunction`, `persona.enabled.render.junction.surface`, `frame.junctions.*.source.intersection`
  - standard-workshop-palette: 1 path(s); missing vs union: `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `perf.activeJunction`, `persona.enabled.render.junction.surface`, `frame.junctions.*.source.intersection`
  - workshop-ddv: 1 path(s); missing vs union: `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `perf.activeJunction`, `persona.enabled.render.junction.surface`, `frame.junctions.*.source.intersection`
- **[cross-base]** `persona-render-junction-center` — {"kind":"persona","persona":"render.junction.center","id":"render.junction.center"}
  - cinematic-ddv: 28 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.0`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.normal.2`, `frame.junctions.*.target.point.1`
  - minimal-ddv: 39 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `persona.enabled.render.junction.bubble`
  - minimal-qnode-junction-alignment-test: 5 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `frame.junctions.*.target.point.2`
  - minimal-workshop-palette: 39 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `persona.enabled.render.junction.bubble`
  - standard-ddv: 28 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.0`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.normal.2`
  - standard-workshop-palette: 75 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `persona.enabled.render.junction.surface`
  - workshop-ddv: 28 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.0`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.normal.2`
- **[cross-base]** `persona-render-junction-surface` — {"kind":"persona","persona":"render.junction.surface","id":"render.junction.surface"}
  - cinematic-ddv: 28 path(s); missing vs union: `nubs.shaded-sphere`
  - minimal-ddv: 1 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`
  - minimal-workshop-palette: 1 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`
  - standard-ddv: 28 path(s); missing vs union: `nubs.glow-bead`
  - standard-workshop-palette: 69 path(s); missing vs union: `nubs.glow-bead`
  - workshop-ddv: 28 path(s); missing vs union: `nubs.glow-bead`
- **[cross-base]** `persona-render-junction-pull-back` — {"kind":"persona","persona":"render.junction.pull-back","id":"render.junction.pull-back"}
  - cinematic-ddv: 28 path(s); missing vs union: `persona.enabled.render.junction.surface`, `nubs.shaded-sphere`
  - minimal-ddv: 3 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`
  - minimal-qnode-junction-alignment-test: 5 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `frame.junctions.*.target.point.2`
  - minimal-workshop-palette: 27 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `persona.enabled.render.junction.bubble`
  - standard-ddv: 28 path(s); missing vs union: `nubs.glow-bead`, `persona.enabled.render.junction.surface`
  - standard-workshop-palette: 69 path(s); missing vs union: `nubs.glow-bead`, `persona.enabled.render.junction.surface`
  - workshop-ddv: 28 path(s); missing vs union: `nubs.glow-bead`, `persona.enabled.render.junction.surface`
- **[cross-base]** `persona-render-junction-voronoi` — {"kind":"persona","persona":"render.junction.voronoi","id":"render.junction.voronoi"}
  - cinematic-ddv: 40 path(s); missing vs union: `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.point.1`, `persona.enabled.render.junction.surface`, `nubs.shaded-sphere`
  - minimal-ddv: 27 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `persona.enabled.render.junction.bubble`
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`
  - minimal-workshop-palette: 35 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `persona.enabled.render.junction.bubble`
  - standard-ddv: 40 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.point.1`, `persona.enabled.render.junction.surface`
  - standard-workshop-palette: 85 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.point.1`, `persona.enabled.render.junction.surface`
  - workshop-ddv: 40 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.point.1`, `persona.enabled.render.junction.surface`
- **[cross-base]** `persona-fx-laser-rat` — {"kind":"persona","persona":"fx.laser-rat","id":"fx.laser-rat"}
  - cinematic-ddv: 23 path(s); missing vs union: `persona.enabled.fx.edge-flow`, `persona.enabled.fx.post-process`
  - minimal-ddv: 14 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.nub.color`, `frame.junctions.*.target.nub.color`, `perf.edgeShape`, `persona.enabled.fx.film-grain`, `persona.enabled.fx.vignette`, `persona.enabled.render.penumbra-backdrop`
  - minimal-qnode-junction-alignment-test: 14 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.nub.color`, `frame.junctions.*.target.nub.color`, `perf.edgeShape`, `persona.enabled.fx.film-grain`, `persona.enabled.fx.vignette`, `persona.enabled.render.penumbra-backdrop`
  - minimal-workshop-palette: 14 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.nub.color`, `frame.junctions.*.target.nub.color`, `perf.edgeShape`, `persona.enabled.fx.film-grain`, `persona.enabled.fx.vignette`, `persona.enabled.render.penumbra-backdrop`
  - standard-ddv: 22 path(s); missing vs union: `persona.enabled.fx.film-grain`, `persona.enabled.fx.vignette`, `persona.enabled.render.penumbra-backdrop`
  - standard-workshop-palette: 30 path(s); missing vs union: `persona.enabled.fx.film-grain`, `persona.enabled.fx.vignette`, `persona.enabled.render.penumbra-backdrop`
  - workshop-ddv: 20 path(s); missing vs union: `perf.nodeOpacity`, `persona.enabled.fx.film-grain`, `persona.enabled.fx.vignette`, `persona.enabled.render.penumbra-backdrop`, `persona.enabled.fx.post-process`
- **[cross-base]** `persona-fx-debug-layer-colors` — {"kind":"persona","persona":"fx.debug-layer-colors","id":"fx.debug-layer-colors"}
  - cinematic-ddv: 2 path(s); missing vs union: `halo.variant`, `halo.visible`, `socket.visible`
  - minimal-ddv: 4 path(s); missing vs union: `socket.visible`
  - minimal-qnode-junction-alignment-test: 4 path(s); missing vs union: `socket.visible`
  - minimal-workshop-palette: 4 path(s); missing vs union: `socket.visible`
  - standard-ddv: 5 path(s)
  - standard-workshop-palette: 5 path(s)
  - workshop-ddv: 1 path(s); missing vs union: `socket.variant`, `halo.variant`, `halo.visible`, `socket.visible`
- **[cross-base]** `perf-nodeHaloVariant` — {"kind":"perfAuto","field":"nodeHaloVariant"}
  - cinematic-ddv: 2 path(s); missing vs union: `halo.count`, `halo.perNode.0`, `halo.perNode.1`, `halo.perNode.2`, `halo.perNode.3`, `halo.radiusMul`, `halo.visible`, `halo.perNode.4`
  - minimal-ddv: 9 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`
  - minimal-qnode-junction-alignment-test: 6 path(s); missing vs union: `halo.perNode.1`, `halo.perNode.2`, `halo.perNode.3`, `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`
  - minimal-workshop-palette: 12 path(s)
  - standard-ddv: 9 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`
  - standard-workshop-palette: 12 path(s)
  - workshop-ddv: 2 path(s); missing vs union: `halo.count`, `halo.perNode.0`, `halo.perNode.1`, `halo.perNode.2`, `halo.perNode.3`, `halo.radiusMul`, `halo.visible`, `halo.perNode.4`
- **[cross-base]** `perf-nodeHaloRadiusMul` — {"kind":"perfAuto","field":"nodeHaloRadiusMul"}
  - cinematic-ddv: 6 path(s)
  - minimal-ddv: 1 path(s); missing vs union: `halo.perNode.*.radius`, `halo.radiusMul`
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `halo.perNode.*.radius`, `halo.radiusMul`
  - minimal-workshop-palette: 1 path(s); missing vs union: `halo.perNode.*.radius`, `halo.radiusMul`
  - standard-ddv: 1 path(s); missing vs union: `halo.perNode.*.radius`, `halo.radiusMul`
  - standard-workshop-palette: 1 path(s); missing vs union: `halo.perNode.*.radius`, `halo.radiusMul`
  - workshop-ddv: 6 path(s)
- **[cross-base]** `perf-socketVariant` — {"kind":"perfAuto","field":"socketVariant"}
  - cinematic-ddv: 2 path(s); missing vs union: `socket.count`, `socket.visible`
  - minimal-ddv: 2 path(s); missing vs union: `socket.count`, `socket.visible`
  - minimal-qnode-junction-alignment-test: 2 path(s); missing vs union: `socket.count`, `socket.visible`
  - minimal-workshop-palette: 2 path(s); missing vs union: `socket.count`, `socket.visible`
  - standard-ddv: 4 path(s)
  - standard-workshop-palette: 4 path(s)
  - workshop-ddv: 2 path(s); missing vs union: `socket.count`, `socket.visible`
- **[cross-base]** `perf-nubVariant` — {"kind":"perfAuto","field":"nubVariant"}
  - cinematic-ddv: 3 path(s); missing vs union: `nubs.ink-dot`
  - minimal-ddv: 1 path(s); missing vs union: `nubs.glow-bead`, `nubs.shaded-sphere`, `nubs.ink-dot`
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `nubs.glow-bead`, `nubs.shaded-sphere`, `nubs.ink-dot`
  - minimal-workshop-palette: 1 path(s); missing vs union: `nubs.glow-bead`, `nubs.shaded-sphere`, `nubs.ink-dot`
  - standard-ddv: 3 path(s); missing vs union: `nubs.glow-bead`
  - standard-workshop-palette: 3 path(s); missing vs union: `nubs.glow-bead`
  - workshop-ddv: 3 path(s); missing vs union: `nubs.glow-bead`
- **[cross-base]** `junction-bubble` — {"kind":"junction","preset":"bubble"}
  - cinematic-ddv: 0 path(s); missing vs union: `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `perf.activeJunction`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.point.0`
  - minimal-ddv: 14 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.point.0`, `frame.junctions.*.target.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.point.2`
  - minimal-qnode-junction-alignment-test: 8 path(s); missing vs union: `frame.junctions.*.target.intersection`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.point.2`
  - minimal-workshop-palette: 65 path(s)
  - standard-ddv: 0 path(s); missing vs union: `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `perf.activeJunction`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.point.0`
  - standard-workshop-palette: 0 path(s); missing vs union: `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `perf.activeJunction`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.point.0`
  - workshop-ddv: 0 path(s); missing vs union: `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `perf.activeJunction`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.point.0`
- **[cross-base]** `junction-center` — {"kind":"junction","preset":"center"}
  - cinematic-ddv: 26 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.0`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.normal.2`, `frame.junctions.*.target.point.1`
  - minimal-ddv: 37 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `nubs.shaded-sphere`
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `frame.junctions.*.target.point.2`
  - minimal-workshop-palette: 37 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `frame.junctions.*.source.normal.0`
  - standard-ddv: 26 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.0`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.normal.2`
  - standard-workshop-palette: 73 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
  - workshop-ddv: 26 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.0`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.normal.2`
- **[cross-base]** `junction-surface` — {"kind":"junction","preset":"surface"}
  - cinematic-ddv: 26 path(s); missing vs union: `nubs.shaded-sphere`
  - minimal-ddv: 0 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`
  - minimal-qnode-junction-alignment-test: 0 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`
  - minimal-workshop-palette: 0 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`
  - standard-ddv: 26 path(s); missing vs union: `nubs.glow-bead`
  - standard-workshop-palette: 67 path(s); missing vs union: `nubs.glow-bead`
  - workshop-ddv: 26 path(s); missing vs union: `nubs.glow-bead`
- **[cross-base]** `junction-pull-back` — {"kind":"junction","preset":"pull-back"}
  - cinematic-ddv: 26 path(s); missing vs union: `nubs.shaded-sphere`
  - minimal-ddv: 1 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`
  - minimal-qnode-junction-alignment-test: 3 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `frame.junctions.*.target.point.2`
  - minimal-workshop-palette: 25 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `nubs.shaded-sphere`
  - standard-ddv: 26 path(s); missing vs union: `nubs.glow-bead`
  - standard-workshop-palette: 67 path(s); missing vs union: `nubs.glow-bead`
  - workshop-ddv: 26 path(s); missing vs union: `nubs.glow-bead`
- **[cross-base]** `junction-voronoi` — {"kind":"junction","preset":"voronoi"}
  - cinematic-ddv: 38 path(s); missing vs union: `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.point.1`, `nubs.shaded-sphere`
  - minimal-ddv: 25 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `nubs.shaded-sphere`
  - minimal-qnode-junction-alignment-test: 1 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`
  - minimal-workshop-palette: 33 path(s); missing vs union: `frame.junctions.*.source.intersection`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.nub`, `frame.junctions.*.target.nubSilhouette`, `nubs.glow-bead`, `frame.junctions.*.source.normal.1`
  - standard-ddv: 38 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.point.1`
  - standard-workshop-palette: 83 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.point.1`
  - workshop-ddv: 38 path(s); missing vs union: `nubs.glow-bead`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.point.1`, `frame.junctions.*.target.normal.1`, `frame.junctions.*.target.point.1`
- **[cross-base]** `profile-minimal` — {"kind":"profile","to":"minimal"}
  - cinematic-ddv: 107 path(s); missing vs union: `persona.enabled.render.context-navigator`, `nubs.shaded-sphere`, `socket.count`, `socket.visible`, `perf.debugPalette`, `perf.outlineMix`, `perf.outlineThickness`, `perf.staticArrowheadsAsFallback`
  - minimal-ddv: 0 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
  - minimal-qnode-junction-alignment-test: 0 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
  - minimal-workshop-palette: 1 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
  - standard-ddv: 82 path(s); missing vs union: `halo.count`, `halo.perNode.0`, `halo.perNode.1`, `halo.perNode.2`, `halo.perNode.3`, `halo.radiusMul`, `halo.variant`, `halo.visible`
  - standard-workshop-palette: 95 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `halo.count`, `halo.perNode.0`, `halo.perNode.1`, `halo.perNode.2`
  - workshop-ddv: 104 path(s); missing vs union: `nubs.glow-bead`, `perf.edgeGradientMix`, `perf.edgeGradientRampId`, `perf.filmGrainStrength`, `perf.socketOpacity`, `persona.enabled.fx.connection-sweep`, `persona.enabled.fx.edge-flow`, `persona.enabled.fx.film-grain`
- **[cross-base]** `profile-standard` — {"kind":"profile","to":"standard"}
  - cinematic-ddv: 38 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
  - minimal-ddv: 82 path(s); missing vs union: `halo.count`, `halo.perNode.0`, `halo.perNode.1`, `halo.perNode.2`, `halo.perNode.3`, `halo.radiusMul`, `halo.variant`, `halo.visible`
  - minimal-qnode-junction-alignment-test: 31 path(s); missing vs union: `halo.count`, `halo.perNode.0`, `halo.perNode.1`, `halo.perNode.2`, `halo.perNode.3`, `halo.radiusMul`, `halo.variant`, `halo.visible`
  - minimal-workshop-palette: 95 path(s); missing vs union: `halo.count`, `halo.perNode.0`, `halo.perNode.1`, `halo.perNode.2`, `halo.perNode.3`, `halo.radiusMul`, `halo.variant`, `halo.visible`
  - standard-ddv: 0 path(s); missing vs union: `halo.count`, `halo.perNode.0`, `halo.perNode.1`, `halo.perNode.2`, `halo.perNode.3`, `halo.radiusMul`, `halo.variant`, `halo.visible`
  - standard-workshop-palette: 1 path(s); missing vs union: `halo.count`, `halo.perNode.0`, `halo.perNode.1`, `halo.perNode.2`, `halo.perNode.3`, `halo.radiusMul`, `halo.variant`, `halo.visible`
  - workshop-ddv: 33 path(s); missing vs union: `nubs.glow-bead`, `nubs.shaded-sphere`, `perf.bloomStrength`, `perf.bloomThreshold`, `perf.edgeGradientMix`, `perf.edgeGradientRampId`, `perf.filmGrainStrength`, `perf.nubVariant`
- **[cross-base]** `profile-workshop` — {"kind":"profile","to":"workshop"}
  - cinematic-ddv: 47 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
  - minimal-ddv: 104 path(s); missing vs union: `halo.perNode.*.radius`, `nubs.glow-bead`, `perf.edgeGradientMix`, `perf.edgeGradientRampId`, `perf.filmGrainStrength`, `perf.socketOpacity`, `persona.enabled.fx.connection-sweep`, `persona.enabled.fx.edge-flow`
  - minimal-qnode-junction-alignment-test: 50 path(s); missing vs union: `halo.perNode.*.radius`, `nubs.glow-bead`, `perf.edgeGradientMix`, `perf.edgeGradientRampId`, `perf.filmGrainStrength`, `perf.socketOpacity`, `persona.enabled.fx.connection-sweep`, `persona.enabled.fx.edge-flow`
  - minimal-workshop-palette: 120 path(s); missing vs union: `halo.perNode.*.radius`, `nubs.glow-bead`, `perf.edgeGradientMix`, `perf.edgeGradientRampId`, `perf.filmGrainStrength`, `perf.socketOpacity`, `persona.enabled.fx.connection-sweep`, `persona.enabled.fx.edge-flow`
  - standard-ddv: 33 path(s); missing vs union: `halo.perNode.*.radius`, `nubs.glow-bead`, `nubs.shaded-sphere`, `perf.bloomStrength`, `perf.bloomThreshold`, `perf.edgeGradientMix`, `perf.edgeGradientRampId`, `perf.filmGrainStrength`
  - standard-workshop-palette: 37 path(s); missing vs union: `halo.perNode.*.radius`, `nubs.glow-bead`, `nubs.shaded-sphere`, `perf.bloomStrength`, `perf.bloomThreshold`, `perf.edgeGradientMix`, `perf.edgeGradientRampId`, `perf.filmGrainStrength`
  - workshop-ddv: 0 path(s); missing vs union: `halo.perNode.*.radius`, `halo.radiusMul`, `nubs.glow-bead`, `nubs.shaded-sphere`, `perf.bloomStrength`, `perf.bloomThreshold`, `perf.debugPalette`, `perf.edgeGradientMix`
- **[cross-base]** `profile-cinematic` — {"kind":"profile","to":"cinematic"}
  - cinematic-ddv: 0 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
  - minimal-ddv: 107 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`, `persona.enabled.render.context-navigator`, `nubs.shaded-sphere`, `socket.count`, `socket.visible`, `halo.perNode.*.radius`
  - minimal-qnode-junction-alignment-test: 53 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `frame.junctions.*.source.point.2`, `frame.junctions.*.target.intersection`, `frame.junctions.*.target.normal.0`, `frame.junctions.*.target.normal.1`
  - minimal-workshop-palette: 123 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `nubs.shaded-sphere`, `socket.count`, `socket.visible`, `halo.perNode.*.radius`
  - standard-ddv: 38 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
  - standard-workshop-palette: 42 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
  - workshop-ddv: 47 path(s); missing vs union: `edgeShape`, `frame.junctions.*.source.intersection`, `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.nub`, `frame.junctions.*.source.nubSilhouette`, `frame.junctions.*.source.point.0`
