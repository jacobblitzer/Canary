# Display-sweep findings — w1-personas-r2

States: 153 · skipped: 0 · errors: 0 · no-ops: 0 · leaks: 0 · settle failures: 0 · unstable reads: 1 · reset-verify failures: 0 · cross-family inconsistencies: 0 (0 cross-base) · control violations: 0

## No-op mutations (dead knobs OR probe gaps) (0)

_none_

## State leaks (reverted fingerprint != family base) (0)

_none_

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

## Cross-family inconsistencies (0; cross-base first)

Same mutation, different effect signature across families. Cross-BASE rows (same fixture,
different base profile) are the primary "display dynamics inconsistent across personas" signal;
cross-fixture-only rows may just reflect content differences.

_none_
