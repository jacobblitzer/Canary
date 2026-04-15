# Penumbra — Canary Agent Notes

## Architecture: CDP Bridge Agent

Penumbra runs in a browser (Vite dev server + Chrome/Edge). The agent is a .NET bridge process
that controls Chrome via Chrome DevTools Protocol (CDP) over WebSocket.

```
canary.exe ←── Named Pipe ──→ PenumbraBridgeAgent ←── CDP WebSocket ──→ Chrome ──→ Penumbra
```

### Why CDP (Not SendInput or Electron)

| Approach | Problem |
|---|---|
| **SendInput** (Pigment approach) | Browser has its own input pipeline. OS-level mouse injection hits browser chrome/tabs/URL bar. Window sizing nightmare with DPI scaling and browser UI offsets. |
| **Electron wrapper** | Over-engineered. Adds a build step and runtime dependency for no benefit. |
| **CDP bridge** ✅ | Chrome already supports `--remote-debugging-port`. CDP gives: pixel-perfect screenshot capture, CSS-coordinate mouse input (no screen coords), `Runtime.evaluate` for direct JS API calls. |

### Key Advantage Over Pigment

With Pigment, mouse input required: `ViewportLocator` → `GetClientRect` → normalize → denormalize → `ScreenToAbsolute` → `SendInput` (65535-range). For Penumbra, **scripted camera positions via `Runtime.evaluate` bypass mouse input entirely** — perfectly deterministic, no coordinate mapping at all.

## Agent Method Mapping

| ICanaryAgent Method | CDP Implementation |
|---|---|
| `HeartbeatAsync()` | `Runtime.evaluate("window.__canaryGetRendererInfo()")` |
| `ExecuteAsync("LoadScene", {"index"})` | `Runtime.evaluate("window.__canarySetScene(N)")` |
| `ExecuteAsync("SetCamera", {"azimuth", "elevation", "distance"})` | `Runtime.evaluate("window.__canarySetCamera(az, el, dist)")` |
| `ExecuteAsync("SetCanvasSize", {"width", "height"})` | `Runtime.evaluate` → lock canvas + disable resize observer |
| `ExecuteAsync("WaitForStable", {"ms"})` | Poll `isAtlasBuildComplete()` + Task.Delay |
| `ExecuteAsync("SetBackend", {"backend"})` | Navigate to `localhost:3000?backend=webgpu\|webgl2` |
| `CaptureScreenshotAsync(settings)` | `Page.captureScreenshot` with clip rect to canvas bounds |
| `AbortAsync()` | Kill Chrome + Vite child processes |

## Two Input Modes

### Path A: Scripted Camera (Primary)
Tests specify camera angles as `{ azimuth, elevation, distance }` in checkpoint definitions.
Bridge agent calls `window.__canarySetCamera()` directly. No mouse input, no coordinate mapping.

### Path B: Recorded Mouse Input (Secondary)
Same `InputRecording` format as Pigment (normalized vx/vy 0-1, timestamped events).
Replayed via CDP `Input.dispatchMouseEvent` in CSS pixel coordinates instead of Win32 `SendInput`.
Used for testing interactive behavior (orbit drag, gumball manipulation, selection).

## Deterministic Rendering

| Requirement | Implementation |
|---|---|
| Fixed canvas size | `window.__canaryLockSize = true` disables resize observer |
| DPI independence | Chrome launched with `--force-device-scale-factor=1` |
| No animation jitter | `window.__canaryPauseAnimation = true` freezes torus orbit |
| Atlas convergence | Bridge agent polls `isAtlasBuildComplete()` before capture |
| Consistent window | Chrome launched with `--window-size=1024,768 --window-position=0,0` |

## Penumbra-Side Requirements

Minimal changes to `test/main.ts`:
1. Expose `window.__canarySetScene`, `__canarySetCamera`, `__canaryGetCamera`, `__canaryGetRendererInfo`
2. Add `__canaryLockSize` guard around canvas resize in render loop
3. Add `__canaryPauseAnimation` guard around torus orbit animation
4. Add `setSpherical()` / `getSpherical()` methods to orbit camera

See `penumbra-hooks/canary-hooks.ts` for the exact code.

## Dependencies

No new NuGet packages. CDP client uses built-in `System.Net.WebSockets.Client` and `System.Text.Json`.
