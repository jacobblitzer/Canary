---
id: 0021
title: Panel asserts silently read the first of N same-nicknamed panels
date: 2026-07-26
status: fixed
component: Canary.Agent.Rhino
---

# Panel asserts silently read the first of N same-nicknamed panels

## Symptom

`lytrohopper-06-effects` CRASHed with `PanelContains 'DofInfo': "aperture" not
found in ""` while the freshly built canvas visibly contained a populated
DofInfo panel. The failure text pointed at the component; the component was
fine.

## Root cause

The operator had explored in a Canary-launched Rhino session and saved the GH
document — which is the FIXTURE — with a built definition still on canvas
(62 KB vs the 4.7 KB clean loader). The Slop cleanup pulse cannot remove
saved-in builds (Slop tags do not persist through save/reopen), so every later
run had TWO generations of every panel nickname. `GrasshopperGetPanelText`
returned the first match: the stale generation, whose LF components errored
(its obsolete-Rectangle `0ca0a214` instances re-solve as PolylineCurve in
Rhino 8.32) and whose panels were therefore empty.

## Fix

`HandleGrasshopperGetPanelText` now counts matches first and fails loudly on
more than one: `Ambiguous panel nickname 'X': N panels match` with a pointer
at fixture regeneration. First-match reads under ambiguity are worse than
loud failure — the assert error blamed a component that had nothing wrong.

## Also

Fixture regenerated from its generator (`gh/make_loader_fixture.py`).
Prevention note added to `spec/LYTROHOPPER_WORKLOAD.md`: never save the
fixture with a built canvas.

## Recurrence and structural fix (2026-07-27)

The fixture was poisoned AGAIN within hours of the "never save the fixture"
doc note — the operator explored in a Canary-launched Rhino and saved, as any
user naturally will. The ambiguity guard caught it loudly this time (its first
real catch: `Ambiguous panel nickname 'DofInfo': 2 panels match`).

Docs don't fix workflows; isolation does. `TestRunner.SendSetupCommandsAsync`
now copies .gh/.ghx fixtures to `%TEMP%\canary-fixtures\<name>-<guid>.gh` and
opens the COPY. Ctrl+S in an exploration session saves the temp copy; the repo
fixture can no longer be poisoned by any in-Rhino action.
