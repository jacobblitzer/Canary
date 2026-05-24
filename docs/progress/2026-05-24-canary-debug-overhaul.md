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
