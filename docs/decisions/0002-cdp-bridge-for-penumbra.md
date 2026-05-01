---
title: "Use CDP bridge agent for Penumbra integration"
date: 2026-04-14
tags:
  - decision
  - architecture
  - penumbra
status: accepted
project: canary
---

# Use CDP bridge agent for Penumbra integration

## Context and Problem Statement
Penumbra is a TypeScript/WebGPU SDF renderer running in a browser via Vite dev server. Canary agents are .NET processes that implement `ICanaryAgent` over named pipes. We need to bridge between the two.

## Considered Options
1. **SendInput (Pigment approach)** -- OS-level mouse injection into browser window
2. **Electron wrapper** -- package Penumbra as an Electron app with built-in agent
3. **CDP bridge** -- .NET process connects to Chrome DevTools Protocol via WebSocket

## Decision Outcome
Chosen option: **CDP bridge** because:
- Chrome/Edge already support `--remote-debugging-port`
- CDP provides pixel-perfect screenshots via `Page.captureScreenshot`
- CSS-coordinate mouse input bypasses screen position mapping entirely
- `Runtime.evaluate` enables programmatic camera control (perfectly deterministic)
- No coordinate mapping, no DPI conversion, no window class discovery
- No new build step or runtime dependency (vs Electron)
- Browser input pipeline is correct target (vs SendInput hitting browser chrome)

### Key Advantage: Programmatic Camera Control
Instead of recording/replaying mouse drags (sensitive to timing, DPI, window size), we call `camera.setSpherical(azimuth, elevation, distance)` via `Runtime.evaluate`. Same angles = identical renders regardless of environment.

### Two Input Modes
- **Path A (primary)**: Scripted camera positions via `Runtime.evaluate`
- **Path B (secondary)**: Recorded mouse input via CDP `Input.dispatchMouseEvent`

### Consequences
- Good: Deterministic rendering -- same angles produce identical screenshots
- Good: No external NuGet packages -- uses built-in `ClientWebSocket` and `HttpClient`
- Good: Same IPC pattern as other agents (named pipe to harness)
- Bad: Requires Chrome/Edge installed on test machine
- Bad: CDP protocol is large; we only use a small subset

## Related
- Full spec: `canary-penumbra/PENUMBRA_CANARY_SPEC.md`
- Agent implementation: `src/Canary.Agent.Penumbra/`
- Phase: Penumbra integration (post-Phase 12)
