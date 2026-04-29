# PHASES.md — Canary Build Phases

## Phase 0: Solution Scaffold + CLI Shell
**Goal:** A .NET solution that builds, a CLI that parses commands, and Ctrl+C works.

### Checkpoint 0.1: Solution Structure
- Create `Canary.sln`
- Create `src/Canary.Harness/Canary.Harness.csproj` — console app, `net8.0`
- Create `src/Canary.Agent/Canary.Agent.csproj` — class library, multi-target `net8.0;net48`
- Create `tests/Canary.Tests/Canary.Tests.csproj` — xUnit test project, `net8.0`
- Create folder structure per `SUPERVISOR.md`
- Create `.gitignore` (bin, obj, results/, *.user)
- Verify: `dotnet build Canary.sln` succeeds

### Checkpoint 0.2: CLI Entry Point
- Install `System.CommandLine` NuGet in Canary.Harness
- Implement `Program.cs` with root command and subcommands: `run`, `record`, `approve`, `report`
- Each subcommand prints placeholder text (e.g., "run: not yet implemented")
- `canary --help` prints usage
- `canary run --help` prints run-specific options
- Register `Console.CancelKeyPress` handler that prints `"\n[Canary] Aborted by user."` and exits

### Checkpoint 0.3: Test Foundation
- Create `Canary.Tests` xUnit project
- Add a trivial test: `Program_Help_PrintsUsage` (runs the CLI and checks output contains "canary")
- Add a test: `Program_CtrlC_RegistersHandler` (verify CancellationTokenSource is created)
- Verify: `dotnet test --filter "Category=Unit"` passes
- Create `README.md`

**Phase 0 Exit Criteria:** Solution builds, CLI runs, Ctrl+C handler registered, tests pass.

---

## Phase 1: Named Pipe IPC + Agent Protocol
**Goal:** Two processes can communicate over a named pipe using JSON-RPC.

### Checkpoint 1.1: RPC Message Types
- Implement `RpcMessage.cs` — request/response/error models matching JSON-RPC 2.0
- Implement `RpcMethods.cs` — string constants: `Heartbeat`, `Execute`, `CaptureScreenshot`, `Abort`
- Unit tests: serialize request to JSON, deserialize back, verify all fields preserved
- Unit tests: serialize error response, verify error code and message round-trip

### Checkpoint 1.2: Agent Interface
- Implement `ICanaryAgent.cs` — async methods: `ExecuteAsync`, `CaptureScreenshotAsync`, `HeartbeatAsync`, `AbortAsync`
- Implement `CaptureSettings.cs`, `ScreenshotResult.cs`, `HeartbeatResult.cs`, `AgentResponse.cs`
- These are data contracts only — no implementation yet

### Checkpoint 1.3: Pipe Server (Agent Side)
- Implement `AgentServer.cs`
- Creates `NamedPipeServerStream` with name `canary-{name}-{pid}`
- Listens for connections asynchronously
- Reads JSON-RPC requests line-by-line
- Dispatches to an `ICanaryAgent` implementation
- Sends JSON-RPC responses back
- Supports graceful shutdown via CancellationToken

### Checkpoint 1.4: Pipe Client (Harness Side)
- Implement `HarnessClient.cs`
- Connects to named pipe by name
- Sends JSON-RPC requests, awaits responses with configurable timeout
- Throws `TimeoutException` if response not received within timeout
- Reconnection logic: if pipe disconnects, attempt one reconnect before failing

### Checkpoint 1.5: IPC Round-Trip Test
- Create a `MockAgent` implementing `ICanaryAgent` that returns canned responses
- In-process test: start `AgentServer` with `MockAgent`, connect `HarnessClient`, send `Heartbeat`, verify response
- Timeout test: `MockAgent` delays response beyond timeout, verify `TimeoutException`
- Concurrent test: send 5 requests in sequence, verify all responses match

**Phase 1 Exit Criteria:** Harness and agent can exchange JSON-RPC messages over named pipes with timeout handling.

---

