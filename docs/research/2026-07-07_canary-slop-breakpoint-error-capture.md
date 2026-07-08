# Canary + Slop: Breakpoint Dismissal & Error-Capture Research

> **Anchoring doc for R6.5 bug 0017/0018.** Written 2026-07-07 after the operator
> redirected the approach: (1) dismiss breakpoint dialogs automatically in the
> future, (2) use Slop's error-logging components (Log Hub / Log Tap / Crash Guard)
> to capture GH errors instead of an agent-side RuntimeMessages dump. This doc
> maps the current state of both systems so future work starts from grounded
> facts, not half-remembered context.

## 1. The two goals

**Goal A — Auto-dismiss "Grasshopper breakpoint" dialogs.** The breakpoint is
GH's catch-all modal dialog for exceptions that escape the solution try/catch
(see §2). It blocks the UI thread until dismissed. Automated Canary runs have
nobody to dismiss it → 180s timeout → Crashed status. We need the dismisser to
catch it.

**Goal B — Capture GH errors via Slop's logging components.** Instead of the
agent reaching into GH after the fact (the `GrasshopperGetDiagnosticDump`
approach that failed because the UI thread is blocked), use the Slop components
already wired into the test (`Log Hub`, `Log Tap`, `Crash Guard`) to capture
errors. **Open question: do these components actually catch GH runtime errors
(the conversion error from the breakpoint)? See §4.**

## 2. The "Grasshopper breakpoint" dialog — what it is

- **GH's generic catch-all exception dialog.** Not one error type — conversion
  errors, thread-crossing exceptions, undo-recording failures, plugin-load
  failures all produce the same dialog shape with different messages. David
  Rutten (GH author, McNeel forum Apr 2018): *"It's an exception thrown by
  Grasshopper, but in all likelihood not within a solution since all solutions
  are encased in try…catch blocks."*
- **Standard WinForms dialog:** title "Grasshopper breakpoint", a "Close"
  button (standard rectangular), a "Do not show this message again" checkbox,
  and a call-stack table. Confirmed via operator screenshots.
- **Fires during the GH solution**, not at GH load. The cpig-59 call stack
  shows `GH_Document.So...` → `Control.Invoke` → `RhinoApp.Wait()` →
  `RhinoAgent.HandleWaitForGrasshopperSolution`. The operator confirmed: "Pops
  up when slop trys to make the components."
- **Transient, not fatal.** When dismissed, the solution re-runs and
  **succeeds** — SlopSuccess=True, no runtime message balloon on the
  component, clean run (~45s). The conversion error fires on one pass; after
  dismissal GH re-solves and the component produces output. This means the
  test "passes" after manual dismissal — the assert (SlopSuccess="True")
  cannot detect the underlying type mismatch.
- **Suppression file:** `%APPDATA%\Grasshopper\grasshopper_ignorewarnings.xml`
  tracks dismissed warnings by ID. Operator confirmed "Do not show again"
  is flaky — not a reliable suppression mechanism.

## 3. Canary's PopupDismisser — current state

**Location:** `Canary/src/Canary.Agent.Rhino/RhinoAgent.cs`, method
`PopupDismisser` (line ~659), started from `CanaryRhinoPlugin.OnLoad` (line
~104) via `Task.Run(() => RhinoAgent.PopupDismisserPublic(_popupCts.Token))`.

**Mechanism:** Background thread, loops every 250ms. For each scan:
1. `EnumWindows` — enumerates **top-level windows only** owned by this process
   (`pid == ourPid` filter).
2. For each visible window with a title, checks the title against a keyword
   list (`"loading errors"`, `"Component Loader"`, `"Plug-in Error"`, etc.).
3. If matched, `PostMessage(hWnd, WM_KEYDOWN/WM_KEYUP, VK_RETURN)` — clicks
   the dialog's default button.
4. **Body-text fallback** for dialogs whose default button is wrong:
   `EnumChildWindows` to find specific buttons ("Skip All", "No") by text,
   `PostMessage(button, BM_CLICK)` to click them directly.

**What it catches (confirmed working):** GH "loading errors", "Component
Loader Errors", "Plug-in Error", "Component ID conflict" (Skip All),
"Grasshopper IO" (No). These fire at startup or on specific events.

