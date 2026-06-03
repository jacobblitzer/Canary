---
title: "Per-checkpoint viewport framing collapses to extreme close-ups in shared-session suite runs"
date: 2026-06-02
tags:
  - bug
  - canary
  - rhino-agent
  - viewport
  - shared-session
status: resolved-not-canary
project: canary
component: rhino-agent-set-viewport
severity: medium
fix-commit: "self-resolved by CPig c091570"
canary-repro: "cpig-kinematics suite (any cpig-kin-* test)"
---

# Per-checkpoint viewport framing collapses to extreme close-ups in shared-session suite runs

## Summary
Running `canary run --suite cpig-kinematics` (shared Rhino session, 13 tests) produces persp / front / top / right captures with the camera zoomed to an **extreme close-up of a single body or a label** — geometry barely visible. The same tests run individually via `--test <name>` (each launching its own Rhino instance) produce correctly-framed captures with the full mechanism visible. The regression appeared today after Phase 14.7's per-checkpoint viewport override + the post-SetViewport `ZoomBoundingBox` I added to `HandleSetViewport` in `Canary.Agent.Rhino/RhinoAgent.cs`.

## Environment
- Canary master, Rhino agent commit chain `c79b9c1` (per-checkpoint Viewport) + `cde063f` (kin tuning that exposed the regression).
- Workload: `rhino` (cpig-kinematics suite, 13 tests all with `runMode: shared`).
- Tested 2026-06-02 in a single suite invocation lasting ~3 min wall time.

## Symptoms (visual diff vs solo-run)

| Test | Solo `--test` (correct) | Shared-suite (regression) |
|---|---|---|
| cpig-kin-15 watt | full V-shape linkage, all 3 bodies visible | extreme close-up of one crank pipe + `crank_torque` label |
| cpig-kin-18 2-link arm | full arm visible (then forearm flies — separate BUG) | a single fat tube filling the entire frame |
| cpig-kin-11 strandbeest | 5 overlapping poses of the coupler — beautiful sweep | close-up of `crank_hub` / `crank_pin` labels; sweep invisible |
| cpig-kin-12 klann | wide framing on the leg's motion envelope | even more extreme close-up |
| cpig-kin-09 bicycle | (was correct in earlier sessions) | empty viewport — only the world axis lines render |
| cpig-kin-07 pendulum | pendulum visible | pendulum rod visible but at an odd angle; labels overlap |

`*.fullscreen.png` siblings show the same problem (so the issue is in what Rhino renders, not in `CaptureToBitmap`'s cropping).

## Suspected root cause
`HandleSetViewport` in `src/Canary.Agent.Rhino/RhinoAgent.cs` (currently in master) does two things after activating the named view:

1. Conditionally resets camera location to `(40, -40, 30)` looking at origin, **only for Perspective / Parallel projections**. Front / Top / Right keep their axis-aligned native cameras.
2. Always runs a `ZoomBoundingBox` over the union of:
   - All `RhinoDoc.Objects` bounding boxes
   - All `IGH_PreviewObject.ClippingBox` from the GH document (when `!Hidden`)

In a fresh Rhino instance (`--test` mode), the doc + GH preview-object set is exactly what the current test built. The union bbox is correct, ZoomExtents frames it tightly.

In shared-session mode (`--suite`), Slop's CLEANUP toggle pulses *before* each test's BUILD pulse, but it may not actually delete every GH preview component's cached geometry — and even if it does, the previous test's RhinoDoc objects (baked CPlanes, frame triads, leftover geometry) might survive. The bbox union then includes phantom geometry from prior tests, pulling the active test's framing to a tiny fraction of the viewport.

An alternative hypothesis: my 4 sequential per-checkpoint `SetViewport` calls stack camera state. After `front` activates Front view + ZoomBoundingBox, then `top` activates Top view + ZoomBoundingBox, the Top viewport's camera might inherit the Front camera's zoom level (or vice versa) and end up too tight.

A third hypothesis: in shared mode, the `WaitForGrasshopperSolution` quiesce timing differs (the canvas might already be "quiesced" because the previous test left it in PostProcess), so the doc/GH-preview bbox is read at a stale moment.

## Diagnostic recipe
1. **Compare solo vs shared for a single test:**
   ```
   canary run --workload rhino --test cpig-kin-15-watt-straight-line --headless   # baseline (correct framing)
   canary run --workload rhino --suite cpig-kinematics --headless                  # regression
   ```
2. **Inspect `C:/Repos/CPig/logs/agent_viewport_diag.log`** — the existing diag log records every `SetViewport` call's view list and zoom diag string (`doc=N gh=M diag=DIAGONAL`). Compare the `diag` value across solo vs shared.
3. **Instrument `HandleSetViewport`** with a one-liner that logs the post-zoom camera position + view direction so the per-checkpoint sequence is visible.
4. **Test the hypothesis that Slop's cleanup is incomplete** — between two test runs in shared session, manually log `RhinoDoc.ActiveDoc.Objects.Count` + GH `ghDoc.Objects.Count`. If they grow each test, that's the smoking gun.
5. **Test the camera-stacking hypothesis** — temporarily skip the post-SetViewport ZoomBoundingBox call entirely; see if framing reverts to the (less-tight but stable) solo behavior.

## Workaround
None. Run tests individually via `--test <name>` if correct framing matters. Loses the ~10x speed advantage of shared sessions.

## Resolution (2026-06-02, post-investigation)
**The framing regression was NOT a Canary-side bug.** Investigation via new diagnostic logging in `HandleSetViewport` (logs `bbox.min/max/diag` + camera position to `C:/Repos/CPig/logs/agent_viewport_diag.log`) showed:

- For kin-15 Watt, the bbox diagonal + camera position are **byte-identical** between solo `--test` and shared `--suite` modes (both: `bbox.diag=166mm`, `cam=(50,-35,0)`, `tgt=(50,26,0)`).
- The agent's `ZoomBoundingBox` does the same math in both modes — there is no shared-session-specific viewport bug.

The "extreme close-up" symptom at the time of reporting was actually **extreme zoom-OUT** caused by an upstream issue: CPig BUG-0008 had just been fixed (the FD driver-field omission) but the kin fixtures' constant torques were still un-tuned. Closed-loop linkages (Watt / Strandbeest / Klann) and the 2-link arm flew apart under the un-tuned torques (Watt 250 N·mm at the time, 1500 in the speculative version, Strandbeest 200, Klann 400, 2-link 80/60). Bodies separated to (millions, millions, 0) mm. The agent correctly computed the bbox over the flown-apart geometry → bbox.diag = millions of mm → camera positioned far away → actual mechanism a single-pixel speck.

After CPig commits `f7506b6` (Watt tuning) + `c091570` (Strandbeest + Klann tuning) landed and motion was bounded (28-65mm origin deltas), the captures returned to correct framing automatically. No Canary-side code change was needed.

The diagnostic instrumentation added to `HandleSetViewport` in this investigation is retained for future framing-issue diagnosis — see `Canary.Agent.Rhino/RhinoAgent.cs::HandleSetViewport` and `C:/Repos/CPig/logs/agent_viewport_diag.log`.

## Out of scope
- The actual visual content of the diagrams (whether the bicycle skeleton resembles a bicycle, etc) — tracked as CPig BUG-0011.
- The kin-18 forearm-divergence bug — tracked as CPig BUG-0009.
- The bicycle fixtures (kin-08 / kin-09) legitimately span 4 metres because they're rolling tests — the resulting captures are small at the default 960×600 res. Not a framing bug, but a "render at higher resolution for wide bboxes" follow-up worth filing if visual diff needs more detail.
