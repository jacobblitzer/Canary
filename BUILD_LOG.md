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

## Phase 8: Rhino .rhp Fix + Canary.Core Extraction

### Checkpoint 8.1: Rhino Plugin .rhp Fix
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Added `<TargetExt>.rhp</TargetExt>`, `<UseWindowsForms>true</UseWindowsForms>`, `<ImportWindowsDesktopTargets>true</ImportWindowsDesktopTargets>`, `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` to `Canary.Agent.Rhino.csproj`. Created `Properties/AssemblyInfo.cs` with `[assembly: Guid]` and `[assembly: PlugInDescription]` attributes. Build produces `Canary.Agent.Rhino.rhp`.
- **Issues Found**: GUID "CANARY00A001" contains non-hex characters — `[assembly: Guid]` requires valid hex.
- **Resolution**: Changed to "CA0A4700A001" (valid hex).
- **SUPERVISOR_FLAGS**: None

### Checkpoint 8.2: Create Canary.Core Project
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `src/Canary.Core/Canary.Core.csproj` (net8.0-windows, `<RootNamespace>Canary</RootNamespace>`). NuGet: SixLabors.ImageSharp 3.1.12, SixLabors.ImageSharp.Drawing 2.1.6. ProjectReference: Canary.Agent. `<InternalsVisibleTo Include="Canary.Tests" />`. Added to Canary.sln.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 8.3: Move Comparison Engine to Core
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Moved `PixelDiffComparer`, `SsimComparer`, `ComparisonResult`, `CheckpointComparison`, `CompositeBuilder` from Harness to Core. Namespace remains `Canary.Comparison` (no change needed thanks to matching RootNamespace). Removed ImageSharp NuGets from Harness (come transitively from Core).
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 8.4: Move Config, Models, Reporting, Input to Core
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Moved all shared types:
  - `Config/`: TestDefinition, WorkloadConfig
  - `Orchestration/`: TestResult, ProcessManager, AppLauncher, Watchdog, TestRunner
  - `Reporting/`: HtmlReportGenerator, JUnitReportGenerator
  - `Input/`: InputEvent, InputRecording, InputRecorder, InputReplayer, ViewportLocator
  Harness retains only: Program.cs, ConsoleTestLogger.cs, Cli/*.cs commands. Updated Tests.csproj to reference both Core and Harness.
- **Issues Found**: TestRunner.cs references `Program.Log`/`Program.LogStatus`/`Program.Verbose` which don't exist in Core — 11 compile errors.
- **Resolution**: Resolved in checkpoint 8.5 (ITestLogger abstraction).
- **SUPERVISOR_FLAGS**: None

### Checkpoint 8.5: Decouple TestRunner via ITestLogger
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: ConsoleTestLogger_Log_WritesTimestampedOutput, ConsoleTestLogger_Quiet_SuppressesLog, ConsoleTestLogger_LogSummary_AlwaysWrites
- **Tests Passed**: 3 new (55 total)
- **Tests Failed**: 0
- **Details**: Created `ITestLogger` interface in Core with `Log`, `LogStatus`, `LogSummary`, `Verbose` members plus `TestStatusLevel` enum. Refactored `TestRunner` constructor to accept `ITestLogger`. Extracted `DiscoverTestsAsync` → `TestDiscovery` and `ApproveTest` → `BaselineManager` (both in Core). `BaselineManager` also includes `ApproveCheckpoint`/`RejectCheckpoint` for future GUI use. Created `ConsoleTestLogger` in Harness implementing `ITestLogger`. Updated `RunCommand` and `ApproveCommand` to wire new types. Added `InternalsVisibleTo` to Harness for test access to `BuildRootCommand`.
- **Issues Found**: Tests couldn't see `Program.BuildRootCommand` (internal) after `InternalsVisibleTo` moved from Harness to Core.
- **Resolution**: Added `<InternalsVisibleTo Include="Canary.Tests" />` to Harness csproj as well.
- **SUPERVISOR_FLAGS**: None

### Phase 8 Gate Verification
- [x] `Canary.Agent.Rhino` builds as `.rhp` (verified in bin/Debug/net48/)
- [x] `Properties/AssemblyInfo.cs` with valid GUID and PlugInDescription attributes
- [x] `Canary.Core` project exists with all shared logic
- [x] Harness is a thin CLI shell (Program.cs, ConsoleTestLogger.cs, Cli/ commands)
- [x] `ITestLogger` interface decouples logging from Console statics
- [x] `TestRunner` accepts `ITestLogger` via constructor injection
- [x] `BaselineManager` extracted with `ApproveTest`, `ApproveCheckpoint`, `RejectCheckpoint`
- [x] `TestDiscovery` extracted with `DiscoverTestsAsync`
- [x] `ConsoleTestLogger` in Harness implements `ITestLogger` with quiet/verbose support
- [x] Both CLI commands (`run`, `approve`) wired to new Core types
- [x] Regression check: all tests pass (55/55 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 9: WinForms Application Shell

### Checkpoint 9.1: Create Canary.UI Project
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `src/Canary.UI/Canary.UI.csproj` (WinExe, net8.0-windows, UseWindowsForms). References Canary.Core and Canary.Agent. `Program.cs` with `ApplicationConfiguration.Initialize()` and `Application.Run(new MainForm())`. Added to Canary.sln.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 9.2: Main Window Layout
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `MainForm` with dark theme (VS Code-inspired colors). `ToolStrip` with 5 buttons: Open Folder, Run Tests, Record, Approve, View Report. `SplitContainer` (vertical) with `TreeView` (250px left panel) and content `Panel`. `StatusStrip` with status label and test count. Minimum size 1024x768, default 1280x900. Custom `DarkToolStripRenderer` for consistent theming.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 9.3: Workload Discovery and Tree Population
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `WorkloadExplorer` service scans workloads directory, loads `WorkloadConfig` + `TestDefinition` per subdirectory. Tree populated with workload -> test hierarchy. Auto-detects `workloads/` relative to exe or CWD. "Open Folder" button shows `FolderBrowserDialog`. `WelcomePanel` with branding and instructions shown when no test is selected.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 9.4: UI Tests
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: LoadWorkloads_ValidDirectory_DiscoversWorkloads, LoadWorkloads_EmptyDirectory_ReturnsEmpty, LoadWorkloads_MissingTestsDir_ReturnsWorkloadWithNoTests
- **Tests Passed**: 3 new (58 total)
- **Tests Failed**: 0
- **Issues Found**: Test JSON for test definitions was missing required `workload` field.
- **Resolution**: Added `workload` field to test fixture JSON.
- **SUPERVISOR_FLAGS**: None

### Phase 9 Gate Verification
- [x] `Canary.UI.exe` builds as WinExe with WinForms
- [x] `MainForm` has ToolStrip, SplitContainer (TreeView + content Panel), StatusStrip
- [x] Dark theme with consistent colors across all controls
- [x] `WorkloadExplorer` discovers workloads and test definitions
- [x] TreeView populated with workload -> test hierarchy
- [x] Auto-detection of `workloads/` directory on startup
- [x] `WelcomePanel` shown when no test is selected
- [x] Regression check: all tests pass (58/58 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 10: Results Viewer + Baseline Management

### Checkpoint 10.1: Results Viewer Control
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `ResultsViewerControl` (UserControl) displays test results. Header with test name, status badge, duration, "Approve All" button. Per-checkpoint rows with stats (diff%, tolerance, SSIM), three `PictureBox` controls (baseline/candidate/diff, SizeMode=Zoom), approve/reject buttons. Events: `ApproveCheckpointRequested`, `RejectCheckpointRequested`, `ApproveAllRequested`, `ImageClicked`.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 10.2: Full-Size Image Viewer
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `ImageViewerForm` (modal Form) shows full-resolution images. Toolbar to toggle baseline/candidate/diff. Mouse wheel zoom (0.1x-10x), click-drag pan via `ScrollableControl.AutoScrollPosition`. Keyboard: Escape closes, Left/Right switch images, +/- zoom. Non-locking file load via `FileStream`.
- **Issues Found**: `AutoScrollPosition` is on `ScrollableControl`, not `Control` — cast needed for `_pictureBox.Parent`.
- **Resolution**: Cast parent to `ScrollableControl` with pattern matching.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 10.3: Approve/Reject from GUI
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `BaselineManager` (already created in Phase 8.5) provides `ApproveCheckpoint` and `RejectCheckpoint` methods. `ResultsViewerControl` exposes per-checkpoint and per-test approve/reject buttons wired to events. GUI consumers connect events to `BaselineManager` calls.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 10.4: Test Result Serialization + History
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `TestResultSerializer` saves/loads `TestResult` as JSON with `JsonStringEnumConverter` and custom `TimeSpanConverter`. `ResultsHistory` service scans `results/` directories for `result.json` files, returns sorted by timestamp descending.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 10.5: Tests
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: ApproveCheckpoint_CopiesCandidateToBaseline, RejectCheckpoint_DeletesCandidate, RoundTrip_PreservesAllFields, Scan_FindsSavedResults, Scan_EmptyDirectory_ReturnsEmpty
- **Tests Passed**: 5 new (63 total)
- **Tests Failed**: 0
- **SUPERVISOR_FLAGS**: None

### Phase 10 Gate Verification
- [x] `ResultsViewerControl` shows per-checkpoint baseline/candidate/diff images with stats
- [x] `ImageViewerForm` provides full-resolution viewing with zoom and pan
- [x] Approve/reject buttons per-checkpoint and per-test
- [x] `BaselineManager.ApproveCheckpoint` copies candidate to baseline
- [x] `BaselineManager.RejectCheckpoint` deletes candidate
- [x] `TestResultSerializer` round-trips TestResult to/from JSON
- [x] `ResultsHistory` scans results directories for saved results
- [x] Regression check: all tests pass (63/63 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 11: Test Manager — Create, Edit, Run

### Checkpoint 11.1: Test Definition Editor
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `TestEditorControl` (UserControl) with fields for name, workload, description, setup (file browser, viewport W/H/projection/display, commands list), checkpoints `DataGridView`. `ErrorProvider` validation (name required, workload required, tolerance numeric). Save serializes to JSON. `LoadDefinition` populates form from existing `TestDefinition`.
- **Issues Found**: `Validate()` hides inherited `ContainerControl.Validate()` — TreatWarningsAsErrors.
- **Resolution**: Added `new` keyword.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 11.2: Workload Configuration Editor
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `WorkloadEditorControl` (UserControl) with fields for all `WorkloadConfig` properties. Browse for executable, agent type combo (rhino/wpf/electron/custom), pipe name, startup timeout, window title. Save serializes to JSON. `LoadConfig` populates form from existing config.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 11.3: Test Runner with Live Progress
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `TestRunnerPanel` (UserControl) with status label, stop button, progress bar, and `ListBox` log. `GuiTestLogger : ITestLogger` fires events (`MessageLogged`, `StatusLogged`, `SummaryLogged`) marshalled to UI thread via `Control.BeginInvoke`. Tests run on background thread via `Task.Run`. Stop button cancels via `CancellationTokenSource`.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 11.4: Recording UI
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `RecordingPanel` (UserControl) with workload combo, window title field, start/stop buttons. Wires to `InputRecorder.StartRecording()`/`StopRecording()` from Core. `SaveFileDialog` writes `.input.json`. Uses `ViewportLocator.FindWindowByTitle` to find target.
- **Issues Found**: `InputRecorder` constructor requires `(IntPtr, string, string)`, methods are `StartRecording`/`StopRecording` (not `Start`/`Stop`/`GetRecording`), no `EventCaptured` event.
- **Resolution**: Matched actual API signatures.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 11.5: Tests
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: TestDefinition_SerializeDeserialize_RoundTrips, TestDefinition_MissingName_ThrowsJsonException, TestDefinition_MissingWorkload_ThrowsJsonException, WorkloadConfig_SerializeDeserialize_RoundTrips, GuiTestLogger_Log_FiresMessageLoggedEvent, GuiTestLogger_LogStatus_FiresStatusLoggedEvent
- **Tests Passed**: 6 new (69 total)
- **Tests Failed**: 0
- **Issues Found**: Test project needed `<UseWindowsForms>true</UseWindowsForms>` to reference `System.Windows.Forms.Form`.
- **Resolution**: Added to test csproj.
- **SUPERVISOR_FLAGS**: None

### Phase 11 Gate Verification
- [x] `TestEditorControl` creates/edits test definitions with validation
- [x] `WorkloadEditorControl` creates/edits workload configs with browse for exe
- [x] `TestRunnerPanel` runs tests with live progress, stop button, log display
- [x] `GuiTestLogger` fires events marshalled to UI thread
- [x] `RecordingPanel` wires to `InputRecorder` with start/stop and save dialog
- [x] Regression check: all tests pass (69/69 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 12: Polish + Integration

### Checkpoint 12.1: Keyboard Shortcuts
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `MainForm.KeyPreview = true` with `KeyDown` handler. Ctrl+O (open folder), Ctrl+R / F5 (run tests), Ctrl+Shift+R (record), Ctrl+A (approve), Delete (delete test with confirmation dialog), Escape (close modals via ImageViewerForm).
- **SUPERVISOR_FLAGS**: None

### Checkpoint 12.2: Drag-and-Drop
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `TreeView.AllowDrop = true`. `DragEnter` accepts `DataFormats.FileDrop`. `DragDrop` handles `.json` (import test) and `.3dm` (create test with model) files. Status bar shows feedback.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 12.3: Context Menus
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Right-click workload: Run All Tests, Edit Config, Open in Explorer. Right-click test: Run, Edit, Approve, Delete, Open in Explorer. `ContextMenuStrip` dynamically shown by node type via `NodeMouseClick`. `Process.Start(UseShellExecute: true)` for Open in Explorer.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 12.4: Update Spec Documents
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Updated `CLAUDE.md` with new projects (Core, UI), new spec files (PHASES_UI.md, TESTS_UI.md), corrected framework targets, current phase set to 12. `spec/PHASES_UI.md` and `spec/TESTS_UI.md` already written in Phase 8.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 12.5: Final Regression
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: Cli_Help_StillWorks_AfterCoreExtraction, TestRunner_UsesCore_WithITestLogger, MainForm_CanBeConstructed
- **Tests Passed**: 3 new (72 total)
- **Tests Failed**: 0
- **Details**: CLI still functional after Core extraction (--help prints all 4 commands). TestRunner accepts ITestLogger from Core. MainForm constructs without errors, title contains "Canary", min size >= 1024x768.
- **SUPERVISOR_FLAGS**: None

### Phase 12 Gate Verification
- [x] Keyboard shortcuts: Ctrl+O, Ctrl+R, F5, Ctrl+Shift+R, Ctrl+A, Delete, Escape
- [x] Drag-and-drop: .json and .3dm files accepted on tree view
- [x] Context menus: workload (Run/Edit/Open) and test (Run/Edit/Approve/Delete/Open)
- [x] All toolbar buttons wired to actions
- [x] Spec documents updated (CLAUDE.md, PHASES_UI.md, TESTS_UI.md)
- [x] CLI still works after Core extraction
- [x] GUI MainForm launches without errors
- [x] Full regression: 72 tests pass, 0 warnings
- [x] `dotnet build` — 0 errors, 0 warnings

---

## Post-Phase 12: Penumbra Bug Fix Verification (2026-04-25)

### Canary Run — 4 Suites After 9 Bug Fixes
- **Date**: 2026-04-25
- **Requested by**: Claude Code
- **Command**: `canary run --workload penumbra --suite <effects|materials|display-modes|overlays>`
- **Status**: PASS (all diffs intentional)
- **Results**:
  - `effects`: 6 pass, 2 fail (fresnel + contours — intentional shader changes)
  - `materials`: 2 pass, 3 fail (wood/zebra/damascus — intentional noise + stripe fixes)
  - `display-modes`: 8 NEW (no baselines yet)
  - `overlays`: 7 NEW (no baselines yet)
- **Notes**: 0 unexpected failures, 0 crashes. 15 NEW tests awaiting `canary approve`.

---

## Phase 13: CPig Regression Workload

### Checkpoint 13.1: New agent actions
- **Date**: 2026-04-26
- **Status**: PASS
- **Details**: Added `GrasshopperSetToggle`, `GrasshopperSetPanelText`, `GrasshopperGetPanelText` to `RhinoAgent.cs`. Each follows the existing `HandleGrasshopperSetSlider` pattern: case-insensitive nickname lookup, mutate, `ExpireSolution(true)`, marshal via `InvokeOnUi`. `GetPanelText` prefers VolatileData over UserText.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 13.2: Test runner extensions
- **Date**: 2026-04-26
- **Status**: PASS
- **Details**: Extended `TestDefinition.cs` with `TestAction` and `TestAssert` classes. `TestRunner.RunTestAsync` executes `actions[]` sequentially before checkpoint capture. `asserts[]` evaluated after each checkpoint via `EvaluateClientAssertAsync` (named pipe path) and `EvaluateAssertAsync` (in-process path). Three assert types implemented: `PanelEquals` (exact trimmed match), `PanelContains` (substring), `PanelDoesNotContain` (inverse substring). Unknown types fail with typo-hint message.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 13.3: Loader fixture
- **Date**: 2026-04-26
- **Status**: PASS
- **Details**: Built `workloads/rhino/fixtures/cpig_slop_loader.gh` (21KB) with Slop component, `JsonPath` panel, `Build` toggle, Crash Guard, Log Hub, three output panels (`SlopLog`, `SlopSuccess`, `SlopCount`). Generator template saved alongside as `cpig_slop_loader_generator.json`. Document-level viewport set to deterministic projection + display mode.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 13.4: Bulk-generate test JSONs
- **Date**: 2026-04-26
- **Status**: PASS
- **Details**: `scripts/cpig-test-from-slop.ps1` implemented — reads Slop JSON paths from `CPig/research/slop_tests/`, emits matching `cpig-NN-slug.json` under `workloads/rhino/tests/`. Script is idempotent. All 17 test JSONs generated and committed: `cpig-00-smoke-ping` through `cpig-16-field-evaluate`. Each test definition includes 3 actions (SetPanelText → SetToggle → WaitForSolution) and 3 asserts (SlopSuccess=True, SlopLog !contains FATAL, SlopLog !contains CRASH).
- **SUPERVISOR_FLAGS**: None

### Checkpoint 13.5: Initial baselines + Field Modifier tests
- **Date**: 2026-04-27
- **Status**: PASS (all 22 tests run, 22 NEW — first-run baseline capture)
- **Details**: 5 new test definitions added for CPig's Field Modifiers sprint (cpig-19 through cpig-23). Suite expanded from 17 to 22 tests. All tests run via `canary run --workload rhino --suite cpig` in shared Rhino instance. All 22 tests report SlopSuccess=True, no FATAL/CRASH in logs.
- **Notes**: Baselines captured but not yet approved. cpig-10 and cpig-13 remain excluded from suite (BUG-004 scope — libfive JIT batch eval crash).
- **SUPERVISOR_FLAGS**: None

## Summary

| Phase | Description | Tests Added | Cumulative |
|-------|-------------|-------------|------------|
| 0 | Solution Scaffold + CLI Shell | 3 | 3 |
| 1 | Named Pipe IPC + Agent Protocol | 11 | 14 |
| 2 | Input Recorder + Replayer | 10 | 24 |
| 3 | Screenshot Comparison Engine | 16 | 40 |
| 4 | Test Runner + Orchestrator | 9 | 49 |
| 5 | HTML Report + Polish | 3 | 52 |
| 6 | Rhino Workload Agent | 0 | 52 |
| 7 | Future Workloads (Stub) | 0 | 52 |
| 8 | .rhp Fix + Canary.Core Extraction | 3 | 55 |
| 9 | WinForms Application Shell | 3 | 58 |
| 10 | Results Viewer + Baseline Management | 5 | 63 |
| 11 | Test Manager — Create, Edit, Run | 6 | 69 |
| 12 | Polish + Integration | 3 | 72 |
| 13 | CPig Regression Workload (13.1–13.5) | 0 (infra) | 72 + 22 test defs |

---

## 2026-04-29 — Test mode duality (Phase 8.6)

Promoted comparison mode to a runtime selector — the user picks pixel-diff (visual regression — code stability) or VLM (semantic correctness) per `canary run` invocation, without rewriting test JSONs.

- `--mode <pixel-diff|vlm|both>` flag added to `canary run` (`src/Canary.Harness/Cli/RunCommand.cs`, default `pixel-diff`).
- `ModeOverride` enum + `CheckpointMode` enum added to `Canary.Orchestration` (`src/Canary.Core/Orchestration/TestRunner.cs`).
- `TestRunner.ModeOverride` property; `RunCommand` propagates the parsed flag.
- Optional `setup.vlmDescription` field added to `TestSetup` (`src/Canary.Core/Config/TestDefinition.cs`); `ProcessVlmCheckpointAsync` falls back to it when a checkpoint omits its own `description`.
- Refactor: `ProcessCheckpointAsync` + `ProcessAgentCheckpointAsync` take an optional `forceMode: CheckpointMode?` parameter; centralized loop in `DispatchClientCheckpointAsync` / `DispatchAgentCheckpointAsync` replaces 4 inlined call sites. `--mode both` runs each checkpoint twice and emits two `CheckpointResult` rows (the VLM one suffixed with `-vlm`).
- Mode resolution: per-checkpoint `mode == "vlm"` always wins; otherwise `--mode` flag applies; otherwise pixel-diff.
- Doc updates: `docs/features/vlm-oracle.md` rewritten with duality framing + writing-good-descriptions section; `spec/PHASES.md` Phase 8.6 entry; `CLAUDE.md` Quick Reference. MultiVerse `CLAUDE.md` gains the canonical "Testing modes" section; child repos (CPig, Slop, Pigture) carry back-references.
- Build: `dotnet build Canary.sln` → 0/0.

Cross-repo coupling: CPig regenerates 19 retopo Slop+Canary test pairs that all emit `setup.vlm` + `setup.vlmDescription`; CPig adds 2 new accessor components (`CrossFieldExplode` BB019, `PatchLayoutExplode` BB01A) so VLM mode has visually distinct viewport content per stage.
