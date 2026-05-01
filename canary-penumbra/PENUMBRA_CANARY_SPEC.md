# Penumbra × Canary Integration Spec

## Overview

Integrate Penumbra's browser-based SDF test harness with Canary's visual regression testing pipeline. The goal: Claude Code makes a change to Penumbra's shader or runtime code, runs `canary run --workload penumbra`, and gets an HTML report showing before/after diffs across every scene and camera angle — automatically, no human in the loop.

**Press Ctrl+C at any time to abort.**

---

## Architecture Decision: CDP Bridge Agent

Penumbra is a TypeScript/WebGL2/WebGPU application running in a browser via Vite dev server (`localhost:3000`). Canary agents are .NET processes that implement `ICanaryAgent` over named pipes. The bridge is a .NET process (`Canary.Agent.Penumbra`) that:

1. Starts the Vite dev server as a child process
2. Launches Chrome/Edge with `--remote-debugging-port=9222`
3. Connects to the browser via Chrome DevTools Protocol (CDP) over WebSocket
4. Implements `ICanaryAgent` by translating Canary commands into CDP calls
5. Runs an `AgentServer` on a named pipe so the harness talks to it like any other agent

```
canary.exe (harness)
    │
    ├── Named Pipe IPC ──→  PenumbraBridgeAgent (.NET)
    │                            │
    │                            ├── Child Process: npm run dev (Vite, port 3000)
    │                            │
    │                            ├── Child Process: Chrome --remote-debugging-port=9222
    │                            │       └── navigated to http://localhost:3000
    │                            │
    │                            └── CDP WebSocket ──→ Chrome DevTools Protocol
    │                                    • Runtime.evaluate → Penumbra JS APIs
    │                                    • Page.captureScreenshot → PNG bytes
    │                                    • Input.dispatchMouseEvent → orbit/pan
    │                                    • Browser.setWindowBounds → deterministic sizing
    │
    └── Comparison Engine (existing)
            • Pixel diff + SSIM
            • Composite builder
            • HTML report generator
```

### Why CDP, Not Electron or SendInput

| Approach | Verdict | Reason |
|---|---|---|
| **SendInput (Pigment approach)** | ❌ Wrong tool | Browser has its own input pipeline; OS-level mouse injection hits browser chrome, tabs, URL bar — not just the canvas. Window sizing nightmare with DPI scaling, browser UI offsets, and no reliable `viewportClass` equivalent. |
| **Electron wrapper** | ❌ Over-engineered | Requires packaging Penumbra as an Electron app. Adds a build step, a runtime dependency, and an IPC layer we don't need. |
| **CDP bridge** | ✅ Best fit | Chrome/Edge already support `--remote-debugging-port`. CDP gives pixel-perfect screenshot capture, CSS-coordinate mouse input (bypasses screen position entirely), and `Runtime.evaluate` for calling JS APIs directly. No coordinate mapping, no window class discovery, no DPI conversion. |

---

## Lessons from Pigment: What We Keep, What We Skip

The Pigment/Rhino integration required extensive work to get deterministic mouse input:

| Pigment Requirement | Penumbra Equivalent | Notes |
|---|---|---|
| `WindowPositioner` — position target at (0,0), 2/3 screen | `Browser.setWindowBounds` via CDP | CDP controls Chrome window position directly |
| `ViewportLocator` — find window by title, get client area bounds | Not needed | CDP operates in CSS coordinates within the page, not screen coordinates |
| Normalized coordinates (vx/vy 0-1) | Keep for recordings | Same format, but denormalized to CSS pixels via CDP, not screen pixels via SendInput |
| `ScreenToAbsolute` (65535-normalized SendInput coords) | Not needed | CDP `Input.dispatchMouseEvent` uses CSS pixel coordinates directly |
| `MoveCursorToHome` before replay | Keep | CDP equivalent: dispatch a mouseMoved event to canvas center |
| Viewport size stored in recording metadata | Keep | Ensures recordings replay at correct aspect ratio |
| `SetForegroundWindow` before replay | `Target.activateTarget` via CDP | Ensures browser tab is focused |

### The Big Win: Programmatic Camera Control

For Penumbra, mouse-based orbit is ONE way to control the camera, but **not the best way for automated testing**. Penumbra's orbit camera exposes its state programmatically. Instead of recording and replaying mouse drag sequences (which are sensitive to timing, DPI, and window size), we can:

```javascript
// Via Runtime.evaluate — set exact camera angles
camera.setSpherical(azimuth, elevation, distance);
// or
camera.setPosition(x, y, z);
camera.setTarget(tx, ty, tz);
```

This is **perfectly deterministic** — same angles produce identical renders regardless of window size, DPI, mouse speed, or replay timing. No coordinate mapping needed at all.