## Phase 2: Input Recorder + Replayer
**Goal:** Record mouse/keyboard events relative to a window, replay them via SendInput.

### Checkpoint 2.1: Input Event Model
- Implement `InputEvent.cs` — enum `InputEventType` (MouseMove, MouseDown, MouseUp, MouseWheel, KeyDown, KeyUp)
- Fields: `TimestampMs`, `Type`, `ViewportX`, `ViewportY` (normalized 0-1), `Button`, `Key`, `WheelDelta`
- Implement `InputRecording.cs` — metadata (workload, viewport size, duration) + list of events
- Unit test: serialize to JSON, deserialize back, all fields preserved
- Unit test: verify coordinate normalization math (pixel → normalized → pixel round-trip within ±1px)

### Checkpoint 2.2: Viewport Locator
- Implement `ViewportLocator.cs`
- `FindWindow(string titleSubstring)` — finds window by title using `FindWindow` / `EnumWindows` P/Invoke
- `GetViewportBounds(IntPtr hwnd)` — returns screen-space rectangle of the window's client area
- `NormalizeCoord(Point screenPoint, Rectangle viewportBounds) → (double vx, double vy)`
- `DenormalizeCoord(double vx, double vy, Rectangle viewportBounds) → Point screenPoint`
- Unit test: normalize then denormalize returns original point (±1px)
- Unit test: FindWindow with known title (e.g., "Notepad") finds the window (integration-ish, but useful)

### Checkpoint 2.3: Input Recorder
- Implement `InputRecorder.cs`
- Uses `SetWindowsHookEx` with `WH_MOUSE_LL` and `WH_KEYBOARD_LL` on a dedicated STA thread
- Filters events: only records events when cursor is within the target window's bounds
- Normalizes mouse coordinates to viewport-relative (0-1)
- Timestamps relative to recording start
- `StartRecording(IntPtr targetWindow)` / `StopRecording() → InputRecording`
- Saves recording to JSON file

### Checkpoint 2.4: Input Replayer
- Implement `InputReplayer.cs`
- Reads `InputRecording` from JSON
- For each event: wait until correct timestamp, denormalize coordinates, inject via `SendInput`
- `SendInput` mouse uses `MOUSEEVENTF_ABSOLUTE` with 65535-normalized screen coordinates
- Supports `CancellationToken` — stops immediately when cancelled
- Supports `SpeedMultiplier` (1.0 = original speed, 2.0 = double speed)
- Supports checkpoint pauses: at specified timestamps, calls a callback and waits before continuing
- Unit test: replay timing accuracy — events fire within ±5ms of target time (on 100ms+ intervals)

**Phase 2 Exit Criteria:** Can record mouse/keyboard input relative to a window, save to JSON, and replay it.

---

## Phase 3: Screenshot Comparison Engine
**Goal:** Compare two images, produce diff visualization, build composites.

### Checkpoint 3.1: Pixel Diff Comparer
- Implement `PixelDiffComparer.cs` using SixLabors.ImageSharp
- Load two `Image<Rgba32>`, iterate pixels, compare per-channel
- `colorThreshold` (default 3): a pixel is "different" only if any channel differs by more than this
- Returns `ComparisonResult` with: `DiffPercentage`, `ChangedPixels`, `TotalPixels`, diff `Image`
- Diff image: unchanged pixels rendered semi-transparent, changed pixels rendered in magenta
- Unit test: identical images → 0% diff
- Unit test: one pixel changed → correct diff count
- Unit test: image with 10% random noise at threshold 0 → ~10% diff
- Unit test: mismatched dimensions → throws `ArgumentException`

### Checkpoint 3.2: SSIM Comparer
- Implement `SsimComparer.cs`
- Convert images to grayscale (luminance: 0.299R + 0.587G + 0.114B)
- Sliding 8×8 window SSIM with standard constants (C1, C2)
- Returns a single float [0, 1]
- Unit test: identical images → SSIM = 1.0
- Unit test: random noise image vs original → SSIM < 0.5
- Unit test: slightly shifted image → SSIM > 0.9 but < 1.0

