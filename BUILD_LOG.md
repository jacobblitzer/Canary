# Build Log — Canary

## Phase 0: Solution Scaffold + CLI Shell

### Checkpoint 0.1: Solution Structure
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: N/A (structural checkpoint)
- **Tests Passed**: N/A
- **Tests Failed**: 0
- **Issues Found**: `dotnet new sln` creates `.slnx` by default on .NET 9 SDK — used `--format sln` flag to force classic `.sln` format as specified.
- **Resolution**: Recreated solution with `--format sln`
- **SUPERVISOR_FLAGS**: None

### Checkpoint 0.2: CLI Entry Point
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: Manual CLI verification
- **Tests Passed**: `canary --help` prints usage with all 4 subcommands; `canary run --help` prints `--workload` and `--test` options
- **Tests Failed**: 0
- **Issues Found**: `Console.TreatControlCAsInput = false` throws `IOException` when no real console handle is available (non-interactive terminals). Wrapped in try-catch.
- **Resolution**: Guarded with `try { } catch (IOException) { }`
- **SUPERVISOR_FLAGS**: None

### Checkpoint 0.3: Test Foundation
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: Program_NoArgs_PrintsHelp, Program_RunHelp_PrintsRunUsage, Program_CtrlCHandler_IsRegistered
- **Tests Passed**: 3
- **Tests Failed**: 0
- **Issues Found**: None
- **Resolution**: N/A
- **SUPERVISOR_FLAGS**: None

### Phase 0 Gate Verification
- [x] `Canary.sln` builds with `dotnet build` — 0 errors, 0 warnings
- [x] `canary.exe` runs and prints help text
- [x] `canary run --help` prints usage for the run command
- [x] `Ctrl+C` handler registered (verified by test)
- [x] `Canary.Tests` project with 3 passing unit tests
- [x] `.gitignore` excludes bin/obj/results
- [x] `README.md` with project overview

## Phase 1: Named Pipe IPC + Agent Protocol

### Checkpoint 1.1: RPC Message Types
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: RpcMessage_SerializeRequest_RoundTrips, RpcMessage_SerializeResponse_WithResult_RoundTrips, RpcMessage_SerializeError_RoundTrips, RpcMessage_DeserializeInvalid_ThrowsClear, RpcMessage_SerializeRequest_WithParams_RoundTrips
- **Tests Passed**: 5
- **Tests Failed**: 0
- **Issues Found**: `System.Text.Json` not available in net48 by default — added NuGet package.
- **Resolution**: Added `System.Text.Json` v10.0.5 NuGet to `Canary.Agent.csproj`
- **SUPERVISOR_FLAGS**: None

### Checkpoint 1.2: Agent Interface
- **Date**: 2026-04-04
- **Status**: PASS (completed in Phase 0 — ICanaryAgent + data contracts already defined)
- **SUPERVISOR_FLAGS**: None

### Checkpoint 1.3: Pipe Server (Agent Side)
- **Date**: 2026-04-04
- **Status**: PASS
- **Issues Found**: net48 doesn't support `StreamReader(Stream, leaveOpen:)` shorthand or `ReadLineAsync(CancellationToken)`. Fixed with explicit encoding constructors and `Task.WhenAny` pattern. Also UTF8 BOM corruption — used `new UTF8Encoding(false)`.
- **Resolution**: Used `UTF8Encoding(false)`, explicit constructor params, `Task.WhenAny` for cancellable reads.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 1.4: Pipe Client (Harness Side)
- **Date**: 2026-04-04
- **Status**: PASS
- **Issues Found**: Same net48 API issues as server. Also `StreamWriter.Dispose()` throws on broken pipe — guarded in `Dispose()`.
- **Resolution**: Wrapped `_writer?.Dispose()` in try-catch for `IOException`.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 1.5: IPC Round-Trip Tests
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: AgentServer_Heartbeat_ReturnsOk, HarnessClient_Timeout_ThrowsTimeoutException, HarnessClient_Execute_PassesParams, AgentServer_CaptureScreenshot_ReturnsMockPath, HarnessClient_SequentialRequests_AllSucceed, AgentServer_Shutdown_DisconnectsCleanly
- **Tests Passed**: 6
- **Tests Failed**: 0
- **Issues Found**: None remaining
- **SUPERVISOR_FLAGS**: None