**We support both modes:**

| Mode | Use Case | Input Method |
|---|---|---|
| **Scripted camera** (primary) | Automated CI, Claude Code regression tests | `Runtime.evaluate` calls to camera API |
| **Recorded mouse input** (secondary) | Interactive test recording, complex gestures | CDP `Input.dispatchMouseEvent` in CSS coordinates |

---

## Input Strategy: Two Paths

### Path A: Scripted Camera Positions (Primary)

Test definitions specify camera positions as spherical coordinates or look-at parameters. The bridge agent sets each position via `Runtime.evaluate`, waits for render stabilization, then captures.

```json
{
  "name": "csg-box-sphere-orbit",
  "workload": "penumbra",
  "description": "Box-minus-sphere CSG from 4 angles",
  "setup": {
    "scene": "tape-csg",
    "backend": "webgpu",
    "canvasWidth": 960,
    "canvasHeight": 540
  },
  "checkpoints": [
    {
      "name": "front",
      "camera": { "azimuth": 0, "elevation": 15, "distance": 8 },
      "stabilizeMs": 500,
      "tolerance": 0.02
    },
    {
      "name": "side",
      "camera": { "azimuth": 90, "elevation": 15, "distance": 8 },
      "stabilizeMs": 500,
      "tolerance": 0.02
    },
    {
      "name": "top-down",
      "camera": { "azimuth": 0, "elevation": 80, "distance": 10 },
      "stabilizeMs": 500,
      "tolerance": 0.02
    },
    {
      "name": "three-quarter",
      "camera": { "azimuth": 45, "elevation": 30, "distance": 8 },
      "stabilizeMs": 500,
      "tolerance": 0.02
    }
  ]
}
```

No recording file. No mouse input. No coordinate mapping. The camera goes exactly where you tell it.

### Path B: Recorded Mouse Input (Secondary)

For testing interactive behavior (drag to orbit, gumball manipulation, selection), we use CDP mouse events with the same normalized coordinate format as Pigment recordings.

```json
{
  "name": "interactive-orbit-drag",
  "workload": "penumbra",
  "description": "Record an orbit drag and verify the view changes",
  "setup": {
    "scene": "multi-field",
    "backend": "webgpu",
    "canvasWidth": 960,
    "canvasHeight": 540
  },
  "recording": "orbit-drag.input.json",
  "checkpoints": [
    {
      "name": "after_orbit",
      "atTimeMs": 2000,
      "tolerance": 0.03
    }
  ]
}
```

The recording file uses the same `InputEvent` format as Pigment (vx/vy normalized 0-1, timestamped). The bridge agent replays them via CDP `Input.dispatchMouseEvent` instead of Win32 `SendInput`:

```csharp
// Pigment: SendInput with MOUSEEVENTF_ABSOLUTE in 65535-range coords
// Penumbra: CDP dispatchMouseEvent in CSS pixel coords

// Denormalize from (vx, vy) to CSS canvas coordinates:
int cssX = (int)(vx * canvasWidth);
int cssY = (int)(vy * canvasHeight);

// Offset by canvas position within the page (measured once at setup):
int pageX = canvasOffsetX + cssX;
int pageY = canvasOffsetY + cssY;

await cdp.SendAsync("Input.dispatchMouseEvent", new {
    type = "mouseMoved",  // or "mousePressed", "mouseReleased"
    x = pageX,
    y = pageY,
    button = "left"
});
```

### Why This Solves the Window Sizing Problem

With Pigment, `vx=0.5` had to map through:
1. Viewport bounds discovery (P/Invoke `GetClientRect` + `ClientToScreen`)
2. Denormalize to screen pixels
3. Convert to 65535-normalized absolute coordinates for `SendInput`
4. Hope the window hasn't moved or resized since we measured

With Penumbra via CDP, `vx=0.5` maps through:
1. Multiply by canvas width → CSS pixel X within canvas
2. Add canvas offset within page → page coordinate
3. Send via `Input.dispatchMouseEvent`

No screen coordinates, no DPI conversion, no `GetClientRect`, no race conditions with window movement. The canvas size is set by us via CDP and never changes during a test.

---

## Deterministic Canvas Sizing

This is the Penumbra equivalent of Pigment's `WindowPositioner`. We need the canvas to be a fixed, known size for every test run so that:
1. Normalized coordinates denormalize to the same CSS pixels
2. Screenshots are the same dimensions for comparison
3. Renders are pixel-identical (different viewport sizes = different ray directions = different pixels)

### Approach: CDP Window + Viewport Control

