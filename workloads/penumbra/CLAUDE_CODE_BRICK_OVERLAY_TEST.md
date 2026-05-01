# Canary Test — Penumbra Brick Overlay Regression

> **To execute:** Open Claude Code in the canary repo root. Say: "Read `specs/canary/penumbra-brick-overlay-regression.md` and execute it."

---

## Goal

Add a Canary visual regression test that captures the brick wireframe overlay (Alt+B) in Penumbra's test harness, so the current broken-overlay bug is caught visually and any future fix can be verified against a baseline.

## Context

The Penumbra brick wireframe overlay (Alt+B) is currently broken — wireframes flash, appear off-screen, or render behind the camera. The other wireframe overlays (cascades / Alt+C, AABBs / Alt+A, Alt+Shift+A) render correctly. We need a Canary test that locks down the visual state of the brick overlay so the Penumbra-side fix can be validated.

## Files Created

- `workloads/penumbra/tests/atlas-blob-bricks-overlay.json` — new test definition
- `workloads/penumbra/tests/baselines/atlas-blob-bricks-overlay/*.png` — baselines (after `canary approve`)

## Files Modified (only if needed)

- `workloads/penumbra/penumbra-hooks/canary-hooks.ts` — add an overlay-toggle hook (`window.__canaryToggleOverlay(key: string)`) **only if one doesn't already exist**. Check first; the hook surface already includes scene/camera/animation control, and may already cover overlays.
- `src/Canary.Agent.Penumbra/PenumbraBridgeAgent.cs` — only if a new CDP command is needed to invoke the toggle hook. Most likely the existing "evaluate JS in page" path already covers this.

## Comprehension Check (BEFORE writing code)

Describe to the user:
1. Whether `__canaryToggleOverlay` (or any equivalent wire-overlay hook) already exists in `penumbra-hooks/canary-hooks.ts`. If yes, what is its exact signature?
2. The exact JSON shape used by existing tests (e.g. `atlas-blob-orbit.json`) — checkpoint structure, camera params, stabilizeMs values, tolerance.
3. Whether `setup` blocks support a `commands` array or similar that runs after scene load but before checkpoint capture (this is where the overlay toggle needs to fire).

Wait for user confirmation before writing the test JSON.

## Test Design

Scene: 12-Sphere Blob (atlas-eval). This is the smallest atlas scene and produces a clearly visible brick distribution clustered around 12 sphere centers — easy to eyeball for correctness.

Sequence per checkpoint:
1. Load scene
2. Wait for atlas build complete (use existing `__canaryWaitForAtlas` pattern if present)
3. Toggle the brick overlay ON (`__canaryToggleOverlay('bricks')` or equivalent)
4. Set camera (azimuth, elevation, distance)
5. Stabilize 1500ms (atlas scene + overlay rebuild)
6. Capture screenshot

Cameras (4 checkpoints — broken state will look uniformly empty/wrong from every angle, fixed state will show brick cubes hugging the spheres from every angle):
- `front`     — azimuth 0,   elevation 0,  distance 8
- `three-quarter` — azimuth 45,  elevation 30, distance 8
- `top-down`  — azimuth 0,   elevation 85, distance 8
- `wide`      — azimuth 30,  elevation 20, distance 14

Tolerance: 0.02 (2%) — wireframes are sparse so accept a bit of edge antialiasing wobble.

## Deliverables

### 1. Test JSON (`workloads/penumbra/tests/atlas-blob-bricks-overlay.json`)

Match the shape of `atlas-blob-orbit.json` exactly. The only structural addition is the overlay-toggle command in `setup` (or per-checkpoint if the schema needs it there).

### 2. Hook (only if missing)

```typescript
// In penumbra-hooks/canary-hooks.ts
(window as any).__canaryToggleOverlay = (key: string): boolean => {
  const valid = ['cascades', 'bricks', 'fieldAABBs', 'atomAABBs', 'foreignObjects', 'pointCloud'];
  if (!valid.includes(key)) return false;
  renderer.debugOverlay.toggle(key as keyof OverlayConfig);
  return renderer.debugOverlay.getConfig()[key as keyof OverlayConfig];
};
```

Returns the new state so the harness can verify the toggle landed.

### 3. First-run capture

Run `canary run --workload penumbra --test atlas-blob-bricks-overlay`. Status will be NEW. Open the report and visually confirm: the captures should show the **broken** state — sphere render visible, brick wireframes either absent, flickering, or in clearly wrong positions.

**Do NOT approve baselines yet.** The point of the first run is to lock in a snapshot of the bug. Approval happens only after the Penumbra-side fix lands and the captures show bricks correctly clustered around spheres.

## Exit Criteria

1. `canary run --workload penumbra` discovers and runs the new test
2. First run produces a NEW status with 4 captures from 4 camera angles
3. The captured PNGs visually demonstrate the broken state (no brick wireframes visible, OR wireframes visibly off-screen / in wrong positions)
4. Test JSON parses cleanly (`canary run --workload penumbra` shows no JSON errors)
5. The overlay toggle hook (if newly added) returns true after toggle and the overlay state is reflected in subsequent renders
6. Git commit: `test: penumbra brick overlay regression capture (atlas-blob-bricks-overlay)`

## Notes for the Agent

- Do NOT touch any C# harness code unless the existing CDP-evaluate-JS path genuinely cannot reach the overlay hook. The hook is a JS function on `window`; the existing PenumbraBridgeAgent should already be able to call it.
- Do NOT approve baselines as part of this task. Baseline approval happens after the Penumbra fix.
- If you discover the overlay toggle hook already exists with a different name, use the existing one and note its signature in the test JSON's setup block.