### Phase 1 Gate Verification
- [x] `Canary.Agent.csproj` builds as multi-target `net8.0;net48` class library
- [x] `ICanaryAgent` interface with Execute, CaptureScreenshot, Heartbeat, Abort
- [x] `AgentServer` listens on named pipe, dispatches JSON-RPC to ICanaryAgent
- [x] `HarnessClient` connects to pipe, sends JSON-RPC, awaits responses with timeout
- [x] Round-trip test: heartbeat returns ok=true
- [x] Timeout test: throws TimeoutException when agent doesn't respond
- [x] Pipe name format: `canary-{name}-{pid}`
- [x] All Phase 0 tests still pass (regression check: 14/14)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 2: Input Recorder + Replayer

### Checkpoint 2.1: Input Event Model
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: InputEvent_Serialize_RoundTrips, InputRecording_Serialize_PreservesMetadata
- **Tests Passed**: 2
- **Tests Failed**: 0
- **Issues Found**: xUnit analyzer enforces `Assert.Single` over `Assert.Equal(1, count)` (TreatWarningsAsErrors)
- **Resolution**: Used `Assert.Single` as required
- **SUPERVISOR_FLAGS**: None

### Checkpoint 2.2: Viewport Locator
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: NormalizeDenormalize_RoundTrips, TopLeft_ReturnsZeroZero, BottomRight_ReturnsOneOne, DifferentViewportSize_ScalesCorrectly, Center_NormalizesToHalf, FindWindow_BadTitle_ReturnsZero, IsValidTarget_Zero_ReturnsFalse, GetViewportBounds_Zero_ReturnsEmptyBounds
- **Tests Passed**: 8
- **Tests Failed**: 0
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 2.3: Input Recorder
- **Date**: 2026-04-04
- **Status**: PASS
- **Issues Found**: `Thread.SetApartmentState` triggers CA1416 platform analyzer on `net8.0`. Changed target to `net8.0-windows` for Harness, Tests, and Tests.Integration projects. This is correct — Canary is Windows-only (SendInput, named pipes, Win32 hooks).
- **Resolution**: Changed TargetFramework from `net8.0` to `net8.0-windows` for Canary.Harness, Canary.Tests, Canary.Tests.Integration
- **SUPERVISOR_FLAGS**: None

