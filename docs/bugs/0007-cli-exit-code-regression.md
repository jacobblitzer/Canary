---
date: 2026-05-24
tags: [bug, cli, regression]
status: open
project: canary
severity: high
component: cli
fix-commit: ""
---

# 0007 — CLI `canary run` always exits 0 even when tests fail

## Summary
`src/Canary.Harness/Cli/RunCommand.cs` `RunAsync` is void-returning (`Task` with no result). The System.CommandLine handler closure awaits it and discards any signal. As a result `canary run` returns exit code 0 even when one or more tests fail or crash, and even when the workload config is missing. This silently breaks CI consumers — any pipeline relying on `canary run`'s exit code currently gets false positives.

This is a regression against `spec/PHASES.md` Phase 4 which specifies exit code 0 for all-pass, non-zero for any-fail.

## Environment
- OS: Windows 11
- Toolchain / SDK: .NET 10 SDK building `net8.0-windows`
- Host app: N/A (CLI surface)
- Project version: pre-debug-overhaul baseline (HEAD = `4993c53` at filing)

## Steps to Reproduce
1. Pick any test that currently passes (e.g. `canary run --workload qualia --test main-pencil --headless`).
2. Edit the test fixture or baseline to force a failure (or simply request a non-existent test: `canary run --workload qualia --test nonexistent-test --headless`).
3. Observe `echo $?` (bash) or `echo $LASTEXITCODE` (PowerShell) — value is `0`.

Expected: `1` on any failure, `0` on all-pass.

## Root Cause
- `RunAsync` returns `Task`, not `Task<int>`.
- The handler closure `async ctx => { ... await RunAsync(...); }` lets System.CommandLine default the exit code to `0`.
- All early-error returns inside `RunAsync` are `return;` with no exit-code signal.

## Fix
1. Change `RunCommand.RunAsync` return type to `Task<int>`.
2. Add an internal pure-function helper `ExitCodeFromSuiteResult(SuiteResult)` returning `0` if `Failed + Crashed == 0` else `1`.
3. Replace every `return;` early-exit error path inside `RunAsync` with `return 1;` (config error, missing file, no tests found, mutually-exclusive flags).
4. At the successful exit point of `RunAsync`, return `ExitCodeFromSuiteResult(suiteResult)`.
5. The handler closure assigns `ctx.ExitCode = await RunAsync(...).ConfigureAwait(false);`.

## Verification
- [x] Build passes (0 errors, 0 warnings)
- [x] New unit tests `Canary.Tests.CliTests.RunCommand_*ExitCode*` pass
- [x] Existing unit tests still pass
- [ ] Manual: a failing run produces `$LASTEXITCODE = 1`; an all-pass run produces `0` (operator verifies once the precursor lands)

## Related
- Audit finding: `docs/research/2026-05-24-canary-surface-audit.md` §A4 + §A5
- Implementation prompt: `MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md` §2 (Phase Precursor)
- Changelog: [CHANGELOG.md](../../CHANGELOG.md)
- Files changed:
  - `src/Canary.Harness/Cli/RunCommand.cs`
  - `tests/Canary.Tests/Cli/RunCommandExitCodeTests.cs` (new)