### Checkpoint 3.3: Composite Builder
- Implement `CompositeBuilder.cs`
- Takes a list of `CheckpointComparison` objects (baseline image, candidate image, diff image, metadata)
- For each: stitches `[baseline | candidate | diff]` horizontally with 2px gap
- Adds a label bar above each strip: checkpoint name, pass/fail, diff percentage
- Stacks strips vertically with 4px gap between
- Saves final composite as PNG
- Unit test: composite of 3 checkpoints produces an image of expected dimensions
- Unit test: composite with 0 checkpoints produces empty/no-op result

### Checkpoint 3.4: Create Test Data
- Create `TestData/` folder with known test images:
  - `baseline_red_square.png` — 100×100 red square on white
  - `candidate_red_square.png` — identical (for 0% diff test)
  - `candidate_shifted.png` — red square shifted 5px right (for partial diff test)
  - `candidate_wrong_color.png` — blue square on white (for high diff test)
- These are generated programmatically in a test fixture setup, not committed as binaries

**Phase 3 Exit Criteria:** Can compare images, generate diffs, build composite review images.

---

## Phase 4: Test Runner + Orchestrator
**Goal:** End-to-end: parse test definition → launch app → run test → compare → report results.

### Checkpoint 4.1: Test Definition Parser
- Implement `TestDefinition.cs` — deserialization from JSON
- Fields: `Name`, `Workload`, `Setup` (file, viewport, commands), `Recording` (filename), `Checkpoints[]` (name, atTimeMs, tolerance)
- Implement `WorkloadConfig.cs` — deserialization of `workload.json`
- Unit test: parse sample test JSON, verify all fields populated
- Unit test: missing required field → clear error message

### Checkpoint 4.2: App Launcher + Process Manager
- Implement `AppLauncher.cs`
- `LaunchAsync(WorkloadConfig config) → Process` — starts the app, returns the process
- `WaitForAgentAsync(string pipeName, TimeSpan timeout)` — polls for named pipe availability
- Implement `ProcessManager.cs` — tracks all launched processes, `KillAll()` on shutdown
- Unit test: launch a simple console app (create a dummy test app), verify it starts and can be killed
- Unit test: KillAll terminates all tracked processes

### Checkpoint 4.3: Watchdog
- Implement `Watchdog.cs`
- Runs on background Task
- Sends `Heartbeat` via `HarnessClient` every 2 seconds
- If 3 consecutive heartbeats fail: fires `OnAppDead` event
- Supports `CancellationToken` for shutdown
- Unit test: mock client that returns heartbeats → watchdog stays quiet
- Unit test: mock client that stops responding → `OnAppDead` fires after ~6 seconds

### Checkpoint 4.4: Test Runner Core
- Implement `TestRunner.cs`
- Orchestrates: load test def → launch app → wait for agent → send setup commands → replay input (pausing at checkpoints) → capture screenshots → compare against baselines → collect results
- Returns `TestResult` (pass/fail/crash per checkpoint + composite image path)
- Handles: app crash (via watchdog), timeout, missing baseline (marks as NEW), comparison failure

### Checkpoint 4.5: CLI Integration
- Wire `RunCommand.cs` to `TestRunner`
- `canary run --workload pigment` discovers `workloads/pigment/tests/*.json`, runs all
- `canary run --test sculpt-undo-test` runs a single test
- `canary approve --test sculpt-undo-test` copies candidates to baselines
- Console output: `[HH:MM:SS] ✓ test-name (0.3% diff) — PASS   Press Ctrl+C to abort`
- Console output on fail: `[HH:MM:SS] ✗ test-name (5.2% diff, tol 2%) — FAIL`
- Summary: `Results: 3 passed, 1 failed, 0 crashed`

**Phase 4 Exit Criteria:** Full test runner works with mock agent. `canary run` and `canary approve` functional.

---

## Phase 5: HTML Report + Polish
**Goal:** Professional report, clean console output, robust edge case handling.