### Checkpoint 2.4: Input Replayer
- **Date**: 2026-04-04
- **Status**: PASS
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Phase 2 Gate Verification
- [x] `InputEvent` and `InputRecording` serialize/deserialize correctly
- [x] Coordinate normalization/denormalization round-trips within ±1px
- [x] `ViewportLocator` finds windows by title, gets client area bounds
- [x] `InputRecorder` uses `SetWindowsHookEx` with WH_MOUSE_LL/WH_KEYBOARD_LL on STA thread
- [x] `InputReplayer` reads recordings, denormalizes coords, injects via SendInput with MOUSEEVENTF_ABSOLUTE
- [x] Replayer supports CancellationToken, SpeedMultiplier, checkpoint pause callbacks
- [x] Regression check: all Phase 0+1 tests pass (24/24 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 3: Screenshot Comparison Engine

### Checkpoint 3.1: Pixel Diff Comparer
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: PixelDiffComparer_IdenticalImages_ReturnsZeroDiff, PixelDiffComparer_SinglePixelDiff_ReturnsCorrectCount, PixelDiffComparer_TenPercentNoise_ReturnsApproxTenPercent, PixelDiffComparer_BelowThreshold_CountsAsSame, PixelDiffComparer_AboveThreshold_CountsAsDifferent, PixelDiffComparer_DimensionMismatch_ThrowsArgumentException, PixelDiffComparer_DiffImage_HighlightsChanges, PixelDiffComparer_ToleranceGate_PassesWhenBelowTolerance, PixelDiffComparer_ToleranceGate_FailsWhenAboveTolerance
- **Tests Passed**: 9
- **Tests Failed**: 0
- **Issues Found**: Nested `ProcessPixelRows` lambdas fail with CS9108 (ref-like type in lambda). Switched to `CopyPixelDataTo` + `Image.LoadPixelData` approach.
- **Resolution**: Use flat array comparison instead of nested pixel accessor lambdas.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 3.2: SSIM Comparer
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: SsimComparer_IdenticalImages_ReturnsOne, SsimComparer_CompletelyDifferent_ReturnsLow, SsimComparer_SlightShift_ReturnsHigh
- **Tests Passed**: 3
- **Tests Failed**: 0
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 3.3: Composite Builder
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: CompositeBuilder_ThreeCheckpoints_CorrectDimensions, CompositeBuilder_ZeroCheckpoints_ReturnsNull, CompositeBuilder_SingleCheckpoint_ProducesValidImage, CompositeBuilder_LabelsIncludeStatus
- **Tests Passed**: 4
- **Tests Failed**: 0
- **Issues Found**: `SixLabors.ImageSharp.Drawing` NuGet needed for `DrawImage` compositing (not included in base ImageSharp).
- **Resolution**: Added `SixLabors.ImageSharp.Drawing` v2.1.6 NuGet to Harness and Tests projects.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 3.4: Test Data
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: All test images generated programmatically in test fixture setup — no binary files committed. Tests use `CreateSolidImage`, `CreateGradientImage` helpers.
- **SUPERVISOR_FLAGS**: None

### Phase 3 Gate Verification
- [x] `PixelDiffComparer` compares images pixel-by-pixel with configurable colorThreshold
- [x] `ComparisonResult` includes DiffPercentage, ChangedPixels, TotalPixels, DiffImage, Passed
- [x] Diff image renders changed pixels in magenta, unchanged as semi-transparent
- [x] `SsimComparer` computes SSIM with 8x8 sliding window, returns [0,1] score
- [x] SSIM uses grayscale luminance (0.299R + 0.587G + 0.114B) and standard C1/C2 constants
- [x] `CompositeBuilder` stitches baseline|candidate|diff horizontally with 2px gaps
- [x] Composite stacks strips vertically with label bars (green=pass, red=fail)
- [x] Test images generated programmatically (no binary files committed)
- [x] Regression check: all Phase 0+1+2+3 tests pass (40/40 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 4: Test Runner + Orchestrator

### Checkpoint 4.1: Test Definition Parser
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: TestDefinition_Parse_ValidJson_AllFieldsPopulated, TestDefinition_Parse_MissingName_ThrowsClearError, TestDefinition_Parse_EmptyCheckpoints_IsValid, WorkloadConfig_Parse_AllFieldsPopulated
- **Tests Passed**: 4
- **Tests Failed**: 0
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 4.2: App Launcher + Process Manager
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: ProcessManager_Track_KillAll_TerminatesProcess, ProcessManager_KillAll_AlreadyExited_NoError
- **Tests Passed**: 2
- **Tests Failed**: 0
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 4.3: Watchdog
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: Watchdog_HealthyAgent_NoEvent, Watchdog_UnresponsiveAgent_FiresDeadEvent, Watchdog_Cancellation_StopsCleanly
- **Tests Passed**: 3
- **Tests Failed**: 0
- **Issues Found**: None. Used `IHeartbeatSource` interface for testability — mock heartbeat sources for unit tests, `HarnessClientHeartbeatSource` adapter for production.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 4.4: Test Runner Core
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `TestRunner` orchestrates full lifecycle: launch → connect → setup → checkpoint capture → compare → composite. `TestResult`/`CheckpointResult`/`SuiteResult` models with pass/fail/crash/new statuses. Handles missing baselines (NEW), dimension mismatches (FAIL), app crashes (via watchdog), cancellation.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 4.5: CLI Integration
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `canary run --workload <name>` discovers and runs all tests. `canary run --workload <name> --test <name>` runs single test. `canary approve --workload <name> --test <name>` copies candidates to baselines. Console output includes timestamped status with "Press Ctrl+C to abort". Summary shows pass/fail/crash/new counts.
- **SUPERVISOR_FLAGS**: None

### Phase 4 Gate Verification
- [x] `TestDefinition` and `WorkloadConfig` parse from JSON with validation
- [x] Missing required fields throw clear `JsonException` messages
- [x] `AppLauncher` starts processes and polls for named pipe availability
- [x] `ProcessManager` tracks processes and `KillAll()` terminates them safely
- [x] `Watchdog` monitors heartbeats, fires `OnAppDead` after 3 consecutive failures
- [x] `TestRunner` orchestrates full test lifecycle with checkpoint capture + comparison
- [x] Missing baseline → NEW status with guidance to run `canary approve`
- [x] `canary run --workload` and `canary approve --workload --test` functional
- [x] Console output: timestamped PASS/FAIL/CRASH/NEW with diff percentages
- [x] Ctrl+C propagated via CancellationToken, `ProcessManager.KillAll()` on shutdown
- [x] Regression check: all Phase 0+1+2+3+4 tests pass (49/49 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 5: HTML Report + Polish

### Checkpoint 5.1: HTML Report Generator
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: HtmlReportGenerator_SingleTest_ProducesValidHtml, HtmlReportGenerator_FailedTest_ShowsRed
- **Tests Passed**: 2
- **Tests Failed**: 0
- **Details**: Self-contained HTML with inline base64 images. Dark theme CSS. Header with pass/fail/crash/new summary badges. Per-test cards with status badge, checkpoint table (name/status/diff%/tolerance/SSIM), composite image. Failed tests sorted before passed.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 5.2: JUnit Report Generator
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: JUnitReportGenerator_ProducesValidXml
- **Tests Passed**: 1
- **Tests Failed**: 0
- **Details**: Standard JUnit XML via `System.Xml.Linq`. `<testsuite>` with `<testcase>` elements. Failed tests get `<failure>` with diff info. Crashed tests get `<error>`. New tests get `<skipped>`. Per-checkpoint details in `<system-out>`.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 5.3: Console UI Polish
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `Program.LogStatus(symbol, message, color)` provides color-coded console output (Green=PASS, Red=FAIL, Magenta=CRASH, Yellow=NEW). `--verbose` flag shows per-checkpoint diff/ssim/tolerance details. `--quiet` suppresses all output except summary line and exit code (for CI). Every status line includes "Press Ctrl+C to abort".
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 5.4: Edge Cases + Report Command
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `canary report` command finds most recent `report.html` and opens via `Process.Start(UseShellExecute: true)`. Supports optional `--workload` to scope the search. Empty suite (0 checkpoints) handled gracefully — no composite built, summary still prints. Missing baselines produce NEW status with guidance message. Report generation runs after every suite execution.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Phase 5 Gate Verification
- [x] `HtmlReportGenerator` produces self-contained HTML with embedded base64 images
- [x] Dark theme CSS, failed tests sorted first, status badges (pass/fail/crash/new)
- [x] Per-test checkpoint table with diff%, tolerance, SSIM columns
- [x] `JUnitReportGenerator` produces valid XML parseable by CI systems
- [x] `<failure>` elements for failed tests, `<error>` for crashed, `<skipped>` for new
- [x] `canary run` generates HTML + JUnit reports to `workloads/{name}/results/`
- [x] `canary report` opens most recent report in default browser
- [x] `--verbose` shows per-checkpoint details, `--quiet` suppresses non-summary output
- [x] Color-coded console output: Green/Red/Magenta/Yellow for PASS/FAIL/CRASH/NEW
- [x] "Press Ctrl+C to abort" in every status line
- [x] Regression check: all Phase 0+1+2+3+4+5 tests pass (52/52 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 6: Rhino Workload Agent

### Checkpoint 6.1: Rhino Plugin Shell
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `Canary.Agent.Rhino` project targeting `net48` with RhinoCommon v8.15.25013.13001 NuGet (compile-only). `CanaryRhinoPlugin` extends `Rhino.PlugIns.PlugIn`, starts `AgentServer` on a background thread via `Task.Run` on plugin load. Pipe name: `canary-rhino-{pid}`. Graceful shutdown via `CancellationTokenSource` in `OnShutdown()`.
- **Issues Found**: RhinoCommon v8.15.25012.13001 not available; resolved to v8.15.25013.13001 (NuGet NU1603 with TreatWarningsAsErrors). Used exact available version.
- **Resolution**: Pinned RhinoCommon to v8.15.25013.13001.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 6.2: Rhino Agent Implementation
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `RhinoAgent : ICanaryAgent` implements:
  - `ExecuteAsync("OpenFile", {"path": "..."})` → `RhinoDoc.Open(path)`
  - `ExecuteAsync("RunCommand", {"command": "..."})` → `RhinoApp.RunScript(command, echo: false)`
  - `ExecuteAsync("SetViewport", {...})` → configures projection (Perspective/Parallel/Top/Front/Right), display mode, viewport size
  - `ExecuteAsync("SetView", {"name": "..."})` → restores named view or uses `_-SetView` command fallback
  - `HeartbeatAsync()` → returns ok=true with rhinoVersion, documentName, objectCount state
  - `AbortAsync()` → sends `_Cancel` keystroke
- **Issues Found**: `NamedViewTable.Restore` API changed between Rhino versions — `RestoreAnimated(int, RhinoView, bool)` marked obsolete, `Restore(int, RhinoView)` signature wrong. Correct signature is `Restore(int, RhinoViewport)`.
- **Resolution**: Used `doc.NamedViews.Restore(index, view.ActiveViewport)`.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 6.3: Rhino Screenshot Capture
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `RhinoScreenCapture.Capture(CaptureSettings)` uses `ViewCaptureSettings` + `ViewCapture.CaptureToBitmap` to capture the active viewport at requested dimensions (72 DPI). Saves as PNG via `System.Drawing.Imaging.ImageFormat.Png`. Validates: no active viewport → `InvalidOperationException`, invalid dimensions → `ArgumentException`, null bitmap → `InvalidOperationException`. Ensures output directory exists before saving.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 6.4: Smoke Test Definition
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `workloads/rhino/workload.json` (Rhino 8 config with `/nosplash` arg, 30s startup timeout, pipe name `canary-rhino`) and `workloads/rhino/tests/smoke-test.json` (creates a sphere, sets perspective shaded viewport, captures one screenshot at 2% tolerance). End-to-end verification requires Rhino installed — this is an Integration test.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Phase 6 Gate Verification
- [x] `Canary.Agent.Rhino` builds as a `net48` class library referencing RhinoCommon (compile-only)
- [x] Plugin starts `AgentServer` on background thread on load, pipe name `canary-rhino-{pid}`
- [x] `RhinoAgent.Execute("OpenFile", {"path"})` opens .3dm files via `RhinoDoc.Open`
- [x] `RhinoAgent.Execute("SetViewport", {...})` configures projection, display mode, size
- [x] `RhinoAgent.Execute("RunCommand", {"command"})` runs commands via `RhinoApp.RunScript`
- [x] `RhinoAgent.Execute("SetView", {"name"})` restores named views or standard projections
- [x] `RhinoAgent.CaptureScreenshot` captures via `ViewCapture.CaptureToBitmap`, saves PNG
- [x] `RhinoAgent.Heartbeat` returns ok=true with rhinoVersion, documentName, objectCount
- [x] Smoke test definition created: `workloads/rhino/tests/smoke-test.json`
- [x] Agent server shuts down gracefully on plugin unload
- [x] Regression check: all Phase 0+1+2+3+4+5 unit tests pass (52/52 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 7: Future Workloads (Stub)

### Checkpoint 7.1: Agent Template
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `docs/creating-a-workload.md` — comprehensive guide covering: project setup, `ICanaryAgent` implementation, `AgentServer` startup on background thread, `workload.json` configuration, test definition authoring, tolerance guidelines, and full examples for WPF (`RenderTargetBitmap`) and Electron (Chrome DevTools Protocol) applications. Includes a pre-flight checklist.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 7.2: Qualia Stub
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `workloads/qualia/workload.json` with placeholder configuration and `workloads/qualia/AGENT_NOTES.md` documenting `ICanaryAgent` method mappings for Qualia — scene loader for OpenFile, command system for RunCommand, 3D viewport for SetViewport, camera presets for SetView, and framebuffer readback or `RenderTargetBitmap` for screenshot capture. Notes architecture considerations (framework target, GPU capture, UI thread marshalling).
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 7.3: Penumbra Stub
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `workloads/penumbra/workload.json` with placeholder configuration and `workloads/penumbra/AGENT_NOTES.md` documenting `ICanaryAgent` method mappings for Penumbra — geometry tree loader, shader parameter control, WebGL canvas capture via `toDataURL`/`toBlob`. Documents two agent approaches: Electron main-process agent vs Chrome DevTools Protocol bridge. Includes Penumbra-specific actions (SetGeometry, SetShaderParam, ToggleWireframe).
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Phase 7 Gate Verification
- [x] `docs/creating-a-workload.md` guide with step-by-step instructions
- [x] Guide covers: ICanaryAgent implementation, AgentServer startup, workload.json, test definitions
- [x] WPF example using `RenderTargetBitmap` with `Dispatcher.Invoke`
- [x] Electron/web example using Chrome DevTools Protocol
- [x] `workloads/qualia/workload.json` placeholder created
- [x] `workloads/qualia/AGENT_NOTES.md` with ICanaryAgent method mapping
- [x] `workloads/penumbra/workload.json` placeholder created
- [x] `workloads/penumbra/AGENT_NOTES.md` with ICanaryAgent method mapping and architecture notes
- [x] Regression check: all unit tests pass (52/52 total)
- [x] `dotnet build` — 0 errors, 0 warnings
- [x] Documentation review: all guides accurate against current codebase
