# ARCHITECTURE.md — Canary Technical Architecture

## System Overview

Canary is a two-process visual regression testing harness. The **Harness** (external orchestrator) drives tests by launching applications, replaying recorded user input, requesting screenshots from an in-app **Agent**, and comparing those screenshots against verified baselines.

```
┌──────────────────────────────────────────────────────────────────────┐
│                        HOST MACHINE                                   │
│                                                                       │
│  ┌─────────────────────────────────┐    ┌──────────────────────────┐ │
│  │       CANARY HARNESS            │    │   TARGET APPLICATION     │ │
│  │       (canary.exe)              │    │   (e.g., Rhino)          │ │
│  │                                 │    │                          │ │
│  │  ┌───────────────────────┐      │    │  ┌────────────────────┐  │ │
│  │  │  CLI / Test Runner    │      │    │  │  Agent Plugin      │  │ │
│  │  │  Orchestrates tests   │      │    │  │  Receives commands  │  │ │
│  │  └──────────┬────────────┘      │    │  │  Captures screens   │  │ │
│  │             │                   │    │  └──────────┬─────────┘  │ │
│  │  ┌──────────▼────────────┐      │    │             │            │ │
│  │  │  Harness Client       ├──────┼────┼─────────────┤            │ │
│  │  │  (Named Pipe Client)  │ IPC  │    │  Agent Server            │ │
│  │  └──────────┬────────────┘      │    │  (Named Pipe Server)     │ │
│  │             │                   │    │                          │ │
│  │  ┌──────────▼────────────┐      │    │  ┌────────────────────┐  │ │
│  │  │  Input Replayer       │      │    │  │  GPU / Viewport    │  │ │
│  │  │  SendInput → Window   ├──────┼────┼──►  (real rendering)  │  │ │
│  │  └──────────┬────────────┘      │    │  └────────────────────┘  │ │
│  │             │                   │    └──────────────────────────┘ │
│  │  ┌──────────▼────────────┐                                       │
│  │  │  Comparison Engine    │      ┌──────────────────────────┐     │
│  │  │  Pixel diff + SSIM    │      │  Artifact Store          │     │
│  │  └──────────┬────────────┘      │  baselines/  candidates/ │     │
│  │             │                   │  results/    reports/     │     │
│  │  ┌──────────▼────────────┐      └──────────────────────────┘     │
│  │  │  Report Generator     │                                       │
│  │  │  HTML + JUnit XML     │                                       │
│  │  └───────────────────────┘                                       │
│  └─────────────────────────────────┘                                 │
│                                                                       │
│  ┌─────────────────────────────────────────────────────────────────┐ │
│  │  WATCHDOG: heartbeat every 2s, kill after 3 misses (6s)         │ │
│  │  CTRL+C:   kills all child processes immediately                │ │
│  └─────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Core Components

### 1. ICanaryAgent — The Agent Interface

Every workload agent implements this interface. It's the contract between the harness and whatever application is being tested.

```csharp
public interface ICanaryAgent
{
    /// Execute a named action with parameters.
    /// Actions are app-specific: "OpenFile", "RunCommand", "SetViewport", etc.
    Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters);

    /// Capture a screenshot of the application's primary viewport.
    /// Returns the path to the saved PNG file.
    Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings);

    /// Heartbeat — returns ok:true if the agent is alive and responsive.
    /// May include optional state info (mesh count, viewport size, etc.)
    Task<HeartbeatResult> HeartbeatAsync();

    /// Gracefully abort any in-progress operation.
    Task AbortAsync();
}
```

```csharp
public class CaptureSettings
{
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
    public string OutputPath { get; set; }  // Where to save the PNG
}

public class ScreenshotResult
{
    public string FilePath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime CapturedAt { get; set; }
}

public class HeartbeatResult
{
    public bool Ok { get; set; }
    public Dictionary<string, string> State { get; set; }  // Optional app state
}

