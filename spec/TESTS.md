# TESTS.md — Canary Test Specifications

## Test Infrastructure

### Project: `Canary.Tests`
- Framework: xUnit
- Two test categories:
  - `[Trait("Category", "Unit")]` — Pure logic, no external apps. Run headless.
  - `[Trait("Category", "Integration")]` — Requires a running application (Rhino, etc.). Run on a machine with the app installed.

### Test Naming Convention
```
[ClassName]_[MethodName]_[Scenario]_[ExpectedResult]
```
Example: `PixelDiffComparer_Compare_IdenticalImages_ReturnsZeroDiff`

### Test Data
- Test images generated programmatically in test setup (no binary files committed)
- Test recordings created as in-memory `InputRecording` objects
- Mock agents created as simple `ICanaryAgent` implementations returning canned responses

---

## Phase 0 Tests

### CLI Tests (Unit)

```
Program_NoArgs_PrintsHelp
  Action: run canary.exe with no arguments
  Assert: output contains "canary" and "run"

Program_RunHelp_PrintsRunUsage
  Action: run canary.exe run --help
  Assert: output contains "--workload" and "--test"

Program_CtrlCHandler_IsRegistered
  Action: verify CancellationTokenSource is created in Program.Main
  Assert: CancelKeyPress event handler is attached
```

---

## Phase 1 Tests

### RPC Message Tests (Unit)

```
RpcMessage_SerializeRequest_RoundTrips
  Setup: request with method="Heartbeat", id=1, params={}
  Action: serialize to JSON, deserialize back
  Assert: all fields match

RpcMessage_SerializeResponse_WithResult_RoundTrips
  Setup: response with id=1, result={ok:true}
  Action: serialize, deserialize
  Assert: result contains "ok"

RpcMessage_SerializeError_RoundTrips
  Setup: error with code=-1, message="timeout"
  Action: serialize, deserialize
  Assert: error code and message preserved

RpcMessage_DeserializeInvalid_ThrowsClear
  Setup: "{not valid json"
  Assert: throws JsonException or similar with useful message
```

### Named Pipe IPC Tests (Unit — in-process pipe)

```
AgentServer_Heartbeat_ReturnsOk
  Setup: start AgentServer with MockAgent in-process
  Action: HarnessClient connects, sends Heartbeat
  Assert: response has ok=true

HarnessClient_Timeout_ThrowsTimeoutException
  Setup: start AgentServer with MockAgent that delays 5 seconds
  Config: HarnessClient timeout = 1 second
  Action: send Heartbeat
  Assert: throws TimeoutException

HarnessClient_Execute_PassesParams
  Setup: MockAgent records received params
  Action: send Execute("OpenFile", {path: "test.3dm"})
  Assert: MockAgent received action="OpenFile", params["path"]="test.3dm"

AgentServer_CaptureScreenshot_ReturnsMockPath
  Setup: MockAgent returns {filePath: "/tmp/test.png"}
  Action: send CaptureScreenshot
  Assert: response filePath == "/tmp/test.png"

HarnessClient_SequentialRequests_AllSucceed
  Setup: MockAgent responds immediately
  Action: send 5 sequential requests (Heartbeat, Execute, etc.)
  Assert: all 5 responses received correctly, IDs match

AgentServer_Shutdown_DisconnectsCleanly
  Setup: start server, connect client
  Action: cancel server's CancellationToken
  Assert: client detects disconnection (next request throws)
```

---

## Phase 2 Tests

### Input Event Tests (Unit)

```
InputEvent_Serialize_RoundTrips
  Setup: MouseMove event at vx=0.5, vy=0.3, t=100
  Action: serialize to JSON, deserialize
  Assert: all fields match

InputRecording_Serialize_PreservesMetadata
  Setup: recording with workload="pigment", viewportWidth=800
  Action: serialize, deserialize
  Assert: metadata intact, events intact

CoordinateNormalization_NormalizeDenormalize_RoundTrips
  Setup: viewport bounds = (100, 200, 800, 600), screen point = (500, 500)
  Action: normalize → (vx, vy), denormalize back → screen point
  Assert: result within ±1px of original

CoordinateNormalization_TopLeft_ReturnsZeroZero
  Setup: viewport bounds = (100, 200, 800, 600)
  Action: normalize (100, 200)
  Assert: vx=0.0, vy=0.0

CoordinateNormalization_BottomRight_ReturnsOneOne
  Setup: viewport bounds = (100, 200, 800, 600)
  Action: normalize (900, 800)
  Assert: vx=1.0, vy=1.0

InputRecording_DifferentViewportSize_ScalesCorrectly
  Setup: recorded at 800x600, replaying at 1024x768
  Action: denormalize vx=0.5, vy=0.5 at new size
  Assert: screen point is at center of new viewport (512, 384)
```