```csharp
// Step 1: Set browser window to a deterministic size
// Account for browser chrome (title bar, URL bar, etc.)
await cdp.SendAsync("Browser.setWindowBounds", new {
    windowId,
    bounds = new { width = 1024, height = 768 }
});

// Step 2: Measure actual canvas dimensions after page load
var canvasInfo = await cdp.EvaluateAsync<CanvasInfo>(@"
    (() => {
        const canvas = document.querySelector('canvas');
        const rect = canvas.getBoundingClientRect();
        return {
            offsetX: rect.left,
            offsetY: rect.top,
            width: rect.width,
            height: rect.height,
            devicePixelRatio: window.devicePixelRatio
        };
    })()
");

// Step 3: If canvas size doesn't match expected, resize
// Penumbra's test harness sizes the canvas to fill the window.
// By controlling the window size, we control the canvas size.
```

### Fixed Canvas via Penumbra's Test Harness API

Better yet — Penumbra's renderer already has `renderer.resize(width, height)` and the canvas size is set in the render loop. We can inject a fixed size:

```javascript
// Via Runtime.evaluate — lock canvas to exact pixel dimensions
(() => {
    const canvas = document.querySelector('canvas');
    canvas.width = 960;
    canvas.height = 540;
    canvas.style.width = '960px';
    canvas.style.height = '540px';
    // Disable the resize observer so it doesn't fight us
    window.__canaryLockSize = true;
})()
```

And in Penumbra's render loop, respect the lock:

```typescript
// In test/main.ts render loop — already calls renderer.resize(canvas.width, canvas.height)
// We just need to not overwrite canvas.width/height when locked
if (!window.__canaryLockSize) {
    canvas.width = canvas.clientWidth * devicePixelRatio;
    canvas.height = canvas.clientHeight * devicePixelRatio;
}
```

This is a one-line change to Penumbra's test harness, not a Canary change.

---

## Agent Implementation: PenumbraBridgeAgent

### ICanaryAgent Method Mapping

| Method | CDP Implementation |
|---|---|
| `HeartbeatAsync()` | `Runtime.evaluate("({ ok: true, fps: ..., fieldCount: ..., backend: ... })")` |
| `ExecuteAsync("LoadScene", { "index": "2" })` | `Runtime.evaluate("loadScene(2)")` — calls existing test harness function |
| `ExecuteAsync("SetCamera", { "azimuth", "elevation", "distance" })` | `Runtime.evaluate("camera.setSpherical(az, el, dist)")` |
| `ExecuteAsync("SetCanvasSize", { "width", "height" })` | `Runtime.evaluate` to set canvas dimensions + disable resize observer |
| `ExecuteAsync("WaitForStable", { "ms": "500" })` | `Task.Delay` + `Runtime.evaluate` to check `renderer.isAtlasBuildComplete()` |
| `ExecuteAsync("SetBackend", { "backend": "webgpu" })` | Navigate to `localhost:3000?backend=webgpu` |
| `CaptureScreenshotAsync(settings)` | `Page.captureScreenshot` with viewport clip to canvas bounds |
| `AbortAsync()` | Kill Chrome + Vite child processes |

### Screenshot Capture: CDP vs readPixels

Two options for screenshot capture:

| Method | Pros | Cons |
|---|---|---|
| `Page.captureScreenshot` (CDP) | Captures composited output exactly as displayed; includes any HTML overlays | Requires clipping to canvas bounds; includes browser compositing |
| `Runtime.evaluate` → `canvas.toDataURL()` | Captures raw WebGL output; no browser compositing artifacts | Requires `preserveDrawingBuffer: true` or capture during render; base64 encoding overhead |
| `Runtime.evaluate` → Penumbra's `readPixels` | Uses existing debug logger infrastructure; proven to work | Synchronous GPU stall; requires coordinating with render loop |

**Decision: Use `Page.captureScreenshot` with clip** as primary, fall back to `canvas.toDataURL` if needed. CDP screenshot is simpler and captures what the user actually sees.

```csharp
public async Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings, CancellationToken ct)
{
    // Get canvas bounds within the page
    var canvasRect = await EvaluateAsync<CanvasRect>(@"
        (() => {
            const c = document.querySelector('canvas');
            const r = c.getBoundingClientRect();
            return { x: r.left, y: r.top, width: r.width, height: r.height };
        })()
    ");

    // Capture just the canvas region
    var result = await _cdp.SendAsync("Page.captureScreenshot", new {
        format = "png",
        clip = new {
            x = canvasRect.X,
            y = canvasRect.Y,
            width = canvasRect.Width,
            height = canvasRect.Height,
            scale = 1
        }
    });

    var pngBytes = Convert.FromBase64String(result["data"].GetString()!);
    Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath)!);
    await File.WriteAllBytesAsync(settings.OutputPath, pngBytes, ct);

    return new ScreenshotResult
    {
        FilePath = settings.OutputPath,
        Width = (int)canvasRect.Width,
        Height = (int)canvasRect.Height,
        CapturedAt = DateTime.UtcNow
    };
}
```

