---
date: 2026-05-24
tags: [progress, canary, debug-overhaul]
status: in-progress
project: canary
component: full-surface
---

# Canary debug-overhaul — progress log

Implementation of the design proposal at `docs/plans/2026-05-24-canary-debug-overhaul.md`.
Driving prompt: `MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md`.

The audit + design landed in commits `790b77e` (Phase A surface inventory),
`104cb04` (Phase B prior-art), `9f0b3da` (Phase C design proposal). This log
covers the implementation of the design's nine C-sections via the prompt's ten
phases (Phase 0 pre-flight + precursor + 9 design phases).

## Phase 0 — Pre-flight (2026-05-24)

- Clean tree confirmed at HEAD `4993c53` (`chore: add .gitattributes for LF line-ending normalization`).
- Snapshot tag `pre-impl-debug-overhaul-2026-05-24` created at that SHA. This is
  the rollback anchor for the entire implementation; deletes after Phase 9 ships
  + pushes.
- Toolchain: .NET SDK 10.0.102 present; repo targets `net8.0-windows` (Core /
  Harness / UI), `net8.0;net48` (Agent), `net48` (Rhino plugin) per `CLAUDE.md`.
- Baseline build: `dotnet build Canary.sln` = 0 warnings, 0 errors.
- Baseline tests: `dotnet test --filter "Category=Unit"` = 107 Passed, 0 Failed,
  0 Skipped. `Category=Integration` count is 0 (the
  `Canary.Tests.Integration` csproj is scaffolded but empty — Integration tests
  start landing in Phase 1 per the prompt's per-phase commit blocks).
- Canon read: design doc (`docs/plans/2026-05-24-canary-debug-overhaul.md`),
  Phase A surface audit, Phase B prior-art, `CLAUDE.md`, `spec/SUPERVISOR.md`,
  `MultiVerse/STANDARD.md` §§ 7, 14, 16, 19, 22.

## Phase Precursor — CLI exit-code regression (bug 0007) (2026-05-24)

Per operator decision Q6 (locked decision §0.1) — the CLI exit-code regression
ships as its own commit before Phase 1, NOT bundled into Phase 1.

- **Bug:** `docs/bugs/0007-cli-exit-code-regression.md` (severity: high).
- **Fix shape:** `RunCommand.RunAsync` now returns `Task<int>`. New helper
  `internal static int ExitCodeFromSuiteResult(SuiteResult)` returning `0` when
  no failures (Failed + Crashed == 0), else `1`. `New` baseline status counts as
  pass — a first-run baseline creation isn't a failure. Every early-error path
  inside `RunAsync` (missing workload, mutually-exclusive flags, missing config,
  missing test JSON, suite not found, no tests discovered) returns `1`. The
  System.CommandLine handler closure assigns `ctx.ExitCode = await RunAsync(...)`
  so the int propagates out of the process.
- **Tests added:** `tests/Canary.Tests/Cli/RunCommandExitCodeTests.cs` — 8 unit
  tests covering the helper (no tests, all passed, one failed, one crashed, new
  baseline only) plus three integration-ish tests of `RunAsync` directly
  (missing workload name, mutually exclusive `--test` + `--suite`, nonexistent
  workload).
- **Verification:** `dotnet build Canary.sln` = 0/0; `dotnet test --filter
  "Category=Unit"` = 115 Passed (was 107).
- **Docs:** `CHANGELOG.md` `[Unreleased] → Fixed`; `BUILD_LOG.md` new section.

## Phase 1 — C3 Non-headless enforcement (2026-05-24)

Implements `STANDARD.md` §16 locked rule 8 + operator decision Q5
(single-instance pipe forwarding) + Q6 follow-through (the precursor having
already shipped).

- **Files added:**
  - `src/Canary.Core/Cli/AutoRunArgs.cs` — POCO with argv + JSON round-trip
    (source-gen serializer context for AOT-readiness).
  - `src/Canary.Harness/UiLocator.cs` — search-order helper for `Canary.UI.exe`.
  - `src/Canary.UI/SingleInstancePipeServer.cs` — async pipe loop + one-shot
    client; raises `AutoRunRequested` on the loop thread; Program.cs marshals
    to the UI thread via `BeginInvoke`.
  - 3 test files: `AutoRunArgsTests.cs`, `HeadlessFlagTests.cs`,
    `SingleInstancePipeTests.cs` (Integration).
- **Files modified:**
  - `src/Canary.Harness/Cli/RunCommand.cs` — `--headless` flag (boolean,
    default false; `--quiet` implies). New `TryLaunchUi` helper spawns the UI
    via `Process.Start(UseShellExecute=true)` and returns true on success;
    handler closure exits 0 on successful spawn.
  - `src/Canary.UI/Program.cs` — `static void Main(string[] args)`; acquires
    `Global\Canary.UI.SingleInstance` mutex; forwards args via pipe + exits
    if mutex held; starts pipe server + handles initial auto-run on first
    instance.
  - `src/Canary.UI/MainForm.cs` — `AutoRunAsync(AutoRunArgs)` method,
    `FindAutoRunNode` (workload/test/suite tree search), `ParseAutoRunMode`,
    new `_autoRunModeOverride` field. `OnRunTests` consumes + clears the
    override and passes it to `TestRunnerPanel.RunAsync`.
  - `src/Canary.UI/Controls/TestRunnerPanel.cs` — `RunAsync` gains optional
    `modeOverride` parameter (default `PixelDiff`); passes to
    `TestRunner.ModeOverride`.
  - `tests/Canary.Tests.Integration/Canary.Tests.Integration.csproj` — added
    `Canary.UI` project reference (needed by the pipe test).
- **Recursion guard verified:** `Canary.UI` calls `TestRunner` directly; no
  shell-out to `canary run`. (Grep on `Process.Start.*canary\.exe` in
  `src/Canary.UI` returns zero hits.)
- **Verification:**
  - `dotnet build Canary.sln` = 0/0.
  - `dotnet test --filter "Category=Unit"` = 128 Passed (was 115; +13 new).
  - `dotnet test --filter "Category=Integration"` = 2 Passed (was 0; +2 new).
  - Smoke: `canary run --headless` (no workload) exits 1; `canary run --help`
    advertises `--headless`.
  - Hard-rule-8 smoke (VLM + pixel-diff still functional): not run on this
    machine — Penumbra Vite + Qualia Vite require operator to start their dev
    servers. Operator verifies non-headless `canary run --workload qualia
    --test main-pencil` triggers UI auto-launch.
- **Snapshot tag:** not used (modified ~6 files, no new csproj — under the
  >5-files-or-new-csproj threshold of §0.2 rule 2).
- **Commit shape:** three commits per the prompt's §3 suggested split —
  feat(cli), feat(ui), docs.

## Phase 2 — C1 universal telemetry envelope (2026-05-24)

L-effort phase. Producer side of the C1 envelope: every workload agent now
writes a uniform `TelemetryRecord` stream to a per-suite NDJSON file. Phase
3 will move it under `runs/<timestamp>/` and consume it for REPORT.md.

- **Snapshot tag:** `pre-impl-phase2-2026-05-24` created at start; deleted on success.
- **New namespace `Canary.Telemetry`:** `TelemetryKind`, `TelemetryRecord` (POCO +
  shared `JsonSerializerOptions` — camelCase, null-omitted, ISO 8601, no
  indent for NDJSON), `ITelemetrySink` + `NullTelemetrySink` +
  `CompositeTelemetrySink`, `NdjsonFileSink` (thread-safe one-line-per-record
  with 500 KB truncation), `EventStreamSink` (in-memory fan-out),
  `ITelemetryAware` (agent registration interface).
- **CDP extension:** `CdpClient.Subscribe(method, handler)` returning
  `IDisposable`. ReadLoopAsync now fans event payloads to both the historic
  one-shot waiters AND the new continuous subscribers.
- **Shared producer (`CdpTelemetryStream`):** enables Runtime/Console/Log/
  Network domains; subscribes to Runtime.consoleAPICalled +
  Log.entryAdded + Network.requestWillBeSent/responseReceived/loadingFailed;
  emits Console + Network kind records. Network records carry durationMs
  via per-request Stopwatch tracking.
- **Penumbra + Qualia bridge agents:** implement `ITelemetryAware`; both
  init paths call `CdpTelemetryStream.EnableAndSubscribeAsync` after the
  existing Page+Runtime enables; subscription handle disposed on agent
  `Dispose`.
- **TestRunner:** new `TelemetrySink` property (default Null).
- **RunCommand:** instantiates per-suite `NdjsonFileSink` at
  `workloads/<w>/results/[<suite>/]telemetry.ndjson`; both bridge-suite
  paths register the sink on the agent before `InitializeAsync`.
- **ITestProgressEvents.OnTelemetry:** default-method no-op; Phase 7 will
  override.
- **Tests:** 12 new unit tests (5 serialization, 7 sink behaviors including
  8-writer concurrency without torn writes). Live CDP integration tests
  deferred to operator-side (needs Chrome + Vite).
- **Verification:** build 0/0; Unit 128 → 140; Integration 2 (unchanged);
  CLI smoke unchanged.
- **Deferrals (documented):** Rhino-side console intercept (no clean
  RhinoCommon 8 hook in scope; queued for v2), InputReplayer event records
  (cross-cuts Phase 7), ProcessManager.Track agent-action records
  (deferred to Phase 6 + SpawnRegistry), live CDP integration tests
  (operator runs).
- **Commit shape (per prompt §4):** core types + sink, CDP+helper, Penumbra
  agent, Qualia agent, TestRunner+RunCommand wiring, ITestProgressEvents
  extension, tests, docs — bundled into a smaller set of logical commits
  to keep churn legible.

## Phase 3 — C2 REPORT.md + per-run dir layout (2026-05-24)

M-effort phase. Each test run gets its own `runs/<timestamp>/` dir with
`result.json` + `REPORT.md`. Baselines/candidates/diffs stay flat at
the test level for this phase (overwriting per run) — keeps refactor
scope manageable; future phase can deepen if PastRuns image history
proves needed.

- **Snapshot tag:** `pre-impl-phase3-2026-05-24` created; deleted on success.
- **MarkdownReportGenerator:** new `Canary.Reporting.MarkdownReportGenerator`
  generates the per-§C2 REPORT.md (header / verdict / checkpoints table /
  errors / VLM / files). Relative links to images via `../<dir>/`. Optional
  telemetry footer link to `../../../telemetry.ndjson` (per-suite location
  from Phase 2).
- **TestRunner.SavePerRunArtifactsAsync:** writes result.json + REPORT.md to
  `<testDir>/runs/<yyyyMMdd-HHmmss-xxxx>/`. Called at end of both
  RunTestAsync (HarnessClient) and RunAgentTestAsync (CDP bridge). Failures
  logged but do NOT flip test verdict. CLI parity — previously CLI never
  wrote per-test result.json (only UI did).
- **ResultsHistory dual-shape scan:** recursive walk picks up both
  `<test>/result.json` (legacy) and `<test>/runs/<timestamp>/result.json`
  (new). No dedup at this layer.
- **ResultRetention.PurgeOlderThan:** new helper in `Canary.Maintenance`;
  walks runs/<timestamp>/ dirs and deletes those older than threshold
  (default 14 days, matches STANDARD.md §16). Returns PurgeReport. Not
  auto-wired into TestRunner — operator decides cadence.
- **TestRunnerPanel cleanup:** removed per-test result.json save loop —
  TestRunner owns it now.
- **Tests:** 15 new unit tests (8 MarkdownReportGenerator, 3 ResultsHistory
  dual-shape, 4 ResultRetention).
- **Verification:** build 0/0; Unit 140 → 155; Integration 2 unchanged;
  CLI smoke unchanged.
- **Deferred:** moving candidates/diffs/composite into per-run dirs
  (substantial refactor; PastRuns can revisit), per-test telemetry slicing
  (boundaries ambiguous in shared-suite mode), auto-wiring retention
  (operator decides cadence).