### ViewportLocator Tests (Unit/Integration boundary)

```
ViewportLocator_FindWindow_KnownTitle_ReturnsHandle
  Category: Integration (requires a window to exist)
  Setup: launch a simple app with known title
  Assert: FindWindow returns non-zero handle

ViewportLocator_FindWindow_BadTitle_ReturnsZero
  Assert: FindWindow("xyznonexistent") returns IntPtr.Zero

ViewportLocator_GetBounds_ValidWindow_ReturnsNonZeroRect
  Category: Integration
  Setup: find a known window
  Assert: bounds width > 0, height > 0
```

---

## Phase 3 Tests

### Pixel Diff Comparer Tests (Unit)

```
PixelDiffComparer_IdenticalImages_ReturnsZeroDiff
  Setup: two identical 100x100 red square images (generated in code)
  Assert: DiffPercentage == 0.0
  Assert: ChangedPixels == 0
  Assert: Passed == true (any tolerance)

PixelDiffComparer_SinglePixelDiff_ReturnsCorrectCount
  Setup: 100x100 image, change pixel (50,50) from red to blue
  Assert: ChangedPixels == 1
  Assert: DiffPercentage ≈ 0.0001 (1/10000)

PixelDiffComparer_TenPercentNoise_ReturnsApproxTenPercent
  Setup: 100x100 red image, randomly change 10% of pixels to blue
  Assert: DiffPercentage ≈ 0.10 (±0.02)

PixelDiffComparer_BelowThreshold_CountsAsSame
  Setup: 100x100 image, change pixel (50,50) from (255,0,0) to (253,0,0) — diff=2
  Config: colorThreshold = 3
  Assert: ChangedPixels == 0 (within threshold)

PixelDiffComparer_AboveThreshold_CountsAsDifferent
  Config: colorThreshold = 1
  Assert: ChangedPixels == 1

PixelDiffComparer_DimensionMismatch_ThrowsArgumentException
  Setup: 100x100 vs 200x200 images
  Assert: throws ArgumentException with message about dimensions

PixelDiffComparer_DiffImage_HighlightsChanges
  Setup: 10x10 image, change top-left pixel
  Assert: diff image pixel (0,0) is magenta, all others are transparent/dim

PixelDiffComparer_ToleranceGate_PassesWhenBelowTolerance
  Setup: 1% of pixels changed, tolerance = 0.02
  Assert: Passed == true

PixelDiffComparer_ToleranceGate_FailsWhenAboveTolerance
  Setup: 5% of pixels changed, tolerance = 0.02
  Assert: Passed == false
```

### SSIM Comparer Tests (Unit)

```
SsimComparer_IdenticalImages_ReturnsOne
  Setup: two identical 64x64 gradient images
  Assert: SSIM ≈ 1.0 (>0.999)

SsimComparer_CompletelyDifferent_ReturnsLow
  Setup: white 64x64 vs random noise 64x64
  Assert: SSIM < 0.3

SsimComparer_SlightShift_ReturnsHigh
  Setup: gradient image, shift by 1 pixel
  Assert: SSIM > 0.85
```

### Composite Builder Tests (Unit)

```
CompositeBuilder_ThreeCheckpoints_CorrectDimensions
  Setup: 3 checkpoint comparisons, each with 100x100 images
  Action: build composite
  Assert: composite width = 100*3 + gaps, height = (100 + labelHeight) * 3 + gaps

CompositeBuilder_ZeroCheckpoints_ReturnsNull
  Assert: returns null or empty result

CompositeBuilder_SingleCheckpoint_ProducesValidImage
  Setup: 1 checkpoint with 200x150 images
  Assert: composite is a valid PNG, wider than 600px (3 × 200 + gaps)

CompositeBuilder_LabelsIncludeStatus
  Setup: checkpoint with name "after_stroke", passed=true, diff=0.3%
  Assert: composite image contains text label (verify image is non-empty at label region)
```

---

## Phase 4 Tests