---

## CDP Client Implementation

Use raw WebSocket — no heavy Puppeteer/Playwright dependency. The CDP protocol is JSON-RPC over WebSocket, which we already know how to handle from Canary's named pipe protocol.

### Connection Flow

```csharp
// 1. Launch Chrome
var chrome = Process.Start(new ProcessStartInfo
{
    FileName = FindChromePath(),  // Check Edge, Chrome, Chromium
    Arguments = string.Join(" ",
        "--remote-debugging-port=9222",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-background-timer-throttling",
        "--disable-renderer-backgrounding",
        "--disable-backgrounding-occluded-windows",
        $"--window-size=1024,768",
        $"--window-position=0,0",
        $"--user-data-dir={tempProfileDir}",  // Clean profile, no extensions
        "about:blank"
    )
});

// 2. Wait for CDP endpoint
// GET http://localhost:9222/json/version → webSocketDebuggerUrl
var wsUrl = await PollForCdpEndpoint("http://localhost:9222", timeout: 10_000);

// 3. Connect WebSocket
var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri(wsUrl), ct);

// 4. Navigate to Penumbra
await SendCdpCommand("Page.navigate", new { url = "http://localhost:3000" });

// 5. Wait for page load
await WaitForCdpEvent("Page.loadEventFired", timeout: 30_000);
```

### Chrome Launch Flags — Critical for Determinism

```
--disable-gpu-compositing          Prevent compositor jitter between runs
--force-device-scale-factor=1      Lock DPI to 1x — no scaling surprises
--hide-scrollbars                  No scrollbar width variation
--disable-smooth-scrolling         Deterministic scroll behavior
--autoplay-policy=no-user-gesture-required   WebGPU may need this
--disable-features=TranslateUI     No translate bar stealing pixels
--disable-infobars                 No "Chrome is being controlled" bar
--disable-popup-blocking           In case Penumbra opens dialogs
```

`--force-device-scale-factor=1` is the **single most important flag** for screenshot consistency. Without it, a 4K display produces 2x screenshots that don't match baselines captured on a 1080p display.

---

## Vite Dev Server Management

### Startup

```csharp
private async Task<Process> StartViteAsync(string penumbraDir, CancellationToken ct)
{
    var vite = Process.Start(new ProcessStartInfo
    {
        FileName = "npm",
        Arguments = "run dev -- --port 3000 --strictPort",
        WorkingDirectory = penumbraDir,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    });

    // Wait for "Local: http://localhost:3000" in stdout
    var ready = new TaskCompletionSource<bool>();
    vite.OutputDataReceived += (_, e) =>
    {
        if (e.Data?.Contains("localhost:3000") == true)
            ready.TrySetResult(true);
    };
    vite.BeginOutputReadLine();

    var completed = await Task.WhenAny(ready.Task, Task.Delay(30_000, ct));
    if (completed != ready.Task)
        throw new TimeoutException("Vite dev server did not start within 30 seconds");

    return vite;
}
```

### Shutdown

```csharp
// On Ctrl+C or test completion:
// 1. Close Chrome gracefully
await _cdp.SendAsync("Browser.close");
// 2. Kill Vite (it's a child process, so ProcessManager.KillAll() handles it)
// 3. Clean up temp Chrome profile directory
```

---

## Workload Configuration

### workloads/penumbra/workload.json (Updated)

```json
{
  "name": "penumbra",
  "displayName": "Penumbra (Browser)",
  "appPath": "npm",
  "appArgs": "run dev -- --port 3000 --strictPort",
  "agentType": "penumbra-cdp",
  "pipeName": "canary-penumbra",
  "startupTimeoutMs": 30000,
  "windowTitle": "Penumbra",
  "viewportClass": "",
  "penumbraConfig": {
    "projectDir": "C:\\Repos\\Penumbra",
    "chromePath": "",
    "cdpPort": 9222,
    "defaultBackend": "webgpu",
    "defaultCanvasWidth": 960,
    "defaultCanvasHeight": 540,
    "chromeFlags": [
      "--force-device-scale-factor=1",
      "--disable-gpu-compositing",
      "--hide-scrollbars"
    ]
  }
}
```

### Penumbra-Specific Config Fields

