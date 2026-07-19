# Display-sweep findings — w1-smoke-r2

States: 12 · errors: 0 · no-ops: 1 · leaks: 2 · settle failures: 0 · unstable reads: 0 · reset-verify failures: 0

## No-op mutations (dead knobs OR probe gaps) (1)

- `junction-surface` — {"kind":"junction","preset":"surface"} changed nothing observable from base `minimal` *(target already at base value — spec artifact, not a finding)*

## State leaks (reverted fingerprint != family base) (2)

- `theme-light` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`
- `profile-workshop-roundtrip` — 1 path(s) leaked, e.g. `viewer.emissiveIntensity`

## Settle failures (0)

_none_

## Unstable double-reads (0)

_none_

## Reset-verify failures (profile/touched not clean) (0)

_none_

## Driver errors (0)

_none_