### Checkpoint 5.1: HTML Report Generator
- Implement `HtmlReportGenerator.cs`
- Single self-contained HTML file (images as inline base64)
- Header: run timestamp, workload name, total pass/fail/crash counts
- Per test: name, status badge (green/red/gray), composite image, diff %, SSIM score
- Failed tests shown first, then passed
- `canary report` opens report in default browser via `Process.Start`

### Checkpoint 5.2: JUnit Report
- Implement `JUnitReportGenerator.cs`
- Standard JUnit XML format for CI integration
- Each test = test case, each checkpoint = assertion
- Failure includes diff percentage and image path

### Checkpoint 5.3: Console UI Polish
- Progress bar or spinner during long operations (app launch, replay)
- Color-coded output: green for pass, red for fail, yellow for new/no-baseline
- Always show `Press Ctrl+C to abort` at the bottom of any multi-test run output
- `--verbose` flag for detailed per-checkpoint output
- `--quiet` flag for CI (only summary + exit code)

### Checkpoint 5.4: Edge Cases
- Test with no checkpoints: runs replay but produces no comparison (just verifies no crash)
- Test with recording but no setup: skips setup phase
- Multiple tests sharing the same app instance: reuse process between tests if same workload
- Corrupt recording JSON: clear error message, skip test, continue suite

**Phase 5 Exit Criteria:** Report generation works, console output is clean, edge cases handled.

---

## Phase 6: Rhino Workload Agent
**Goal:** A real Rhino plugin that implements the Canary agent interface.

### Checkpoint 6.1: Rhino Plugin Shell
- Create `src/Canary.Agent.Rhino/Canary.Agent.Rhino.csproj` — targets `net48` (Rhino 8 plugin)
- Add RhinoCommon NuGet reference
- Create `CanaryRhinoPlugin.cs` extending `Rhino.PlugIns.PlugIn`
- Generate unique GUID
- On plugin load: start `AgentServer` on a background thread
- Verify: plugin builds, loads in Rhino (manually)

### Checkpoint 6.2: Rhino Agent Implementation
- Implement `RhinoAgent.cs : ICanaryAgent`
- `ExecuteAsync("OpenFile", {"path": "..."})` → `RhinoDoc.Open(path)`
- `ExecuteAsync("RunCommand", {"command": "..."})` → `RhinoApp.RunScript(command)`
- `ExecuteAsync("SetViewport", {"width","height","projection","displayMode"})` → configure active viewport
- `ExecuteAsync("SetView", {"name": "Perspective"})` → set named view
- `HeartbeatAsync()` → return `{ok: true}`

### Checkpoint 6.3: Rhino Screenshot Capture
- Implement `RhinoScreenCapture.cs`
- Uses `Rhino.Display.ViewCapture.CaptureToBitmap(ViewCaptureSettings)` or `RhinoView.CaptureToBitmap(Size)`
- Saves to specified output path as PNG
- Handles: no active viewport (error), viewport too small (warning)
- Locks viewport size to requested dimensions before capture

### Checkpoint 6.4: End-to-End Smoke Test
- Create a minimal test definition: open a .3dm file, capture one screenshot
- Run `canary run --test smoke-test` with Rhino as the target
- Verify: Rhino launches, agent connects, file opens, screenshot captured, comparison runs (first run = no baseline, "NEW" status)
- Run `canary approve --test smoke-test` to establish baseline
- Run again: verify PASS with 0% diff

**Phase 6 Exit Criteria:** Canary can launch Rhino, communicate with the agent, capture screenshots, and compare them.

---

## Phase 7: Future Workloads (Stub)
**Goal:** Document how to add new workload agents for Qualia, Penumbra, and other applications.

### Checkpoint 7.1: Agent Template
- Create a `docs/creating-a-workload.md` guide
- Template covers: implementing `ICanaryAgent`, starting `AgentServer`, creating `workload.json`
- Example: minimal WPF app agent (capture via `RenderTargetBitmap`)

### Checkpoint 7.2: Qualia Stub
- Create `workloads/qualia/workload.json` with placeholder configuration
- Document what `ICanaryAgent` methods map to in Qualia's architecture