public class AgentResponse
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public Dictionary<string, string> Data { get; set; }
}
```

### 2. IPC Protocol — JSON-RPC over Named Pipes

Communication is newline-delimited JSON-RPC 2.0 over a named pipe.

**Pipe naming convention:** `canary-{workloadName}-{processId}`

Example: `canary-rhino-12345`

**Request:**
```json
{"jsonrpc":"2.0","id":1,"method":"CaptureScreenshot","params":{"width":800,"height":600,"outputPath":"C:\\temp\\cap.png"}}
```

**Response:**
```json
{"jsonrpc":"2.0","id":1,"result":{"filePath":"C:\\temp\\cap.png","width":800,"height":600}}
```

**Error:**
```json
{"jsonrpc":"2.0","id":1,"error":{"code":-1,"message":"Viewport not available"}}
```

**Rules:**
- One JSON object per line, terminated by `\n`
- Requests have `method` + `params`; responses have `result` or `error`
- IDs are sequential integers, matched between request and response
- All reads/writes are async with configurable timeout (default 10s)
- If the pipe disconnects, the harness treats it as an agent crash

### 3. Input Recording Format

Recordings are JSON files storing a sequence of timestamped input events with viewport-relative coordinates.

```json
{
  "metadata": {
    "workload": "pigment",
    "recordedAt": "2026-04-04T10:30:00Z",
    "viewportWidth": 800,
    "viewportHeight": 600,
    "windowTitle": "Rhinoceros 8 - test_sphere.3dm",
    "durationMs": 3200
  },
  "events": [
    {"t": 0,    "type": "mouse_move",  "vx": 0.50, "vy": 0.50},
    {"t": 15,   "type": "mouse_down",  "vx": 0.50, "vy": 0.50, "button": "left"},
    {"t": 30,   "type": "mouse_move",  "vx": 0.52, "vy": 0.48},
    {"t": 45,   "type": "mouse_move",  "vx": 0.55, "vy": 0.45},
    {"t": 60,   "type": "mouse_up",    "vx": 0.55, "vy": 0.45, "button": "left"},
    {"t": 200,  "type": "key_down",    "key": "ControlKey"},
    {"t": 210,  "type": "key_down",    "key": "Z"},
    {"t": 280,  "type": "key_up",      "key": "Z"},
    {"t": 290,  "type": "key_up",      "key": "ControlKey"}
  ]
}
```

**Coordinate system:**
- `vx`, `vy` are normalized to [0.0, 1.0] relative to the target viewport's client area
- (0, 0) = top-left of viewport, (1, 1) = bottom-right
- On replay, coordinates are denormalized using the current viewport bounds
- If the viewport size differs from recording, the proportional position is preserved

**Event types:**
- `mouse_move` — cursor position update
- `mouse_down` / `mouse_up` — button press/release (left, right, middle)
- `mouse_wheel` — scroll delta
- `key_down` / `key_up` — keyboard key press/release (uses .NET `Keys` enum names)

### 4. Test Definition Format

Each test is a JSON file describing what to do and what to verify.

```json
{
  "name": "sculpt-standard-undo",
  "workload": "pigment",
  "description": "Sculpt with standard brush, verify displacement, undo, verify restoration",
  "setup": {
    "file": "test_sphere.3dm",
    "viewport": {
      "width": 800,
      "height": 600,
      "projection": "Perspective",
      "displayMode": "Shaded"
    },
    "commands": [
      "SetView World Perspective",
      "Zoom All Extents"
    ]
  },
  "recording": "sculpt-standard-undo.input.json",
  "checkpoints": [
    {
      "name": "after_stroke",
      "atTimeMs": 1500,
      "tolerance": 0.02,
      "description": "Mesh should show displacement from standard brush"
    },
    {
      "name": "after_undo",
      "atTimeMs": 3000,
      "tolerance": 0.01,
      "description": "Mesh should match original after Ctrl+Z"
    }
  ]
}
```

**Checkpoint execution:** The replayer pauses at the specified timestamp, the harness sends `CaptureScreenshot` to the agent, saves the result, then resumes replay.

### 5. Comparison Engine

**Pixel Diff (primary):**
```csharp
public class PixelDiffComparer
{
    /// Compare two images pixel-by-pixel.
    /// Returns the fraction of pixels that differ beyond the color threshold.
    /// colorThreshold: max per-channel difference to consider "same" (0-255, default 3)
    public ComparisonResult Compare(
        Image<Rgba32> baseline,
        Image<Rgba32> candidate,
        int colorThreshold = 3);
}