### Test Definition Tests (Unit)

```
TestDefinition_Parse_ValidJson_AllFieldsPopulated
  Setup: sample test JSON string
  Assert: Name, Workload, Setup, Recording, Checkpoints all non-null
  Assert: Checkpoints has expected count

TestDefinition_Parse_MissingName_ThrowsClearError
  Setup: JSON without "name" field
  Assert: throws with message containing "name"

TestDefinition_Parse_EmptyCheckpoints_IsValid
  Setup: JSON with empty checkpoints array
  Assert: no error, Checkpoints.Count == 0

WorkloadConfig_Parse_AllFieldsPopulated
  Setup: workload.json string
  Assert: Name, AppPath, AgentType, PipeName all populated
```

### Process Manager Tests (Unit)

```
ProcessManager_Track_KillAll_TerminatesProcess
  Setup: launch a dummy long-running process (e.g., `timeout /t 60`)
  Action: ProcessManager.Track(process), then ProcessManager.KillAll()
  Assert: process has exited

ProcessManager_KillAll_AlreadyExited_NoError
  Setup: launch process, let it exit naturally
  Action: KillAll()
  Assert: no exception
```

### Watchdog Tests (Unit)

```
Watchdog_HealthyAgent_NoEvent
  Setup: MockHarnessClient that always returns heartbeat ok
  Action: run watchdog for 5 seconds
  Assert: OnAppDead event never fires

Watchdog_UnresponsiveAgent_FiresDeadEvent
  Setup: MockHarnessClient that throws TimeoutException on heartbeat
  Action: run watchdog
  Assert: OnAppDead fires within ~8 seconds (3 misses × 2s interval + tolerance)

Watchdog_Cancellation_StopsCleanly
  Setup: start watchdog
  Action: cancel after 1 second
  Assert: watchdog task completes without error
```

---

## Phase 5 Tests

### Report Tests (Unit)

```
HtmlReportGenerator_SingleTest_ProducesValidHtml
  Setup: TestResult with 1 test, 2 checkpoints, pass
  Action: generate HTML
  Assert: contains "<html>", contains test name, contains "PASS"

HtmlReportGenerator_FailedTest_ShowsRed
  Setup: TestResult with failed test
  Assert: HTML contains failure indicator (red badge or similar)

JUnitReportGenerator_ProducesValidXml
  Setup: TestResult with 2 tests
  Action: generate JUnit XML
  Assert: valid XML, contains <testsuite> and <testcase> elements
  Assert: failed test has <failure> element
```

---

## Phase 6 Tests

### Rhino Agent Tests (Integration — requires Rhino)

```
RhinoAgent_Heartbeat_ReturnsOk
  Category: Integration
  Setup: load Canary.Agent.Rhino plugin in Rhino
  Action: connect HarnessClient, send Heartbeat
  Assert: ok == true

RhinoAgent_OpenFile_LoadsDocument
  Category: Integration
  Action: Execute("OpenFile", {path: "test_sphere.3dm"})
  Assert: success == true

RhinoAgent_CaptureScreenshot_SavesPng
  Category: Integration
  Action: CaptureScreenshot(800, 600, "/tmp/capture.png")
  Assert: file exists, is valid PNG, dimensions match

RhinoAgent_RunCommand_Executes
  Category: Integration
  Action: Execute("RunCommand", {command: "SelAll"})
  Assert: success == true
```

---

## Performance Benchmarks (Log in BUILD_LOG.md)

| Test | Target | Parameters |
|------|--------|------------|
| Pixel diff | < 100ms | 800×600 images |
| SSIM | < 500ms | 800×600 images |
| Composite build (5 checkpoints) | < 200ms | 800×600 images |
| IPC round-trip (heartbeat) | < 10ms | Named pipe, same machine |
| Recording deserialization | < 50ms | 5000-event recording |
| App launch → agent ready | < 30s | Rhino 8 cold start |

---

## Regression Test Matrix

After each phase, run ALL tests from all prior phases:

| Phase | Tests to Run |
|-------|-------------|
| 0 | Phase 0 |
| 1 | Phase 0 + 1 |
| 2 | Phase 0 + 1 + 2 |
| 3 | Phase 0 + 1 + 2 + 3 |
| 4 | Phase 0 + 1 + 2 + 3 + 4 |
| 5 | All |
| 6 | All + Rhino integration (manual) |
| 7 | All + documentation review |