| Field | Type | Description |
|---|---|---|
| `projectDir` | string | Absolute path to the Penumbra monorepo root |
| `chromePath` | string | Path to Chrome/Edge executable (empty = auto-detect) |
| `cdpPort` | int | Chrome DevTools Protocol port (default 9222) |
| `defaultBackend` | string | `"webgpu"` or `"webgl2"` — which Penumbra backend to test |
| `defaultCanvasWidth` | int | Canvas pixel width for captures |
| `defaultCanvasHeight` | int | Canvas pixel height for captures |
| `chromeFlags` | string[] | Additional Chrome launch flags |

---

## Test Definitions

### Scene Coverage

Penumbra has these test scenes (from `test/main.ts` and `test/scenes/`):

| Index | Scene | Key Features | Why Test It |
|---|---|---|---|
| 0 | Tape CSG (box - sphere) | Analytical ray marching | Core SDF rendering |
| 1 | Atlas Blob (12-sphere smooth union) | Baked atlas evaluation | Atlas pipeline correctness |
| 2 | Multi-Field (3 primitives + animation) | Multi-tape composition, field transforms | Transform system |
| 3 | 64-Sphere Stress Test | Performance, atlas scalability | Regression under load |
| 4+ | v4 scenes (SCENES_5_TO_10) | Atoms, groups, companions, debug overlays | v4 type system rendering |

### Camera Angle Coverage

Each scene should be tested from 4 standard angles plus 1 scene-specific angle:

```
Standard angles:
  front:          azimuth=0,   elevation=15,  distance=8
  side:           azimuth=90,  elevation=15,  distance=8
  three-quarter:  azimuth=45,  elevation=30,  distance=8
  top-down:       azimuth=0,   elevation=80,  distance=10

Scene-specific:
  close-up:       azimuth=30,  elevation=10,  distance=4   (for detail scenes)
  wide:           azimuth=45,  elevation=45,  distance=15  (for stress test)
```

### Example Full Test Suite

```
workloads/penumbra/tests/
├── tape-csg-orbit.json           # Scene 0, 4 angles
├── atlas-blob-orbit.json         # Scene 1, 4 angles
├── multi-field-orbit.json        # Scene 2, 4 angles (animation paused)
├── stress-test-orbit.json        # Scene 3, 4 angles
├── v4-atoms-orbit.json           # Scene 4+, 4 angles
├── backend-comparison.json       # Same scene, WebGPU vs WebGL2
└── interactive-orbit-drag.json   # Recorded mouse input test
```

---

## Penumbra-Side Changes (Minimal)

The integration requires small additions to Penumbra's test harness — no changes to the rendering engine itself.

### 1. Canvas Size Lock (test/main.ts)

```typescript
// Add near the top of main.ts, after canvas setup
declare global {
    interface Window {
        __canaryLockSize?: boolean;
        __canarySetScene?: (index: number) => Promise<void>;
        __canaryGetCamera?: () => { azimuth: number; elevation: number; distance: number };
        __canarySetCamera?: (azimuth: number, elevation: number, distance: number) => void;
        __canaryGetRendererInfo?: () => Record<string, unknown>;
    }
}
```

### 2. Expose Canary API on window (test/main.ts)

```typescript
// After renderer and camera are initialized:
window.__canarySetScene = async (index: number) => {
    await loadScene(index);
    // Wait for atlas build if applicable
    const fields = renderer.getFields();
    if (fields.some(f => f.evalMode === 'atlas')) {
        await waitForAtlasBuild();
    }
};

window.__canarySetCamera = (azimuth: number, elevation: number, distance: number) => {
    camera.setSpherical(azimuth, elevation, distance);
};

window.__canaryGetCamera = () => {
    return camera.getSpherical();  // { azimuth, elevation, distance }
};

window.__canaryGetRendererInfo = () => ({
    backend: renderer.getBackendName(),
    fieldCount: renderer.getFields().length,
    atlasBuildComplete: renderer.isAtlasBuildComplete(),
    fps: currentFps,
});

window.__canaryLockSize = false;  // Set to true by bridge agent
```

### 3. Respect Size Lock in Render Loop

```typescript
// In the frame() function, guard the canvas resize:
if (!(window as any).__canaryLockSize) {
    canvas.width = canvas.clientWidth * devicePixelRatio;
    canvas.height = canvas.clientHeight * devicePixelRatio;
}
```

### 4. Camera API: setSpherical (if not already present)

The orbit camera needs a `setSpherical(azimuth, elevation, distance)` method. If it currently only stores cartesian state, add:

