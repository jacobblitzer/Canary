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
