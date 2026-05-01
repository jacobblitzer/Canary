# SUPERVISOR.md — Canary Build Orchestrator

## Purpose
This document is the **single source of truth** for validating every phase, checkpoint, and deliverable in the Canary project. Claude Code must read this file **before starting any phase** and **after completing any checkpoint** to verify correctness, catch regressions, and decide the next action.

---

## How to Use This File

### Before Starting a Phase
1. Read `PHASES.md` for the current phase requirements
2. Read this file's **Phase Gate Checklist** for the target phase
3. Read `ARCHITECTURE.md` for relevant system design constraints
4. Read `TESTS.md` for the unit tests that must pass at the gate
5. Only then begin coding

### After Completing a Checkpoint
1. Run all tests specified in the checkpoint's test list
2. Compare actual outputs against the **Expected Artifacts** listed below
3. Check for **Dependency Violations** (see section below)
4. Check for **Regression** — do all prior phase tests still pass?
5. Log the result in `BUILD_LOG.md` (create if it doesn't exist)
6. If any check fails: fix before advancing. Do not skip.

### If Something Looks Wrong
1. Check the **Known Pitfalls** section for the current phase
2. Check `ARCHITECTURE.md` for constraint violations
3. Check whether a NuGet dependency version changed
4. If the issue is ambiguous, leave a `// SUPERVISOR_FLAG: [description]` comment in the code and note it in `BUILD_LOG.md`

---

## Global Constraints (Apply to ALL Phases)

### Project Identity
- **Name**: Canary
- **Harness Namespace**: `Canary`
- **Agent Interface Namespace**: `Canary.Agent`
- **Rhino Agent Namespace**: `Canary.Agent.Rhino`
- **Language**: C# / .NET 8.0 (harness + shared libs), .NET Framework 4.8 (Rhino agent plugin)
- **Build System**: SDK-style .csproj, solution file `Canary.sln`
- **Output**: Console application (`canary.exe`) + class libraries + Rhino plugin (.rhp)

### Dependency Rules
- **WindowsInput** (`WindowsInput` NuGet) — for SendInput mouse/keyboard simulation
- **SixLabors.ImageSharp** — cross-platform image loading, comparison, compositing (no System.Drawing dependency in harness)
- **System.IO.Pipes** — built-in .NET named pipes (no external IPC library)
- **Newtonsoft.Json** or **System.Text.Json** — for JSON-RPC protocol messages
- **RhinoCommon** (NuGet, Rhino agent project only) — never in the harness
- **No other external dependencies** without explicit justification logged in BUILD_LOG.md

### Code Quality Rules
- All public methods must have XML doc comments
- No blocking calls on the main thread without a timeout
- All IPC calls must have a configurable timeout (default 10 seconds)
- The harness must always respond to `Ctrl+C` — register `Console.CancelKeyPress` in `Program.cs`
- Every console output line must include a timestamp prefix
- Every status output must include: `Press Ctrl+C to abort`

### Safety Rules
- The harness process must be able to `Process.Kill()` any application it launched
- The watchdog must kill unresponsive applications after 3 missed heartbeats (6 seconds)
- Input replay must stop immediately on Ctrl+C — no queued events after cancellation
- Never replay input to a window that isn't the expected target (check window title before each event batch)

### File Organization
```
Canary/
├── Canary.sln
├── CLAUDE.md
├── README.md
├── BUILD_LOG.md
├── spec/
│   ├── SUPERVISOR.md
│   ├── ARCHITECTURE.md
│   ├── PHASES.md
│   └── TESTS.md
│
├── src/
│   ├── Canary.Core/                   # Shared library — extracted Phase 8
│   │   ├── Canary.Core.csproj         # net8.0-windows, ImageSharp
│   │   ├── ITestLogger.cs             # Logging abstraction
│   │   ├── Config/
│   │   │   ├── TestDefinition.cs      # Test JSON deserialization (incl. TestAction, TestAssert)
│   │   │   └── WorkloadConfig.cs
│   │   ├── Comparison/
│   │   │   ├── PixelDiffComparer.cs
│   │   │   ├── SsimComparer.cs
│   │   │   ├── ComparisonResult.cs
│   │   │   └── CompositeBuilder.cs
│   │   ├── Input/
│   │   │   ├── InputRecorder.cs
│   │   │   ├── InputReplayer.cs
│   │   │   ├── InputEvent.cs
│   │   │   └── ViewportLocator.cs
│   │   ├── Orchestration/
│   │   │   ├── TestRunner.cs          # Runs actions[], checkpoints, asserts[]
│   │   │   ├── TestResult.cs
│   │   │   ├── TestDiscovery.cs
│   │   │   ├── BaselineManager.cs
│   │   │   ├── AppLauncher.cs
│   │   │   ├── Watchdog.cs
│   │   │   └── ProcessManager.cs
│   │   └── Reporting/
│   │       ├── HtmlReportGenerator.cs
│   │       └── JUnitReportGenerator.cs
│   │
│   ├── Canary.Harness/               # Console app — thin CLI shell
│   │   ├── Canary.Harness.csproj     # References Canary.Core
│   │   ├── Program.cs                # CLI entry point, Ctrl+C handler
│   │   ├── ConsoleTestLogger.cs      # ITestLogger → console
│   │   └── Cli/
│   │       ├── RunCommand.cs
│   │       ├── RecordCommand.cs
│   │       ├── ApproveCommand.cs
│   │       └── ReportCommand.cs
│   │
│   ├── Canary.UI/                     # WinForms GUI — Phase 9–12
│   │   ├── Canary.UI.csproj          # WinExe, net8.0-windows
│   │   ├── Program.cs
│   │   ├── MainForm.cs
│   │   ├── Services/
│   │   │   └── WorkloadExplorer.cs
│   │   ├── Controls/
│   │   │   ├── ResultsViewerControl.cs
│   │   │   ├── TestEditorControl.cs
│   │   │   ├── WorkloadEditorControl.cs
│   │   │   ├── TestRunnerPanel.cs
│   │   │   └── RecordingPanel.cs
│   │   └── GuiTestLogger.cs
│   │
│   ├── Canary.Agent/                  # Shared library — agent interface + IPC
│   │   ├── Canary.Agent.csproj       # net8.0;net48 multi-target
│   │   ├── ICanaryAgent.cs
│   │   ├── AgentServer.cs
│   │   ├── HarnessClient.cs
│   │   └── Protocol/
│   │       ├── RpcMessage.cs
│   │       ├── RpcMethods.cs
│   │       └── ScreenshotResult.cs
│   │
│   └── Canary.Agent.Rhino/           # Rhino-specific agent plugin (.rhp)
│       ├── Canary.Agent.Rhino.csproj  # net48, TargetExt=.rhp
│       ├── CanaryRhinoPlugin.cs
│       ├── RhinoAgent.cs             # GH actions: SetSlider, SetToggle, SetPanelText, GetPanelText
│       └── RhinoScreenCapture.cs
│
├── scripts/
│   └── cpig-test-from-slop.ps1       # Generates cpig-NN-slug.json from Slop JSONs
│
├── tests/
│   ├── Canary.Tests/
│   │   └── ...                        # 72 unit tests
│   └── Canary.Tests.Integration/
│       └── ...
│
├── workloads/
│   ├── rhino/
│   │   ├── workload.json
│   │   ├── fixtures/
│   │   │   ├── cpig_slop_loader.gh            # CPig test loader (Phase 13)
│   │   │   └── cpig_slop_loader_generator.json
│   │   ├── tests/
│   │   │   ├── cpig-00-smoke-ping.json        # 17 CPig tests (cpig-00 through cpig-16)
│   │   │   ├── ...
│   │   │   ├── cpig-16-field-evaluate.json
│   │   │   ├── smoke-test.json
│   │   │   └── salimon.json
│   │   └── results/                   # gitignored — populated by test runs
│   ├── pigment/
│   │   └── ...
│   ├── penumbra/
│   │   └── ...
│   └── qualia/
│       └── ...
│
└── .gitignore
```

---

## Phase Gate Checklists

### Phase 0: Solution Scaffold + CLI Shell
**Expected Artifacts:**
- [ ] `Canary.sln` builds with `dotnet build` — 0 errors, 0 warnings
- [ ] `canary.exe` runs and prints help text
- [ ] `canary run --help` prints usage for the run command
- [ ] `Ctrl+C` during execution exits cleanly (verified by test)
- [ ] `Canary.Tests` project with a trivial passing test
- [ ] `.gitignore` excludes bin/obj/results
- [ ] `README.md` with project overview

**Dependency Check:**
- .NET 8 SDK resolves
- No NuGet errors

**Known Pitfalls:**
- Use `System.CommandLine` NuGet for CLI parsing — it handles `--help`, verbs, and options cleanly
- Register `Console.CancelKeyPress` FIRST in `Program.Main` before any other setup
- Set `Console.TreatControlCAsInput = false` (the default, but be explicit)

---

### Phase 1: Named Pipe IPC + Agent Protocol
**Expected Artifacts:**
- [ ] `Canary.Agent.csproj` builds as a `net8.0` class library (also multi-targets `net48` for Rhino)
- [ ] `ICanaryAgent` interface defined with methods: `Execute`, `CaptureScreenshot`, `Heartbeat`, `Abort`
- [ ] `AgentServer` listens on a named pipe, deserializes JSON-RPC, dispatches to `ICanaryAgent`
- [ ] `HarnessClient` connects to the named pipe, sends JSON-RPC requests, awaits responses
- [ ] Round-trip test: harness sends `Heartbeat`, agent responds `{ok: true}` — in-process, no real app
- [ ] Timeout test: harness sends request, agent doesn't respond, harness times out after configured duration
- [ ] Pipe name format: `canary-{workloadName}-{pid}`

**Dependency Check:**
- No external IPC library — `System.IO.Pipes` only

**Known Pitfalls:**
- Named pipe server must be started BEFORE the client connects — race condition if agent starts after harness connects
- Use `NamedPipeServerStream` with `PipeDirection.InOut` for duplex
- Always use `async/await` for pipe read/write — blocking calls will deadlock with the watchdog
- JSON-RPC messages delimited by newlines (`\n`) — one message per line, read with `StreamReader.ReadLineAsync`

---

### Phase 2: Input Recorder + Replayer
**Expected Artifacts:**
- [ ] `InputRecorder` hooks global mouse/keyboard via `SetWindowsHookEx` (low-level mouse + keyboard hooks)
- [ ] Records events as viewport-relative normalized coordinates `(vx, vy)` in `[0,1]` range
- [ ] Saves recording to JSON file with timestamp, event type, coordinates, key codes
- [ ] `InputReplayer` reads JSON, converts normalized coords to screen coords, injects via `SendInput`
- [ ] `ViewportLocator` finds a target window by title/class, gets its screen bounds, normalizes/denormalizes coordinates
- [ ] Recording captures only events targeted at the specified window (not global activity)
- [ ] Replay timing preserves original inter-event delays (configurable speed multiplier)

**Regression Check:**
- Phase 0+1 tests still pass

**Known Pitfalls:**
- `SetWindowsHookEx` for `WH_MOUSE_LL` and `WH_KEYBOARD_LL` requires a message pump — run hooks on an STA thread with `Application.Run`
- The hook thread must NOT be the main console thread — spawn a dedicated thread
- `SendInput` coordinates use absolute screen position in 65535-normalized units, not pixels — conversion: `x_norm = x_pixel * 65535 / Screen.PrimaryScreen.Bounds.Width`
- Window may be partially obscured — check `IsWindowVisible` and `GetForegroundWindow` before replaying
- Recording file must store the viewport dimensions at record time for proper scaling

---

### Phase 3: Screenshot Comparison Engine
**Expected Artifacts:**
- [ ] `PixelDiffComparer` loads two images, computes per-pixel difference, returns percentage of pixels exceeding threshold
- [ ] Tolerance is configurable per-checkpoint (default 0.02 = 2%)
- [ ] Diff image generated with changed pixels highlighted in magenta
- [ ] `SsimComparer` computes SSIM score between two images (secondary metric)
- [ ] Images must be same dimensions — comparison throws a clear error if they differ
- [ ] `CompositeBuilder` stitches `[baseline | candidate | diff]` side-by-side into a single image
- [ ] Multiple checkpoints stacked vertically into one composite per test

**Regression Check:**
- All Phase 0+1+2 tests pass

**Performance Gate:**
- Pixel diff of two 800×600 images: < 100ms
- SSIM of two 800×600 images: < 500ms
- Composite build for 5 checkpoints: < 200ms

**Known Pitfalls:**
- ImageSharp loads images as `Image<Rgba32>` — access pixels via indexer `image[x, y]`
- Antialiasing causes 1-2 pixel border noise on edges — the tolerance threshold handles this
- SSIM requires grayscale conversion first — convert using luminance formula
- Composite image can get very tall with many checkpoints — cap at 10 per composite, paginate beyond that

---

### Phase 4: Test Runner + Orchestrator
**Expected Artifacts:**
- [ ] `TestRunner` reads a test definition JSON, orchestrates: launch app → wait for agent → setup → replay → capture → compare → report
- [ ] `AppLauncher` starts the target application process, waits for agent pipe connection
- [ ] `Watchdog` pings agent heartbeat every 2 seconds, kills process after 3 misses
- [ ] `ProcessManager` tracks all child processes, kills all on `Ctrl+C`
- [ ] Test definition JSON schema: `{ name, workload, setup, steps[], checkpoints[] }`
- [ ] `canary run --workload pigment` discovers all tests in `workloads/pigment/tests/` and runs them
- [ ] `canary run --test sculpt-undo-test` runs a single test
- [ ] `canary approve --test sculpt-undo-test` promotes candidate screenshots to baselines
- [ ] On first run (no baselines exist): all tests "fail" with message "No baseline — run `canary approve` to establish baselines"
- [ ] Exit codes: 0 = all pass, 1 = failures, 2 = crash/error

**Regression Check:**
- All prior phase tests pass
- IPC round-trip still works
- Comparison engine unaffected

**Known Pitfalls:**
- App launch is async — the harness must poll for the named pipe to become available (retry with backoff, timeout after 30 seconds)
- If the app crashes during test, the harness must not hang — the watchdog detects the missed heartbeat
- Test result JSON should be written atomically (write to temp, rename) to avoid partial files on crash
- The `approve` command must only promote screenshots from the most recent test run

---

### Phase 5: HTML Report + UI Polish
**Expected Artifacts:**
- [ ] `HtmlReportGenerator` produces a single self-contained HTML file showing all test results
- [ ] Report shows: test name, pass/fail, composite image (inline base64), diff percentage, SSIM score
- [ ] Failed tests highlighted in red, passed in green
- [ ] `canary report` opens the report in the default browser
- [ ] Console output during test run shows live progress: `[12:34:56] ✓ sculpt-undo-test (0.3% diff) — PASS   Press Ctrl+C to abort`
- [ ] Summary at end: `Results: 14 passed, 1 failed, 0 crashed. Report: results/report.html`

**Regression Check:**
- Full test suite passes
- End-to-end: record → run → compare → report pipeline works

**Known Pitfalls:**
- HTML report with embedded base64 images can be large — compress PNGs before embedding
- Use a simple HTML template string, not a templating engine — minimize dependencies
- Console output must work in both regular terminal and CI environments (no ANSI escape codes unless terminal supports them)

---

### Phase 6: Rhino Workload Agent
**Expected Artifacts:**
- [ ] `Canary.Agent.Rhino` builds as a Rhino plugin (.rhp) targeting `net48`
- [ ] Plugin starts the `AgentServer` on load, listening on `canary-rhino-{pid}` pipe
- [ ] `RhinoAgent.Execute("OpenFile", {"path": "..."})` opens a .3dm file
- [ ] `RhinoAgent.Execute("SetViewport", {"width": 800, "height": 600, "projection": "Perspective"})` configures viewport
- [ ] `RhinoAgent.Execute("RunCommand", {"command": "SelAll"})` runs a Rhino command
- [ ] `RhinoAgent.CaptureScreenshot(settings)` calls `ViewCapture.CaptureToBitmap`, saves PNG, returns path
- [ ] `RhinoAgent.Heartbeat()` returns `{ok: true, meshCount: N, ...}` — basic state info
- [ ] End-to-end: harness launches Rhino → agent connects → harness sends setup commands → replay input → capture screenshot → compare

**Regression Check:**
- All harness-side tests pass
- Agent protocol tests pass with mock agent
- Rhino plugin compiles and loads

**Known Pitfalls:**
- Rhino 8 plugins use `net48` while the harness uses `net8.0` — the `Canary.Agent` library must multi-target
- `ViewCapture.CaptureToBitmap` requires a real display — this phase cannot be tested headless
- The agent must start its pipe server on a background thread — do NOT block Rhino's UI thread
- `RhinoApp.RunScript` is synchronous — wrap in a dispatcher if needed
- Plugin GUID must be unique (not reused from Pigment)

---

### Phase 13: CPig Regression Workload
**Expected Artifacts:**
- [ ] `RhinoAgent.cs` handles `GrasshopperSetToggle`, `GrasshopperSetPanelText`, `GrasshopperGetPanelText`
- [ ] `TestDefinition.cs` deserializes `actions[]` (`TestAction`) and `asserts[]` (`TestAssert`)
- [ ] `TestRunner.cs` executes `actions[]` before checkpoints, evaluates `asserts[]` after each checkpoint
- [ ] Three assert types implemented: `PanelEquals`, `PanelContains`, `PanelDoesNotContain`
- [ ] `workloads/rhino/fixtures/cpig_slop_loader.gh` built with Slop, JsonPath, Build, CrashGuard, LogHub, 3 output panels
- [ ] `scripts/cpig-test-from-slop.ps1` generates test JSONs from Slop definitions
- [ ] 17 `cpig-*` test JSONs committed under `workloads/rhino/tests/`
- [ ] All 17 tests run end-to-end without harness crash
- [ ] `cpig-00-smoke-ping` passes pixel diff
- [ ] Crash-related tests (`cpig-07`, `cpig-09`, `cpig-16`) confirm CPig mitigations hold (Watchdog does NOT fire)
- [ ] Baselines approved and committed

**Regression Check:**
- All Phase 0–12 tests still pass
- Existing rhino workload tests (`smoke-test`, `salimon`) unaffected

**Known Pitfalls:**
- CPig `cpig_native.dll` must be loadable by Rhino on the test machine — verify DLL path in `NativeLibrary.cs`
- Slop JSON paths in test definitions use absolute paths (`C:/Repos/CPig/...`) — must match the machine's layout
- New machine environments may have different display drivers, affecting pixel diffs — re-approve baselines when switching machines
- Three CPig components (Field Evaluate, Mesh Shell, Alpha Wrap) have mitigated-but-not-root-caused native crashes — if Watchdog fires during these tests, capture `cpig_debug.log` and `%LOCALAPPDATA%\CPig\trace.log` before investigating

---

## Dependency Matrix

| Component | Depends On | Phase Introduced |
|-----------|-----------|-----------------|
| Program.cs (CLI) | System.CommandLine | 0 |
| Canary.Agent (IPC lib) | System.IO.Pipes, System.Text.Json | 1 |
| HarnessClient | Canary.Agent | 1 |
| AgentServer | Canary.Agent | 1 |
| InputRecorder | WindowsInput, User32 hooks | 2 |
| InputReplayer | WindowsInput | 2 |
| ViewportLocator | User32 P/Invoke | 2 |
| PixelDiffComparer | SixLabors.ImageSharp | 3 |
| SsimComparer | SixLabors.ImageSharp | 3 |
| CompositeBuilder | SixLabors.ImageSharp | 3 |
| TestRunner | HarnessClient, InputReplayer, Comparers | 4 |
| AppLauncher | System.Diagnostics.Process | 4 |
| Watchdog | HarnessClient | 4 |
| HtmlReportGenerator | — (string templates) | 5 |
| CanaryRhinoPlugin | RhinoCommon, Canary.Agent | 6 |
| RhinoAgent | RhinoCommon, Canary.Agent | 6 |
| Canary.Core | SixLabors.ImageSharp, Canary.Agent | 8 |
| Canary.UI (WinForms) | Canary.Core, Canary.Agent | 9 |
| BaselineManager | Canary.Core | 8 |
| TestAction / TestAssert | Canary.Core.Config | 13 |
| cpig_slop_loader.gh | Slop, CPig, Grasshopper | 13 |
| cpig-test-from-slop.ps1 | CPig/research/slop_tests/ | 13 |

---

## Regression Test Protocol

After ANY code change, run in this order:
1. `dotnet build Canary.sln` — must succeed with 0 errors, 0 warnings
2. Unit tests: `dotnet test --filter "Category=Unit"` — all green
3. If the change touched IPC: run the named pipe round-trip test
4. If the change touched comparison: run diff tests with known test images
5. If the change touched input replay: manual test (record short macro, replay, verify mouse moves)
6. If the change touched the Rhino agent: load plugin in Rhino, verify pipe connects

---

## Error Classification

| Severity | Description | Action |
|----------|------------|--------|
| **BLOCKER** | Harness crashes on startup, Ctrl+C doesn't work, child processes orphaned | Fix immediately, do not proceed |
| **CRITICAL** | IPC disconnects, screenshots blank, comparison produces wrong results | Fix before advancing to next phase |
| **MAJOR** | Performance below gate threshold, report missing data, replay timing off | Fix before phase gate |
| **MINOR** | Console output formatting, report cosmetics, non-critical UX issue | Log in BUILD_LOG.md, fix in Phase 5 |

---

## BUILD_LOG.md Template

```markdown
# Build Log — Canary

## Phase [N]: [Name]
### Checkpoint [N.M]: [Description]
- **Date**: YYYY-MM-DD
- **Status**: PASS / FAIL / PARTIAL
- **Tests Run**: [list]
- **Tests Passed**: [count]
- **Tests Failed**: [count + names]
- **Issues Found**: [description]
- **Resolution**: [what was done]
- **SUPERVISOR_FLAGS**: [any flagged items]
```