```typescript
setSpherical(azimuthDeg: number, elevationDeg: number, distance: number): void {
    const az = azimuthDeg * Math.PI / 180;
    const el = elevationDeg * Math.PI / 180;
    this.position[0] = this.target[0] + distance * Math.cos(el) * Math.sin(az);
    this.position[1] = this.target[1] + distance * Math.sin(el);
    this.position[2] = this.target[2] + distance * Math.cos(el) * Math.cos(az);
    this.distance = distance;
    this.dirty = true;
}

getSpherical(): { azimuth: number; elevation: number; distance: number } {
    // Inverse of setSpherical
    const dx = this.position[0] - this.target[0];
    const dy = this.position[1] - this.target[1];
    const dz = this.position[2] - this.target[2];
    const dist = Math.sqrt(dx*dx + dy*dy + dz*dz);
    const el = Math.asin(dy / dist) * 180 / Math.PI;
    const az = Math.atan2(dx, dz) * 180 / Math.PI;
    return { azimuth: az, elevation: el, distance: dist };
}
```

---

## Implementation Phases

### Phase P0: CDP Client Library

**Goal:** A minimal CDP client that can launch Chrome, connect via WebSocket, send commands, receive responses/events.

**Checkpoints:**

- P0.1: `CdpClient.cs` — WebSocket-based JSON-RPC client for Chrome DevTools Protocol
  - `ConnectAsync(string wsUrl)` — connect to CDP WebSocket
  - `SendAsync(string method, object? params)` — send command, await response
  - `EvaluateAsync<T>(string expression)` — shorthand for `Runtime.evaluate` + JSON parse
  - `WaitForEventAsync(string eventName, TimeSpan timeout)` — await a CDP event
  - Sequential message IDs, response matching, timeout handling
  - All the same patterns as `HarnessClient` but over WebSocket instead of named pipe

- P0.2: `ChromeLauncher.cs` — find and launch Chrome/Edge
  - `FindChromePath()` — check Program Files for Edge, Chrome, Chromium (in that order)
  - `LaunchAsync(ChromeOptions options)` — start Chrome with remote debugging
  - Poll `http://localhost:{port}/json/version` for WebSocket URL
  - Return `(Process chrome, string webSocketUrl)`
  - Chrome flags: `--force-device-scale-factor=1`, `--window-size`, `--user-data-dir={temp}`, etc.

- P0.3: Round-trip test
  - Launch Chrome → connect CDP → `Runtime.evaluate("1 + 1")` → assert result is 2
  - Navigate to `about:blank` → `Page.captureScreenshot` → assert PNG bytes received
  - `Input.dispatchMouseEvent` → verify no error
  - Cleanup: close Chrome, delete temp profile

**Exit Criteria:** Can launch Chrome, execute JS, capture screenshots, inject mouse events via CDP.

### Phase P1: Vite Integration + Bridge Agent Shell

**Goal:** The bridge agent starts Vite, launches Chrome to Penumbra's test harness, and responds to heartbeats.

**Checkpoints:**

- P1.1: `ViteManager.cs` — start/stop the Vite dev server
  - `StartAsync(string projectDir, int port)` — runs `npm run dev`, watches stdout for ready
  - `StopAsync()` — kills the process
  - Timeout: 30 seconds for startup
  - Port conflict detection: if port 3000 is in use, log error with clear message

- P1.2: `PenumbraBridgeAgent.cs` — implements `ICanaryAgent`
  - Constructor takes `PenumbraConfig` (project dir, CDP port, canvas size, etc.)
  - `InitializeAsync()` — start Vite → launch Chrome → navigate to Penumbra → wait for page load
  - `HeartbeatAsync()` — `Runtime.evaluate` to call `window.__canaryGetRendererInfo()`
  - `AbortAsync()` — kill Chrome, kill Vite
  - Runs `AgentServer` on named pipe `canary-penumbra-{pid}`

- P1.3: Integration test
  - Start bridge agent → harness connects via named pipe → heartbeat returns ok=true with backend info
  - Verify Vite starts, Chrome navigates, Penumbra loads

**Exit Criteria:** Harness can connect to bridge agent, Penumbra loads in Chrome, heartbeat works.

### Phase P2: Canvas Control + Screenshot Capture

**Goal:** Lock canvas to a fixed size, capture pixel-perfect screenshots.

**Checkpoints:**

- P2.1: Canvas size locking
  - `ExecuteAsync("SetCanvasSize", { "width": "960", "height": "540" })` → inject size lock JS
  - Verify canvas dimensions via `Runtime.evaluate`
  - Verify size persists across scene changes

- P2.2: Screenshot capture
  - `CaptureScreenshotAsync` → `Page.captureScreenshot` with canvas clip rect
  - Verify PNG file is written with correct dimensions
  - Verify two captures of the same scene produce identical bytes (determinism)

- P2.3: DPI handling
  - Verify `--force-device-scale-factor=1` produces 1x screenshots
  - Verify canvas pixel dimensions match screenshot pixel dimensions

