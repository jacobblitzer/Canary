# Display-sweep findings — w3-pairs-r1

States: 60 · skipped: 0 · errors: 0 · no-ops: 0 · leaks: 26 · settle failures: 0 · unstable reads: 0 · reset-verify failures: 2 · cross-family inconsistencies: 16 (0 cross-base) · control violations: 0

## No-op mutations (dead knobs OR probe gaps) (0)

_none_

## State leaks (reverted fingerprint != family base) (26)

- `laserrat-x-theme--a` — 2 path(s) leaked, e.g. `persona.enabled.fx.post-process`, `viewer.emissiveIntensity`
- `laserrat-x-theme--a` — 2 path(s) leaked, e.g. `persona.enabled.fx.post-process`, `viewer.emissiveIntensity`
- `laserrat-x-theme--b` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `laserrat-x-theme--ab` — 2 path(s) leaked, e.g. `persona.enabled.fx.post-process`, `viewer.emissiveIntensity`
- `laserrat-x-theme--ab` — 1 path(s) leaked, e.g. `persona.enabled.fx.post-process`
- `laserrat-x-junctioncenter--a` — 2 path(s) leaked, e.g. `persona.enabled.fx.post-process`, `viewer.emissiveIntensity`
- `laserrat-x-junctioncenter--a` — 2 path(s) leaked, e.g. `persona.enabled.fx.post-process`, `viewer.emissiveIntensity`
- `laserrat-x-junctioncenter--b` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `laserrat-x-junctioncenter--b` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `laserrat-x-junctioncenter--ab` — 2 path(s) leaked, e.g. `persona.enabled.fx.post-process`, `viewer.emissiveIntensity`
- `laserrat-x-junctioncenter--ab` — 2 path(s) leaked, e.g. `persona.enabled.fx.post-process`, `viewer.emissiveIntensity`
- `postprocess-x-filmgrain--a` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `postprocess-x-filmgrain--a` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `postprocess-x-filmgrain--b` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `postprocess-x-filmgrain--b` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `postprocess-x-filmgrain--ab` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `postprocess-x-filmgrain--ab` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `paper-x-workshop--a` — 2 path(s) leaked, e.g. `touched.0`, `viewer.emissiveIntensity`
- `paper-x-workshop--a` — 2 path(s) leaked, e.g. `touched.0`, `viewer.emissiveIntensity`
- `paper-x-workshop--b` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `paper-x-workshop--b` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `paper-x-workshop--ab` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `paper-x-workshop--ab` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `theme-x-workshop--a` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `theme-x-workshop--b` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `theme-x-workshop--ab` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`

## Settle failures (0)

_none_

## Unstable double-reads (0)

_none_

## Reset-verify failures (profile/touched not clean) (2)

- `paper-x-workshop--a` — pre:true post:false
- `paper-x-workshop--a` — pre:true post:false

## Driver errors (0)

_none_

## Control violations (profile-to-self must be 0 effect / 0 leak) (0)

_none_

## Skipped mutations (no derivable alternate — extend the alternates map to cover) (0)

_none_

## Pair interactions (2 with emergent/suppressed paths)

- `paper-x-workshop` (minimal-ddv) — a:3 b:53 ab:54; SUPPRESSED: `perf.paperVisible`
- `paper-x-workshop` (minimal-workshop-palette) — a:3 b:52 ab:53; SUPPRESSED: `perf.paperVisible`

## Cross-family inconsistencies (16; cross-base first)

Same mutation, different effect signature across families. Cross-BASE rows (same fixture,
different base profile) are the primary "display dynamics inconsistent across personas" signal;
cross-fixture-only rows may just reflect content differences.

- [cross-fixture] `halovar-x-radiusmul--a` — {"kind":"perfAuto","field":"nodeHaloVariant"}
  - minimal-ddv: 9 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`
  - minimal-workshop-palette: 12 path(s)
