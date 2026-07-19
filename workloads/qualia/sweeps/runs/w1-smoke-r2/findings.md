# Display-sweep findings — w1-smoke-r2

States: 12 · skipped: 0 · errors: 0 · no-ops: 1 · leaks: 2 · settle failures: 0 · unstable reads: 0 · reset-verify failures: 0 · cross-family inconsistencies: 0 (0 cross-base) · control violations: 0

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

## Control violations (profile-to-self must be 0 effect / 0 leak) (0)

_none_

## Skipped mutations (no derivable alternate — extend the alternates map to cover) (0)

_none_

## Cross-family inconsistencies (0; cross-base first)

Same mutation, different effect signature across families. Cross-BASE rows (same fixture,
different base profile) are the primary "display dynamics inconsistent across personas" signal;
cross-fixture-only rows may just reflect content differences.

_none_
