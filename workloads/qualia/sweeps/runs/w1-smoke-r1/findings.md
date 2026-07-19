# Display-sweep findings — w1-smoke-r1

States: 12 · errors: 0 · no-ops: 2 · leaks: 2 · settle failures: 0 · unstable reads: 0 · reset-verify failures: 12

## No-op mutations (dead knobs OR probe gaps) (2)

- `persona-film-grain` — {"kind":"persona","persona":"fx.film-grain","id":"fx.film-grain"} changed nothing observable from base `minimal`
- `junction-surface` — {"kind":"junction","preset":"surface"} changed nothing observable from base `minimal`

## State leaks (reverted fingerprint != family base) (2)

- `theme-light` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `profile-workshop-roundtrip` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`

## Settle failures (0)

_none_

## Unstable double-reads (0)

_none_

## Reset-verify failures (profile/touched not clean) (12)

- `persona-film-grain` — pre:false post:false
- `persona-junction-bubble` — pre:false post:false
- `perf-edge-width-6` — pre:false post:false
- `perf-halo-aurora-vent` — pre:false post:false
- `perf-node-opacity-03` — pre:false post:false
- `perf-socket-solder` — pre:false post:false
- `perf-grid-off` — pre:false post:false
- `perf-labels-off` — pre:false post:false
- `junction-bubble` — pre:false post:false
- `junction-surface` — pre:false post:false
- `theme-light` — pre:false post:false
- `profile-workshop-roundtrip` — pre:false post:false

## Driver errors (0)

_none_