- [cross-fixture] `halovar-x-radiusmul--ab` — {"kind":"pair","a":{"kind":"perfAuto","field":"nodeHaloVariant"},"b":{"kind":"perfAuto","field":"nodeHaloRadiusMul"}}
  - minimal-ddv: 10 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`
  - minimal-workshop-palette: 13 path(s)
- [cross-fixture] `halovar-x-debugpalette--a` — {"kind":"perfAuto","field":"nodeHaloVariant"}
  - minimal-ddv: 9 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`
  - minimal-workshop-palette: 12 path(s)
- [cross-fixture] `halovar-x-debugpalette--ab` — {"kind":"pair","a":{"kind":"perfAuto","field":"nodeHaloVariant"},"b":{"kind":"perfAuto","field":"debugPalette"}}
  - minimal-ddv: 10 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`
  - minimal-workshop-palette: 13 path(s)
- [cross-fixture] `junctioncenter-x-nubvar--a` — {"kind":"junction","preset":"center"}
  - minimal-ddv: 37 path(s)
  - minimal-workshop-palette: 37 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
- [cross-fixture] `junctioncenter-x-nubvar--ab` — {"kind":"pair","a":{"kind":"junction","preset":"center"},"b":{"kind":"perfAuto","field":"nubVariant"}}
  - minimal-ddv: 38 path(s)
  - minimal-workshop-palette: 38 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
- [cross-fixture] `junctioncenter-x-socketvar--a` — {"kind":"junction","preset":"center"}
  - minimal-ddv: 37 path(s)
  - minimal-workshop-palette: 37 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
- [cross-fixture] `junctioncenter-x-socketvar--ab` — {"kind":"pair","a":{"kind":"junction","preset":"center"},"b":{"kind":"perfAuto","field":"socketVariant"}}
  - minimal-ddv: 39 path(s)
  - minimal-workshop-palette: 39 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
- [cross-fixture] `junctioncenter-x-edgeshape--a` — {"kind":"junction","preset":"center"}
  - minimal-ddv: 37 path(s)
  - minimal-workshop-palette: 37 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
- [cross-fixture] `junctioncenter-x-edgeshape--ab` — {"kind":"pair","a":{"kind":"junction","preset":"center"},"b":{"kind":"perfAuto","field":"edgeShape"}}
  - minimal-ddv: 39 path(s)
  - minimal-workshop-palette: 39 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
- [cross-fixture] `laserrat-x-junctioncenter--b` — {"kind":"junction","preset":"center"}
  - minimal-ddv: 38 path(s)
  - minimal-workshop-palette: 38 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
- [cross-fixture] `laserrat-x-junctioncenter--ab` — {"kind":"pair","a":{"kind":"persona","persona":"fx.laser-rat","id":"fx.laser-rat"},"b":{"kind":"junction","preset":"center"}}
  - minimal-ddv: 51 path(s)
  - minimal-workshop-palette: 51 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
- [cross-fixture] `paper-x-workshop--b` — {"kind":"profile","to":"workshop"}
  - minimal-ddv: 106 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`, `persona.enabled.render.context-navigator`
  - minimal-workshop-palette: 122 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
- [cross-fixture] `paper-x-workshop--ab` — {"kind":"pair","a":{"kind":"persona","persona":"render.paper","id":"render.paper"},"b":{"kind":"profile","to":"workshop"}}
  - minimal-ddv: 107 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`, `persona.enabled.render.context-navigator`
  - minimal-workshop-palette: 123 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
- [cross-fixture] `theme-x-workshop--b` — {"kind":"profile","to":"workshop"}
  - minimal-ddv: 106 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`, `persona.enabled.render.context-navigator`
  - minimal-workshop-palette: 121 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`, `viewer.emissiveIntensity`
- [cross-fixture] `theme-x-workshop--ab` — {"kind":"pair","a":{"kind":"theme","value":"light"},"b":{"kind":"profile","to":"workshop"}}
  - minimal-ddv: 110 path(s); missing vs union: `halo.perNode.4`, `halo.perNode.5`, `halo.perNode.6`, `persona.enabled.render.context-navigator`
  - minimal-workshop-palette: 126 path(s); missing vs union: `frame.junctions.*.source.normal.0`, `frame.junctions.*.source.normal.1`, `frame.junctions.*.source.normal.2`, `frame.junctions.*.source.point.1`