**Exit Criteria:** Screenshots are deterministic — same scene, same camera = identical PNG bytes.

### Phase P3: Scene Loading + Camera Control

**Goal:** Load any Penumbra test scene and position the camera programmatically.

**Checkpoints:**

- P3.1: Scene loading
  - `ExecuteAsync("LoadScene", { "index": "0" })` → calls `window.__canarySetScene(0)`
  - Wait for scene load complete (check `renderer.getFields().length > 0`)
  - Wait for atlas build if applicable

- P3.2: Camera positioning
  - `ExecuteAsync("SetCamera", { "azimuth": "45", "elevation": "30", "distance": "8" })` → calls `window.__canarySetCamera(45, 30, 8)`
  - Verify camera position via `window.__canaryGetCamera()`
  - Capture screenshot, verify it changes when camera moves

- P3.3: Stabilization
  - `ExecuteAsync("WaitForStable", { "ms": "500" })` → wait for render to stabilize
  - For atlas scenes: wait for `isAtlasBuildComplete()` before capturing
  - For all scenes: wait N frames after camera change before capturing

**Exit Criteria:** Can load any scene, set camera to exact angles, and capture stable screenshots.

### Phase P4: Mouse Input Replay via CDP

**Goal:** Replay recorded mouse input files through CDP for interactive tests.

**Checkpoints:**

- P4.1: CDP mouse input injection
  - `Input.dispatchMouseEvent` with types: `mouseMoved`, `mousePressed`, `mouseReleased`, `mouseWheel`
  - Coordinate mapping: normalized (vx, vy) → CSS pixels → page coordinates (add canvas offset)
  - Measure canvas offset within page once at test start, reuse throughout

- P4.2: Recording replay
  - Load an `InputRecording` from JSON (same format as Pigment recordings)
  - Replay events with original timing via CDP `Input.dispatchMouseEvent`
  - Support checkpoint pauses (same as `InputReplayer` but via CDP instead of SendInput)
  - Support `CancellationToken` for Ctrl+C abort

- P4.3: Recording comparison
  - Record a mouse orbit drag via CDP (or manually create one)
  - Replay it → capture screenshot → verify it looks like an orbited view
  - Replay same recording twice → verify screenshots match (determinism within tolerance)

**Exit Criteria:** Can replay recorded mouse input through the browser with deterministic results.

### Phase P5: Test Runner Integration + Claude Code Workflow

**Goal:** `canary run --workload penumbra` works end-to-end. Claude Code can run it.

**Checkpoints:**

- P5.1: TestRunner integration
  - Bridge agent handles the test lifecycle: setup → load scene → set camera per checkpoint → capture → compare
  - Existing `TestRunner.cs` orchestrates (it already supports Path B: no recording, just checkpoints)
  - Add support for `camera` field in checkpoint definitions (new to Penumbra)
  - Add support for `scene` and `backend` in setup section

- P5.2: Full test suite
  - Create test definitions for all Penumbra scenes × standard camera angles
  - First run: all tests show NEW (no baselines)
  - `canary approve --workload penumbra` promotes candidates to baselines
  - Second run: all tests PASS (0% diff)
  - Modify a shader → third run → regression detected → report shows diffs

- P5.3: Claude Code runner prompt
  - Create `workloads/penumbra/CLAUDE_CODE_RUNNER.md` with prompts for:
    - Running the full test suite
    - Interpreting the HTML report
    - Approving baselines after intentional changes
    - Adding new test scenes/angles

- P5.4: HTML report verification
  - Report shows scene name, camera angle, baseline vs candidate vs diff
  - Failed tests clearly highlighted
  - Report works with existing `HtmlReportGenerator`

**Exit Criteria:** Full pipeline works: `canary run --workload penumbra` → report with diffs. Claude Code can execute it autonomously.

---

## Project Structure

```
Canary/
├── src/
│   ├── Canary.Core/
│   │   ├── Cdp/                          # NEW — CDP client library
│   │   │   ├── CdpClient.cs             # WebSocket-based CDP protocol client
│   │   │   ├── CdpResponse.cs           # Response models
│   │   │   └── ChromeLauncher.cs         # Find + launch Chrome/Edge
│   │   └── ...existing...
│   ├── Canary.Agent.Penumbra/            # NEW — Bridge agent project
│   │   ├── Canary.Agent.Penumbra.csproj  # net8.0-windows, refs Canary.Core + Canary.Agent
│   │   ├── PenumbraBridgeAgent.cs        # ICanaryAgent implementation
│   │   ├── PenumbraConfig.cs             # Penumbra-specific config model
│   │   ├── ViteManager.cs                # Start/stop Vite dev server
│   │   ├── CdpInputReplayer.cs           # Mouse replay via CDP (parallel to InputReplayer)
│   │   └── Program.cs                    # Standalone bridge agent process entry point
│   └── ...existing...
├── workloads/
│   └── penumbra/
│       ├── workload.json                 # Updated with penumbraConfig
│       ├── AGENT_NOTES.md                # Updated with CDP approach
│       ├── CLAUDE_CODE_RUNNER.md          # NEW — prompts for Claude Code
│       └── tests/
│           ├── tape-csg-orbit.json
│           ├── atlas-blob-orbit.json
│           ├── multi-field-orbit.json
│           ├── stress-test-orbit.json
│           └── ...
└── ...existing...
```

