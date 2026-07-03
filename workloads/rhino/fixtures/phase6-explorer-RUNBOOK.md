# Phase-6 Explorer — operator runbook (2026-07-02)

> **ADDENDUM (2026-07-02 evening — post-FLIP, flight-recorder Phase A live): the A/B registry
> procedure below is SUPERSEDED for the 6.2 soak.** STOP-POINT 6.1 was signed off on attended
> evidence; the §3 FLIP shipped (defaults ON when unset — registry vars now redundant); bug 0058
> was downgraded (capture-timing class, does not reproduce attended). For the soak you run ONE
> session in your normal config, no registry flips:
> `canary session start --workload rhino --file C:\Repos\Canary\workloads\rhino\fixtures\phase6-explorer.3dm`
> (the new `--file` opens the doc for you — skip the manual `_Open`; still run `_CPigDisplay`
> after it loads, then walk the stations). **Expectations updated:** S1–S6, S8, S9 should all
> look CORRECT (peanut / lens / fillet / lattice+bulge / cloverleaf / bites); S7 stays the soft
> ball (flag-INDEPENDENT dense-bake defect, tracked separately). Anything else wrong = say so
> before signing off 6.2; fall back with `PENUMBRA_USE_NATIVE_DLL=0` / `PENUMBRA_HOST_FSM_TS=0`.
> Sessions now also write `manifest.json` + `telemetry-prior.ndjson` + per-capture frame-state
> markers (flight recorder Phase A).

**What this is:** one Rhino document, nine CPig field stations side by side, for *poking* —
orbit, toggle, compare, take notes. It is NOT a regression test: no checkpoints, no
assertions, nothing passes or fails. Regression versions come later, after bug 0058 is fixed
and there is a correct behavior worth locking.

**File:** `workloads/rhino/fixtures/phase6-explorer.3dm` (fields persist via M7 recipe
restore — opening the file rebuilds every native handle). Builder (one-shot, if it ever needs
regenerating — delete the .3dm first): `canary run --workload rhino --test phase6-explorer-builder`.

## Station map (left → right along +X, 120 apart)

| # | X center | contents | what CORRECT looks like |
|---|---|---|---|
| S1 | 0 | control sphere r10 | smooth sphere (sanity) |
| S2 | ~127 | union(sphere, sphere) | fused PEANUT with waist crease |
| S3 | ~247 | intersection(sphere, sphere) | small LENS (overlap volume only) |
| S4 | ~367 | smoothUnion k=5 | peanut with FILLETED waist, no crease |
| S5 | ~485 | union(gyroid box, sphere r12) | GYROID LATTICE + sphere bulge fused on +X face |
| S6 | ~607 | union of THREE spheres | 3-lobed cloverleaf blob |
| S7 | ~720 | `CPigTPMS SchwarzP` (DENSE voxel-bake path) | Schwarz-P lattice; if it renders as a soft ball, that is the flag-INDEPENDENT dense-bake defect (same both sides — control station) |
| S8 | ~846 | two spheres, UNCONSUMED | **you run it:** `_CPigDifference` → click the r12 sphere (keeper), Enter → click the r8 sphere, Enter → expect a BITE carved out |
| S9 | ~960 | gyroid box + center sphere, UNCONSUMED | **you run it:** `_CPigDifference` → click the gyroid (keeper), Enter → click the sphere, Enter → expect the lattice with a spherical CAVITY |

(S8/S9 are manual because `_CPigDifference`'s subtraction-set prompt disables preselection —
finding F11; unscriptable by design until the R1 `RunMode.Scripted` ask lands.)

## The A/B procedure — TWO sessions with a registry flip between

**Why not the live toggle:** `_PenumbraHostFsmToggle` flips only the C# flag. The 3MF compile
path reads `PENUMBRA_HOST_FSM_TS` from the **node host's environment, frozen at spawn**
(`packages/host-node/src/main.ts:143`) — so a mid-session toggle gives a MIXED state
(artifact still cascade-encoded, C# side flipped). Use the toggle to feel FSM pacing if you
like, but the composite A/B verdict needs the full flip:

1. **State A (current daily config, flag = 1):**
   `canary session start --workload rhino` → in Rhino: `_Open` the explorer .3dm →
   `_CPigDisplay` → walk the stations, run S8/S9, take notes (REPL `c` = capture, `n` = note).
   Expected per bug 0058: S2–S6 composites BROKEN (missing operands / collapsed lattice);
   S1 fine; S7 soft ball. The session telemetry will collect `gl.cascade.bake-error` lines
   automatically. Quit the session; close Rhino.
2. **Flip:** `Set-ItemProperty 'HKCU:\Environment' -Name 'PENUMBRA_HOST_FSM_TS' -Value '0'`
   (or tell Claude "flip me to legacy"). Close ALL Rhino + Canary first (bug 0057 — frozen env).
3. **State B (legacy, flag = 0):** same steps as 1. Expected: S2–S6 CORRECT (peanut / lens /
   fillet / lattice+bulge / cloverleaf); S1 fine; S7 same soft ball as A.
4. **Restore:** set the registry value back to `1` (or tell Claude "restore the flag").

Session outputs land at `workloads/rhino/sessions/<stamp>/` (SESSION_REPORT.md + captures +
telemetry). Hand Claude the two session stamps + your notes; the report's telemetry section
gives the bake-error evidence next to what you saw.

## Note template (copy per station)

```
S# <name>   A (flag=1): looks like… / anomalies…
            B (flag=0): looks like… / anomalies…
            pan/orbit observations (refine behavior, F10 feel):
```
