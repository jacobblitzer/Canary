# CLAUDE_CODE_RUNNER.md — Running Canary Visual Regression Tests for Penumbra

## Overview

This document describes how Claude Code uses Canary to detect visual regressions in Penumbra after code changes. The workflow is fully automated — no human interaction needed during the test run.

---

## Prerequisites

Before running tests:
1. Penumbra repo at path specified in `workloads/penumbra/workload.json` → `penumbraConfig.projectDir`
2. Chrome or Edge installed (auto-detected)
3. Node.js + npm installed (for Vite dev server)
4. Canary solution built: `dotnet build Canary.sln`
5. Penumbra's Canary hooks integrated in `test/main.ts` (see `penumbra-hooks/canary-hooks.ts`)

---

## The Workflow

### After Making a Change to Penumbra

```bash
# 1. Run the full Penumbra visual regression suite
canary run --workload penumbra

# 2. Check results
canary report --workload penumbra
```

### What Happens Under the Hood

1. Canary reads `workloads/penumbra/workload.json`
2. The bridge agent starts:
   - Launches Vite dev server (`npm run dev` in the Penumbra repo)
   - Launches Chrome with `--remote-debugging-port=9222 --force-device-scale-factor=1`
   - Navigates Chrome to `http://localhost:3000?backend=webgpu`
   - Locks the canvas to 960×540 pixels
3. For each test definition in `workloads/penumbra/tests/`:
   - Loads the specified scene via `window.__canarySetScene(index)`
   - Waits for atlas build to complete (if applicable)
   - For each checkpoint:
     - Sets camera to exact (azimuth, elevation, distance) via `window.__canarySetCamera()`
     - Waits for render stabilization
     - Captures screenshot via CDP `Page.captureScreenshot` (clipped to canvas)
     - Compares against baseline using pixel diff + SSIM
4. Bridge agent shuts down Chrome + Vite
5. Harness generates HTML report

### Interpreting Results

| Status | Meaning |
|--------|---------|
| **PASS** | Screenshot matches baseline within tolerance (typically ≤2% pixel diff) |
| **FAIL** | Too many pixels differ — visual regression detected |
| **NEW** | No baseline exists yet — run `canary approve` to establish one |
| **CRASHED** | Browser or Vite process died during test |

### On First Run (Establishing Baselines)

```bash
# First run — all tests show NEW (no baselines yet)
canary run --workload penumbra

# Review the report — verify captures look correct
canary report --workload penumbra

# Approve all captures as baselines
canary approve --workload penumbra
```

### After Intentional Visual Changes

If you deliberately changed rendering behavior (new shader, different lighting, etc.):

```bash
# Run tests — expect FAILs for affected scenes
canary run --workload penumbra

# Review the report — verify the diffs are expected
canary report --workload penumbra

# Approve new baselines for specific tests
canary approve --workload penumbra --test tape-csg-orbit
canary approve --workload penumbra --test atlas-blob-orbit

# Or approve all at once if all changes are intentional
canary approve --workload penumbra
```

---

## Test Coverage

### Current Test Scenes

| Test File | Scene | Checkpoints | What It Tests |
|-----------|-------|-------------|---------------|
| `tape-csg-orbit.json` | Box − Sphere CSG | front, side, 3/4, top-down | Analytical ray marching core |
| `atlas-blob-orbit.json` | 12-sphere blob | front, side, 3/4, top-down | Atlas pipeline, baked evaluation |
| `multi-field-orbit.json` | 3 primitives | front, side, 3/4, top-down | Multi-tape composition, transforms |
| `stress-test-orbit.json` | 64 spheres | front, 3/4, top-down, wide | Atlas scalability under load |

### Adding New Tests

Create a JSON file in `workloads/penumbra/tests/`:

```json
{
  "name": "my-new-test",
  "workload": "penumbra",
  "description": "What this test verifies",
  "setup": {
    "scene": { "index": 4 },
    "backend": "webgpu",
    "canvas": { "width": 960, "height": 540 }
  },
  "checkpoints": [
    {
      "name": "default-view",
      "camera": { "azimuth": 45, "elevation": 30, "distance": 8 },
      "stabilizeMs": 500,
      "tolerance": 0.02
    }
  ]
}
```

Key parameters:
- **`scene.index`** — which Penumbra test scene to load (0-based)
- **`camera`** — spherical coordinates: azimuth (degrees, 0=front), elevation (degrees, 0=horizon), distance
- **`stabilizeMs`** — wait time after camera change before capturing (atlas scenes need 800-1500ms)
- **`tolerance`** — max fraction of differing pixels (0.02 = 2%)

---

## Troubleshooting

### "Vite dev server did not start"
- Check that `npm run dev` works manually in the Penumbra directory
- Check port 3000 isn't already in use: `netstat -ano | findstr :3000`
- Kill stale node processes: `taskkill /F /IM node.exe`

### "Chrome/Edge not found"
- Set `penumbraConfig.chromePath` in workload.json to the full path

### "Penumbra test harness did not initialize"
- Ensure `window.__canaryGetRendererInfo` is exposed in `test/main.ts`
- Check the browser console for JS errors (open Chrome manually to debug)

### "Screenshots don't match between runs"
- Verify `--force-device-scale-factor=1` is in chromeFlags
- Check that `window.__canaryPauseAnimation` is guarding the torus animation
- Atlas scenes may need longer `stabilizeMs` — try 1500-2000ms
- Temporal accumulation (Phase 6) can cause first-frame differences — increase stabilize time

### "Port already in use"
- Kill existing Vite: `taskkill /F /IM node.exe`
- Or change the port in workload.json: `"vitePort": 3001`

---

## Autonomous Mode Prompt

To run Canary visual regression tests as part of an autonomous build:

```
You are modifying Penumbra's rendering code. After making changes:

1. Build Penumbra: `npx tsc --noEmit` (check for type errors)
2. Run unit tests: `npx vitest run` (check for logic errors)
3. Run visual regression tests: `canary run --workload penumbra`
4. Read the report: `canary report --workload penumbra`
5. If any tests FAIL:
   - Open the HTML report and examine the diffs
   - If the visual change is INTENTIONAL (you changed the shader/lighting/etc.):
     → `canary approve --workload penumbra`
   - If the visual change is UNINTENTIONAL (regression):
     → Fix the issue and re-run from step 3
6. All tests PASS → change is safe to commit

Press Ctrl+C at any time to abort the test run.
```