---

## Dependency Additions

| Package | Purpose | Justification |
|---|---|---|
| `System.Net.WebSockets.Client` | Built-in .NET — CDP WebSocket | No external dependency |
| `System.Net.Http` | Built-in .NET — CDP endpoint discovery | No external dependency |

**No new NuGet packages required.** The CDP client uses only built-in .NET types (`ClientWebSocket`, `HttpClient`, `System.Text.Json`). This is intentional — same philosophy as using `System.IO.Pipes` for named pipes.

---

## Test Specifications

### Unit Tests (Category=Unit, headless)

```
CdpClient_SendCommand_ReceivesResponse
  Mock WebSocket, send Runtime.evaluate, verify response parsed

CdpClient_Timeout_ThrowsTimeoutException
  Mock WebSocket, don't respond, verify timeout

CdpClient_EvaluateAsync_ParsesJson
  Mock Runtime.evaluate response with JSON result

ChromeLauncher_FindChromePath_ReturnsPath
  Verify at least one browser found on Windows

PenumbraConfig_Parse_ValidJson_AllFields
  Parse workload.json with penumbraConfig section

CdpInputReplayer_NormalizeToPage_CorrectMapping
  vx=0.5, vy=0.5, canvas 960x540, offset (0, 60)
  Expected: pageX=480, pageY=330

CdpInputReplayer_DifferentCanvasSize_ScalesCorrectly
  Same vx/vy, different canvas → different page coordinates
```

### Integration Tests (Category=Integration, requires Chrome)

```
ChromeLauncher_Launch_ConnectsCdp
  Launch Chrome, connect CDP, evaluate 1+1, close

PenumbraBridgeAgent_Heartbeat_ReturnsOk
  Start Vite + Chrome, heartbeat, verify renderer info

PenumbraBridgeAgent_LoadScene_ChangesFields
  Load scene 0, verify field count > 0
  Load scene 1, verify field count changes

PenumbraBridgeAgent_SetCamera_ChangesScreenshot
  Set camera to front, capture → set to side, capture → verify different

PenumbraBridgeAgent_ScreenshotDeterminism
  Same scene + camera → capture twice → verify identical bytes

PenumbraBridgeAgent_CanvasLock_PersistsDimensions
  Lock to 960x540, load scene, verify canvas still 960x540
```

---

## Claude Code Automation Workflow

The end-to-end workflow for Claude Code:

```
1. Claude Code edits Penumbra shader/runtime code
2. Claude Code runs: canary run --workload penumbra
3. Bridge agent starts Vite (with the modified code)
4. Bridge agent launches Chrome, navigates to localhost:3000
5. For each test definition:
   a. Load scene N
   b. For each checkpoint:
      - Set camera to (azimuth, elevation, distance)
      - Wait for render to stabilize
      - Capture screenshot via CDP
      - Compare against baseline (pixel diff + SSIM)
6. Bridge agent shuts down Chrome + Vite
7. Harness generates HTML report
8. Claude Code reads report:
   - All PASS → change is safe
   - Any FAIL → inspect diffs, decide if intentional
   - If intentional → canary approve --workload penumbra
```

---

## Open Questions

1. **Should the bridge agent be a standalone process or part of `canary.exe`?** Standalone is cleaner (same pattern as `Canary.Agent.Rhino`), but adds a process launch step. Could also be an in-process agent selected by `agentType: "penumbra-cdp"`.

2. **WebGPU backend testing** — Chrome may need `--enable-unsafe-webgpu` or `--enable-features=Vulkan` depending on the version. Need to test on Jake's machine.

3. **Atlas build wait** — the 64-sphere scene takes 300-500ms to build its atlas. The stabilization wait must be long enough, or we should poll `isAtlasBuildComplete()` in a loop rather than using a fixed delay.

4. **Temporal accumulation** — if Penumbra's Phase 6 (viz optimizations) is active, the first frame after a camera change may be at reduced resolution. Need to wait for convergence before capturing.