**What it does NOT catch — the breakpoint (4 failed attempts this session):**
- Attempt 1: added "breakpoint" to keyword list → `VK_RETURN` didn't dismiss
  (the breakpoint's default button isn't Close).
- Attempt 2: routed breakpoint to the Close-button BM_CLICK path → still
  didn't catch.
- Attempts 3-5: added diagnostic logging (per-window, heartbeat, recursive
  tree dump) → **the log was never written, not even a heartbeat that writes
  unconditionally every scan cycle to a known-good path.**

**The narrowed finding (from operator report):** The operator saw
`[Canary] Agent listening on pipe` (line 95 of `OnLoad`) in the Rhino command
line but NOT `[Canary-OnLoad] starting popup dismisser...` (line 104, 9 lines
later). The plugin IS the fresh build (call stack in the breakpoint screenshot
contains `RhinoAgent.HandleWaitForGrasshopperSolution` — my code). So `OnLoad`
runs to line 95, then **does not reach line 104**. Between 95 and 104 there is
only `_popupCts = new CancellationTokenSource()` (cannot throw) and comments.

**Open question (cause #3):** Why does `OnLoad` not reach line 104? The
`RhinoApp.WriteLine` trace calls I added to settle this did NOT appear in the
command line — even though the "Agent listening" WriteLine (same method, same
file, 9 lines earlier) DID appear. Possibilities: (a) the dismisser
`Task.Run` throws synchronously in a way that aborts `OnLoad` before the
post-`Task.Run` WriteLine, (b) `RhinoApp.WriteLine` calls after a certain
point in `OnLoad` are suppressed/not flushed, (c) something else. **This is
unresolved and is the blocker for Goal A.** The operator's redirect to Goal B
may sidestep it entirely.

## 4. Slop's error-logging components — do they catch GH runtime errors?

**Source read:** `Slop/Components/LogHubComponent.cs`,
`LogTapComponent.cs`, `LogCrashGuardComponent.cs`, `Logging/LogManager.cs`.

### Log Hub (`51070A00-...-510DDEF00011`)
- **Hooks `doc.SolutionStart` + `doc.SolutionEnd`** events → marks solution
  boundaries in the log.
- **Collects `LogEntry` records from `LogManager.OnEntry`** (the event fired
  when `LogManager.Record()` is called).
- **Writes all log data to a file** with crash-safe flushing.
- **Does NOT subscribe to component `RuntimeMessages`.** It logs solution
  start/end markers + whatever `LogManager.Record()` is called with (from
  Log Tap). It does NOT sweep components for error/warning balloons.

### Log Tap (`51070A00-...-510DDEF00010`)
- **Pass-through wiretap.** Reads data trees on its input, logs a snapshot
  (type, branch/item count, summary) to `LogManager.Record()`, passes data
  through unchanged.
- **Logs DATA flowing through wires, not errors.** A `LogEntry` has
  `DataType`, `Summary`, `FullData` — no error field. `LogManager.RecordError`
  exists but Log Tap only calls it if the *logging itself* throws, not if the
  data represents an error.
- **Does NOT capture `RuntimeMessages`.**

### Crash Guard (`51070A00-...-510DDEF00012`)
- **The most relevant component.** Hooks `AppDomain.UnhandledException`,
  `ProcessExit`, `TaskScheduler.UnobservedTaskException`, and (optionally,
  verbose mode) `FirstChanceException`.
- **Solve tracer:** polls every 30ms during a solution for the component with
  `Phase == Computing` → writes `[solving] '{name}' ({type}) {guid}` to the
  trace file. If a native crash kills Rhino mid-solve, the last `[solving]`
  line identifies the culprit.
- **Writes a crash dump on `UnhandledException`** (exception type, message,
  stack trace, inner exceptions, recent log entries).
- **`FirstChanceException` (verbose mode) logs EVERY first-chance exception**
  to the trace file: `[first-chance] {ExceptionType}: {Message}`. Capped at
  200 entries.

### The verdict on Goal B

**The conversion error from the breakpoint is a GH-internal exception that
escapes the solution try/catch.** Whether Slop's components catch it depends
on which exception pipeline it flows through:

| Slop component | Catches the conversion error? | Why |
|---|---|---|
| Log Hub | **No** | Only logs solution markers + Log Tap data. No `RuntimeMessages` sweep. |
| Log Tap | **No** | Logs wire data, not errors. The error is in the component, not the wire. |
| Crash Guard (default) | **Maybe — via `UnhandledException`** | If the breakpoint exception is unhandled (escapes all catch blocks), `OnUnhandledException` fires and writes a crash dump. BUT: the breakpoint dialog IS a catch (GH catches it to show the dialog), so it may NOT be "unhandled" from the AppDomain's perspective. **Needs testing.** |
| Crash Guard (verbose) | **Likely yes — via `FirstChanceException`** | First-chance exceptions fire for ALL exceptions, including caught ones. The conversion error, even if GH catches it to show the breakpoint, would fire `FirstChanceException` first. `[first-chance] {Type}: {Message}` would land in the trace file. **This is the most promising lane.** |

**The test to run (not yet done):** Enable Crash Guard's verbose mode
(`Verbose=true`) in the cpig-59 Slop test, run it, dismiss the breakpoint
manually, then read the Slop trace file (on Desktop by default, or wherever
`DumpPath` points). If `[first-chance]` lines contain the conversion error
text → Goal B works via Crash Guard verbose. If not → the exception doesn't
flow through `FirstChanceException` and we need a different capture mechanism.

**What Log Hub / Log Tap / Crash Guard do NOT do (the gap):** None of them
sweep component `RuntimeMessages` after a solution. A component that fails
gracefully (sets an error balloon via `AddRuntimeMessage`) is invisible to all
three. This is a separate gap from the breakpoint — but relevant if we want
Slop tests to detect component-level errors generally.

## 5. Build/deploy chain — current state + the stale-path issue

### CPig.Grasshopper (.gha)
- **Build output:** `C:\Repos\CPig\CPig.Grasshopper\bin\net48\CPig.Grasshopper.gha`
- **GH loads from:** the `Assemblies:Folders` list in
  `%APPDATA%\Grasshopper\grasshopper_kernel.xml`. **Finding: the main CPig
  Grasshopper bin path is NOT in that list.** The list includes
  `C:\Repos\CPig\CPig.Kinematics\...` and `G:\My Drive\Builds\CPig` but NOT
  `C:\Repos\CPig\CPig.Grasshopper\bin\net48\`. **This means GH may be loading
  CPig from `G:\My Drive\Builds\CPig` (the ShipToDrive path), not the local
  build output.** A local `dotnet build` of CPig.Grasshopper would NOT be
  what Rhino loads unless `ShipToDrive` runs. **Needs verification.**

### CPig.Rhino (.rhp)
- **Build output:** `C:\Repos\CPig\build\rhino\CPig.Rhino.rhp` (csproj
  `<OutputPath>..\build\rhino\</OutputPath>`, `<TargetExt>.rhp`)
- **Has a `ShipToDrive` target** (Release only, mirrors Penumbra.Rhino) —
  ships to `G:\My Drive\Builds\CPig`.
- **Rhino loads .rhp from** `%APPDATA%\McNeel\Rhinoceros\8.0\Plug-ins\` —
  need to check whether CPig.Rhino auto-deploys there or only ships to G:.

### Canary.Agent.Rhino (.rhp)
- **Build output:** `C:\Repos\Canary\src\Canary.Agent.Rhino\bin\Release\net48\Canary.Agent.Rhino.rhp`
- **Auto-deploys to:** `%APPDATA%\McNeel\Rhinoceros\8.0\Plug-ins\Canary (B4E7C920-...)\`
  via a `DeployToRhino` MSBuild target (confirmed: build output says "Deployed
  Canary agent to ...").
- **Stale-binary symptom observed this session:** `.rhp` timestamp didn't
  update after `dotnet build --no-incremental` unless I `touch`ed the source
  first. The build "succeeded" but may have skipped the link step if inputs
  looked fresh. **Mitigation: `touch` source files before building, or clean
  the bin/obj dirs.**

### Canary.UI.Avalonia (the GUI)
- **Build output:** `C:\Repos\Canary\src\Canary.UI.Avalonia\bin\Release\net8.0-windows\`
- **Loads its OWN copy** of `Canary.Core.dll` + `Canary.Agent.dll` — building
  only `Canary.Harness` does NOT update the UI's copy. Must build
  `Canary.UI.Avalonia.csproj` after touching `Canary.Core` (e.g. TestResult.cs,
  TestRunner.cs).

### The hard rule (operator instruction)
**Always close Rhino + Grasshopper + Canary before building anything.** A
running Rhino holds the `.rhp` + `.gha` in memory; a fresh build deploys to
disk but Rhino loads the stale in-memory copy until restarted. A running
Canary.UI locks its DLLs (`MSB3021 file is locked by Canary.UI`). Kill all
three before any build.

## 6. Canary's information surfaces (how it gets info from Rhino/GH/Slop)

| Surface | Mechanism | What it captures | Limit |
|---|---|---|---|
| `GrasshopperGetPanelText` | Agent RPC → reads `GH_Panel.VolatileData`/`UserText` by nickname | Live panel text (SlopSuccess, SlopLog, Count, Report) | Only panels; not component errors |
| Asserts (`PanelEquals` etc.) | Calls `GrasshopperGetPanelText`, string-compares | Pass/fail vs expected panel text | Opaque — failure says "expected True got False", not why |
| Checkpoint screenshot | Captures Rhino viewport + optional fullscreen at `atTimeMs` | Visual state at a moment | Misses dialogs that appear later; fullscreen is a separate file |
| Telemetry (`telemetry.ndjson`) | Events from harness + Penumbra | Action log, Penumbra startup, command history | GH breakpoints emit NO telemetry event |
| Crash log (`%TEMP%\Canary\canary-crash.log`) | `VectoredExceptionHandler` + `AppDomain.UnhandledException` | Native fault code + managed exception details | Only fires on actual crashes; the breakpoint is NOT a crash (it's caught by GH) |
| `GrasshopperGetDiagnosticDump` (new, experimental) | Agent RPC → sweeps `RuntimeMessages` + all panels | Component error/warning/remark messages + panel text | **Never ran** — the UI thread is blocked by the breakpoint before the dump fires (Crashed status, not Failed) |

**The gap:** No Canary surface captures the breakpoint dialog's error text.
The dump was meant to fix this but can't run during a UI-thread block. Goal B
(Slop's Crash Guard verbose) is the alternative lane.

## 7. Open questions for the next session

1. **Does Crash Guard verbose (`FirstChanceException`) capture the breakpoint's
   conversion error?** Test: enable verbose in cpig-59 Slop test, run, dismiss
   breakpoint manually, read the Slop trace file. (Goal B validation.)
2. **Why does `OnLoad` not reach the dismisser-start line (104)?** The trace
   `RhinoApp.WriteLine` calls didn't appear. Is `RhinoApp.WriteLine` suppressed
   after a certain point in `OnLoad`? Does the `Task.Run` throw? (Goal A
   blocker.)
3. **Is GH loading CPig from `G:\My Drive\Builds\CPig` instead of the local
   build?** The main CPig Grasshopper bin path isn't in `Assemblies:Folders`.
   (Build-chain correctness.)
4. **Does CPig.Rhino.rhp auto-deploy to the Rhino Plug-ins path, or only
   ShipToDrive?** (Build-chain correctness.)

## 8. What was learned the hard way (process lessons, not findings)

- **The spiral:** 4 build-deploy-run cycles on the dismisser, each guessing at
  a fix, none reading the actual window or reporting back. Broken by the
  operator saying "hold on, don't build anything."
- **The check-in failure:** the operator sent 3 messages during the spiral,
  each with information that would have broken it; the agent acknowledged each
  and immediately launched the next build, never giving the operator a turn.
- **The stale-binary trap:** `dotnet build --no-incremental` can "succeed"
  without producing a new binary if MSBuild decides inputs are fresh. `touch`
  the source or clean bin/obj.
- **The "read the error" failure:** "checking your own instrumented output" is
  NOT reading the error. The error was the window on the operator's screen; the
  agent's logs were a model of the error, not the error. Extracted into the
  skill's "Behavioral rules (hard)" section + memory.