### Checkpoint 7.3: Penumbra Stub
- Same as Qualia — placeholder config + documentation

**Phase 7 Exit Criteria:** Documentation exists for adding new workloads. Stubs created for known future workloads.

---

## Phase 8: VLM Oracle
**Goal:** Add a second comparison mode where a Vision-Language Model evaluates screenshots against natural-language descriptions, returning pass/fail verdicts without requiring baseline images.

### Checkpoint 8.1: Configuration & Types
- Add `mode` field to `TestCheckpoint` (values: `"pixel-diff"`, `"vlm"`)
- Create `VlmConfig` DTO (provider, model, maxTokens)
- Add `vlm` field to `TestSetup`
- Existing `Description` field on `TestCheckpoint` serves as the VLM prompt

### Checkpoint 8.2: VLM Provider Abstraction
- `IVlmProvider` interface with `EvaluateAsync(imageBytes, description, ct) → VlmVerdict`
- `VlmVerdict`: pass/fail, confidence (0–1), reasoning string
- `ClaudeVlmProvider`: calls Anthropic Messages API via `HttpClient`, sends screenshot as base64 image
- System prompt instructs model to return structured JSON verdict
- `VlmEvaluator` factory: resolves API key from `CANARY_VLM_API_KEY` / `ANTHROPIC_API_KEY`, instantiates provider

### Checkpoint 8.3: TestRunner Integration
- Lazy-init VLM provider when first VLM checkpoint is encountered
- Branch `ProcessCheckpointAsync` and `ProcessAgentCheckpointAsync` on `checkpoint.Mode`
- VLM checkpoints skip baseline lookup — always produce a verdict directly
- Add `VlmReasoning`, `VlmConfidence`, `VlmDescription` to `CheckpointResult`

### Checkpoint 8.4: Reporting
- HTML report: adaptive table columns for mixed-mode tests
- VLM detail sections show description, reasoning, confidence, and screenshot
- Console verbose output shows VLM confidence and reasoning

### Checkpoint 8.5: Tests
- Unit tests: Mode default value, JSON round-trip, VlmVerdict parsing, API response parsing
- Full test definition deserialization with VLM config
- Mixed-mode test (pixel-diff + VLM) deserialization

**Phase 8 Exit Criteria:** VLM checkpoints produce verdicts from Claude, reports render VLM reasoning, existing pixel-diff tests unchanged.

### Checkpoint 8.6: Mode Duality at the Harness Level
- Add `--mode <pixel-diff|vlm|both>` flag to `canary run` (default `pixel-diff`).
- Add `ModeOverride` enum + `TestRunner.ModeOverride` property; `RunCommand` propagates the flag.
- Add optional `setup.vlmDescription` field on `TestDefinition.Setup`; `ProcessVlmCheckpointAsync` falls back to it when a checkpoint has no `description`.
- Refactor `ProcessCheckpointAsync` + `ProcessAgentCheckpointAsync` to take an optional `forceMode: CheckpointMode?` parameter; centralize the loop in `DispatchClientCheckpointAsync` / `DispatchAgentCheckpointAsync`.
- Mode resolution rule: per-checkpoint `mode == "vlm"` always wins; otherwise `--mode` flag applies; otherwise pixel-diff. `--mode both` runs each checkpoint twice and emits two `CheckpointResult` rows (the VLM one suffixed with `-vlm`).
- Documentation: canonical "Testing modes — VLM vs. Visual Regression" section in `MultiVerse/CLAUDE.md`; back-references in CPig, Canary, Slop, and Pigture CLAUDE.md.

**Phase 8.6 Exit Criteria:** every existing test runs unchanged under `--mode pixel-diff` (default); same tests run as VLM under `--mode vlm` when `setup.vlmDescription` is set; `--mode both` produces two verdicts per checkpoint in the HTML report. Cross-repo docs taught the duality once (MultiVerse) and linked from each child repo. Rationale: pixel-diff and VLM are different jobs (regression vs. correctness); mode should be a runtime choice, not a property baked into the test JSON.
