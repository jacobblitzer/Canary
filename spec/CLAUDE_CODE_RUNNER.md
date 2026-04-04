# CLAUDE_CODE_RUNNER.md — Running Canary with Claude Code

## Overview

This document describes how to use Claude Code to build Canary with minimal human intervention. The spec files form a closed-loop system: Claude Code reads specs, builds code, runs tests, checks against the Supervisor, and advances.

---

## Directory Setup

```bash
mkdir canary
cd canary
mkdir spec
# Copy all spec files into spec/
git init
```

---

## The Prompt Sequence

### Initial Bootstrap Prompt

```
You are building a C# project called "Canary" — a cross-application visual regression testing harness.

Read these files IN ORDER before writing any code:
1. spec/SUPERVISOR.md — your orchestration rules and constraints
2. spec/ARCHITECTURE.md — system design, IPC protocol, comparison engine
3. spec/PHASES.md — what to build and when
4. spec/TESTS.md — tests for each phase

You are starting at Phase 0. Follow the Supervisor protocol:
- Read the Phase 0 requirements from PHASES.md
- Read the Phase 0 gate checklist from SUPERVISOR.md
- Build everything specified in Phase 0
- Run all Phase 0 tests
- Log results in BUILD_LOG.md
- Report status and wait for approval to advance

Important constraints:
- Target .NET 8.0 for the harness and agent library
- Target net48 for the Rhino agent plugin (Phase 6 only)
- Use System.CommandLine for CLI parsing
- Use System.IO.Pipes for IPC (no external IPC libraries)
- Use SixLabors.ImageSharp for image comparison
- Use System.Text.Json for JSON serialization
- CRITICAL: Every console output line must include a timestamp
- CRITICAL: Every status line must include "Press Ctrl+C to abort"
- CRITICAL: Console.CancelKeyPress must be registered in Program.Main FIRST
- Follow the file organization in SUPERVISOR.md exactly
```

### Phase Advancement Prompt

```
Phase [N] approved. Advance to Phase [N+1].

Before starting:
1. Re-read spec/SUPERVISOR.md Phase [N+1] Gate Checklist
2. Re-read the relevant section of spec/PHASES.md
3. Run all existing tests to confirm no regression
4. Then begin building Phase [N+1] checkpoints in order

Log all activity in BUILD_LOG.md.
```

### Debugging Prompt

```
There is an issue with [description].

Check these in order:
1. Does the current code match the constraint in spec/SUPERVISOR.md Global Constraints?
2. Does the dependency in question appear in the Dependency Matrix?
3. Is there a relevant Known Pitfall in the current phase's gate checklist?
4. Check spec/ARCHITECTURE.md for the component's design contract.

Fix the issue, re-run tests, and update BUILD_LOG.md.
```

### Autonomous Mode

```
Execute Phases [start] through [end] autonomously.

Rules:
- Follow spec/SUPERVISOR.md protocol for every checkpoint
- After each checkpoint: build, test, log results
- If a test fails: debug and fix before advancing (up to 3 attempts)
- If still failing after 3 attempts: stop, log the issue in BUILD_LOG.md with tag HUMAN_REVIEW_NEEDED, and wait
- If a Known Pitfall from SUPERVISOR.md is triggered: follow the guidance there
- If you need to make an architectural decision not covered by the specs: add a SUPERVISOR_FLAG comment and proceed with your best judgment, but log the decision in BUILD_LOG.md
- Do NOT skip any checkpoint or test

After completing all phases in range, run the full regression test suite and report.
```

---

## What Can Go Wrong

### "System.CommandLine NuGet issues"
```
Use: dotnet add package System.CommandLine --version 2.0.0-beta4.*
It's still in prerelease. Use --prerelease flag if needed.
Alternative: if System.CommandLine causes issues, use a simple manual arg parser.
The CLI is not the core product — don't over-engineer it.
```

### "Named pipe connection race condition"
```
The agent pipe server must be listening BEFORE the harness client tries to connect.
In tests, add a small delay or use a ManualResetEvent to synchronize.
In production, the harness retries connection every 500ms for up to 30 seconds.
```

### "SetWindowsHookEx fails or events not captured"
```
Low-level hooks (WH_MOUSE_LL, WH_KEYBOARD_LL) require:
1. A message pump — use a dedicated STA thread with Application.Run()
2. The hook DLL callback must return quickly (< 300ms or Windows unhooks you)
3. Admin rights are NOT required for LL hooks, but some antivirus may block them
4. If hooks fail entirely: fall back to a polling approach using GetCursorPos + GetAsyncKeyState
   This is less precise but works without hooks.
```

### "ImageSharp pixel access is slow"
```
Use image.DangerousGetPixelRowMemoryAt(y) for fast row access.
Or use image.ProcessPixelRows(accessor => { ... }) for safe bulk access.
Do NOT use image[x, y] in tight loops — it has bounds checking overhead.
```

### "Rhino agent plugin won't load"
```
Common issues:
1. Target framework must be net48 (Rhino 8), NOT net8.0
2. RhinoCommon NuGet: use Rhino.RhinoCommon or RhinoCommon package
3. Plugin GUID must be unique (not shared with any other plugin)
4. The agent must NOT block Rhino's UI thread — pipe server runs on Task.Run
5. AgentServer should start in PlugIn.OnLoad, not in a command
```

---

## Quality Gates for Human Review

You (Jake) should personally review at these points:

1. **After Phase 1** — Does IPC work reliably? Test by running the pipe round-trip manually.
2. **After Phase 2** — Does input recording capture correctly? Record a short sequence, examine the JSON.
3. **After Phase 3** — Does the comparison engine give sensible results? Generate a diff of known images and inspect the composite.
4. **After Phase 4** — Does the full runner pipeline work end-to-end with a mock agent?
5. **After Phase 6** — The big one: does Canary actually work with Rhino? Launch Rhino, replay a recording, verify screenshot and comparison.

---

## Expected Timeline

| Phase | Claude Code Work | Human Review | Total |
|-------|-----------------|-------------|-------|
| 0 | 10 min | 2 min | 12 min |
| 1 | 30-45 min | 5 min | 45 min |
| 2 | 45-60 min | 10 min | 60 min |
| 3 | 30-45 min | 5 min | 45 min |
| 4 | 60-90 min | 15 min | 90 min |
| 5 | 30-45 min | 5 min | 45 min |
| 6 | 60-90 min | 30 min (Rhino testing) | 90 min |
| 7 | 15-30 min | 10 min | 30 min |
| **Total** | **~5-7 hours** | **~1-1.5 hours** | **~7 hours** |
