---
date: 2026-05-09
status: shipped
source: extracted from Canary/CLAUDE.md per STANDARD.md §3 + §19
---

# Penumbra bug 0036 stall observability + soak fixture (Phase A shipped 2026-05-09)


Penumbra is investigating bug 0036 ("display stalls indefinitely;
gumball still selects"). The 2026-05-09 session shipped Phase A
observability hooks on the WebGPU renderer:

- **3 new `__canary*` hooks** wired in `packages/studio/canary-hooks.ts`:
  - `__canaryGetRenderLoopState()` → `{ totalFrames,
    framesSinceMarkDirty, lastFrameMs, currentResolutionScale,
    gpuPressureEMA, dirtyReasonHistory[] }`. Frame-by-frame health
    snapshot — `framesSinceMarkDirty` climbing without
    `dirtyReasonHistory` growth points to hypothesis #2
    (eventDrivenRender stuck off).
  - `__canaryGetDeviceState()` → `{ lost, lostReason,
    errorScopeMessages[] }`. Ring buffer of last 16 popErrorScope
    captures across the renderer's 5 known scope sites
    (`atlas-pipeline-build{,-catch}`, `atlas-bind-group-1`,
    `particulate-{render,construct}`).
  - `__canaryForceMarkDirty(reason)` — manual probe; if calling
    revives a stalled canvas, hypothesis #2 is confirmed.
- All 3 hooks return null / no-op on WebGL2 (feature-frozen path).
- **1 new fixture** `workloads/penumbra/tests/bug-0036-display-stall-soak.json`:
  ~7-minute soak on the 64-Sphere Stress scene (index 8). Setup
  injects gentle camera drift via `setInterval` plus two scheduled
  rapid-input bursts at T+90s and T+240s (30 events/sec for 5s
  each). Pixel-diff at staggered checkpoints catches the "screen
  went black" failure mode. Side channel: `window.__bug0036Samples`
  accumulates hook state every 30s for human DevTools inspection.
- **Suite** `workloads/penumbra/suites/bug-0036-stall.json` runs the
  one fixture.

**Known gap**: Canary's TestDefinition has no asserts on canary-hook
return values (`asserts[]` is Grasshopper-panel-only). Detecting
the stall AUTOMATICALLY at a checkpoint requires extending
`TestAssert` with hook-state assertion types. That's deferred — for
now the user runs a manual DevTools session to read
`window.__bug0036Samples` after a stall.

Run:
```
canary run --workload penumbra --suite bug-0036-stall
```

See `Penumbra/docs/bugs/0036-display-stalls-gumball-alive.md` and
`Penumbra/docs/debug-sessions/2026-05-09-bug-0036-stall-investigation.md`
(anti-spiral instance file). Phase B (classify the actual cause
under instrumentation) is the next session's first task.