public class ComparisonResult
{
    public double DiffPercentage { get; set; }  // 0.0 to 1.0
    public bool Passed { get; set; }            // diffPercentage <= tolerance
    public Image<Rgba32> DiffImage { get; set; } // Changed pixels in magenta
    public int TotalPixels { get; set; }
    public int ChangedPixels { get; set; }
}
```

The `colorThreshold` handles antialiasing noise. A pixel is "different" only if ANY channel differs by more than the threshold. This prevents 1-bit GPU rasterization jitter from causing false failures.

**SSIM (secondary):**
```csharp
public class SsimComparer
{
    /// Compute structural similarity index between two images.
    /// Returns a score from 0.0 (completely different) to 1.0 (identical).
    public double ComputeSsim(Image<Rgba32> baseline, Image<Rgba32> candidate);
}
```

SSIM is used as a secondary sanity check and logged in the report, but does not independently pass/fail tests. The pixel diff is the gate.

### 6. Composite Image Builder

After comparison, the `CompositeBuilder` creates a single review image per test:

```
┌─────────────────────────────────────────────┐
│  Checkpoint: after_stroke                    │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐    │
│  │ Baseline  │ │ Candidate│ │   Diff   │    │
│  │          │ │          │ │  (mag.)  │    │
│  └──────────┘ └──────────┘ └──────────┘    │
│  Status: PASS (0.3% diff, tolerance 2%)     │
├─────────────────────────────────────────────┤
│  Checkpoint: after_undo                      │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐    │
│  │ Baseline  │ │ Candidate│ │   Diff   │    │
│  │          │ │          │ │          │    │
│  └──────────┘ └──────────┘ └──────────┘    │
│  Status: PASS (0.0% diff, tolerance 1%)     │
└─────────────────────────────────────────────┘
```

Each strip is: `baseline | candidate | diff` at original resolution, with a label bar above showing checkpoint name and result. Strips are stacked vertically. Saved as PNG.

### 7. Lifecycle Management

**App Launch Sequence:**
1. Harness starts the target application process (`Process.Start`)
2. Harness creates a `NamedPipeClientStream` targeting `canary-{workload}-{pid}`
3. Harness retries connection every 500ms for up to 30 seconds
4. When connected, harness sends `Heartbeat` to verify the agent is ready
5. Harness sends setup commands (open file, set viewport, etc.)
6. Harness begins input replay + checkpoint capture

**Ctrl+C Shutdown Sequence:**
1. `Console.CancelKeyPress` fires
2. Handler sets a `CancellationToken` that all async operations observe
3. `InputReplayer.Stop()` is called — no more SendInput events
4. `ProcessManager.KillAll()` terminates all child processes
5. Harness writes partial results (if any) and exits with code 2

**Watchdog:**
- Runs on a background `Task` during test execution
- Sends `Heartbeat` every 2 seconds
- If 3 consecutive heartbeats fail (no response within 2 seconds each = 6 seconds total), declares the app dead
- On death: logs crash, kills the process, marks test as "CRASHED", moves to next test

---

## Workload Configuration

Each workload has a `workload.json` that describes how to launch and connect to the target app:

```json
{
  "name": "pigment",
  "displayName": "Pigment (Rhino 8)",
  "appPath": "C:\\Program Files\\Rhino 8\\System\\Rhino.exe",
  "appArgs": "/nosplash",
  "agentType": "rhino",
  "pipeName": "canary-rhino",
  "startupTimeoutMs": 30000,
  "windowTitle": "Rhinoceros 8",
  "viewportClass": "Afx:00400000:8:00010011:00000000:00000000"
}
```

The `viewportClass` is the Win32 window class name for the Rhino viewport — used by `ViewportLocator` to find the correct child window for coordinate mapping. This is discovered once using Spy++ and stored in the config.

---

## Performance Strategy

- **IPC latency**: Named pipes on the same machine add ~0.1ms per round trip — negligible
- **Screenshot capture**: `CaptureToBitmap` takes 50-200ms depending on viewport complexity — acceptable
- **Pixel diff**: O(width × height) — 800×600 = 480K pixels, ~50ms with ImageSharp
- **SSIM**: More expensive (~200-500ms) — only computed after pixel diff, not blocking
- **Input replay**: Events injected at recorded timestamps — CPU cost is negligible
- **Total per-checkpoint overhead**: ~300ms (capture + compare + save)
- **Typical test with 3 checkpoints**: ~1 second of overhead beyond the replay time

---

## Error Handling Strategy

- IPC timeout: log warning, retry once, then mark checkpoint as FAILED with "Agent unresponsive"
- App crash during test: watchdog detects, marks test as CRASHED, harness moves to next test
- Screenshot is blank/black: log as FAILED with "Blank screenshot — possible GPU issue"
- Baseline missing: mark test as NEW, save candidate, prompt user to run `canary approve`
- Baseline dimensions differ from candidate: FAIL with "Viewport size mismatch" — do not compare
- Recording file missing: FAIL with "Recording not found"
- All errors are non-fatal to the harness — it always continues to the next test unless Ctrl+C
