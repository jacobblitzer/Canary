---
date: 2026-05-27
tags: [bug, cli, session]
status: resolved
project: canary
severity: medium
component: cli
fix-commit: ""
---

# 0008 — `canary session start` REPL crashes when stdin is redirected

## Summary
`canary session start --workload <w>` polls `Console.KeyAvailable` in a single-key REPL loop. `Console.KeyAvailable` (and `Console.ReadKey`) throw `InvalidOperationException` whenever the process's stdin is redirected from a file or pipe rather than attached to a console. This made the supervised-session CLI unusable from any non-interactive context: CI smoke scripts, `printf "c\nq\n" | canary session start ...`, automated agents driving the CLI, etc.

The `await using (session)` block on the call path did mean the failsafe disposal still wrote a `SESSION_REPORT.md` + `session.json` + finalised `telemetry.ndjson`, so the bug presented as an ugly unhandled exception + zero captures — not data loss. Found during the operator-side Phase 1 verification smoke on 2026-05-27.

## Environment
- OS: Windows 11 Pro
- Toolchain / SDK: .NET 10 SDK building `net8.0-windows`
- Host app: any (failure is stdin-mode dependent, not workload dependent)
- Project version: at HEAD `49a9b5d` (just after Phase 3 of supervised-session shipped)

## Steps to Reproduce
1. From `C:\Repos\Canary\`, run
   ```
   printf "c\nq\nsmoke\n" | ./src/Canary.Harness/bin/Debug/net8.0-windows/canary.exe session start --workload qualia
   ```
2. Wait ~10s for Vite + Chrome + CDP to come up.
3. Observe the unhandled `System.InvalidOperationException` from `Canary.Cli.SessionCommand.RunReplAsync` line 101:
   ```
   System.InvalidOperationException: Cannot see if a key has been pressed when either application does not have a console or when console input has been redirected from a file. Try Console.In.Peek.
      at System.Console.get_KeyAvailable()
      at Canary.Cli.SessionCommand.RunReplAsync(...)
   ```

## Root Cause
`RunReplAsync` unconditionally used `Console.KeyAvailable` to poll for input. The .NET runtime documents that this property throws when stdin is redirected — there's no fallback in the original implementation.

## Fix
Detect `Console.IsInputRedirected` at REPL entry and branch to a line-mode loop that uses `Console.In.ReadLineAsync(ct)` instead. Each non-empty line's first character is interpreted as a command (same `c` / `a` / `n` / `q` alphabet as the interactive REPL). The interactive `Console.ReadKey` path stays unchanged for the common operator-at-a-terminal case.

The banner gains a one-line "(stdin is redirected — line-mode REPL: one command character per line)" hint when the line-mode branch is taken, so the operator sees what behaviour is in effect.

Verification: re-ran the repro with the fix in place. Session armed, `c` triggered a real capture (158 KB PNG of Qualia's Configure screen), `q` ended the REPL, the closeout line was absorbed correctly, `SESSION_REPORT.md` + `session.json` + `captures/001-16-35-30.png` + `telemetry.ndjson` (with a `kind: Screenshot` envelope linking back to the PNG path) all landed correctly. Exit code 0.

## Notes
- Pre-existing Vite-cleanup edge case surfaced alongside: `ViteManager.Dispose` calls `Kill(entireProcessTree: true)` on the spawned `cmd.exe`, but on session end the grandchild `node.exe` sometimes survives (process tree walk timing). The next session's `KillStaleListenerAsync(port)` pre-kill papers over it. Not in scope here.
