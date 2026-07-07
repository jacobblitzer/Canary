---
date: 2026-07-06
tags: [bug, canary, rhino, agent-session, headless, slop-tests]
status: resolved
project: canary
severity: medium
component: "Canary.Harness — RhinoSessionAgent + TestRunner + RhinoAgent InvokeOnUi"
fix-commit: 2be407c
---

# 0016 — headless agent-session Canary runs crash with 120s pipe timeout (blocks Slop test verification)

## Symptom

Any Canary test that loads a Rhino fixture headless from an agent session (Hermes
TUI / background process) crashes with:

```
Agent did not respond within 120s for method 'Execute'.
```

The error fires at `RhinoSessionAgent.cs:118` where the `HarnessClient` named-pipe
connection is hard-capped at `TimeSpan.FromSeconds(120)`. The workload startup
timeout is 90000ms (`workloads/rhino/workload.json:8`), so Rhino is given 90s to
launch — but the Canary agent *plugin inside Rhino* never connects to the named pipe
within the 120s window when launched from an agent session.

Reproduced against BOTH a known-good existing test and newly-authored tests:

```
# existing, works when run interactively by an operator:
canary run --workload rhino --test cpig-14-graph --headless
→ CRASH (0.0% max diff) — Agent did not respond within 120s

# new R6 Slop tests:
canary run --workload rhino --test cpig-56-plane-analysis-r6 --headless
→ CRASH (0.0% max diff) — Agent did not respond within 120s
```

The existing `cpig-14-graph` test **passes when run by the operator without
`--headless`** (Rhino opens with UI, plugin connects, test runs). The crash is
specific to the agent-session / headless launch path.

## Root cause (working hypothesis)

Not a test-content bug — it's a **headless agent-session launch flake**. When
`canary run` is invoked from a Hermes agent session (TUI or background process),
Rhino's plugin loading is slow or blocked past the 120s pipe-connect window. The
named-pipe agent plugin needs to start inside Rhino and connect back to the
harness client within 120s; from an agent session it does not.

Evidence the tests themselves are correct:
1. `cpig-14-graph` (existing, operator-verified working) crashes identically headless.
2. All new Slop test JSONs pass the wire-index validator
   (`research/slop_tests/validate-wire-indices.ps1`) — 0 wire mismatches.
3. All new Canary wrapper JSONs are well-formed (`json.load` succeeds).
4. The operator confirmed `cpig-58-connected-components-r6` works when run
   interactively ("ran the connected components one, seems to work in canary").

## Impact

Blocks **end-to-end agent-side verification of any Rhino/Grasshopper Slop test**.
The 7 new R6 Slop tests (56-62) and all existing `cpig-NN-*` Slop tests cannot be
verified headless from an agent session — they must be run interactively by the
operator, which defeats automation.

This also blocks the "do a few slop definitions using cpig to make sure that you
have things correct" verification loop: the agent can author + structurally
validate the Slop JSONs, but cannot close the loop by running them.

## Resolution (2026-07-07)

**Root cause:** THREE hard-coded timeouts were masking each other:
1. `TestRunner.cs:152/524` — hard-coded `TimeSpan.FromSeconds(120)` for `HarnessClient` (the UI uses `TestRunner`, not `RhinoSessionAgent`)
2. `RhinoAgent.InvokeOnUi` — hard-coded 180s UI-thread marshal timeout
3. `RhinoSessionAgent.cs:118` — hard-coded 120s (already fixed in `2312e70`)

The `TestRunner` 120s + `InvokeOnUi` 180s fired before the configured 300s, so the config change had no effect until all three were made configurable.

**Fix (6 commits):**
- `2312e70` — `WorkloadConfig.ExecuteTimeoutMs` + `RhinoSessionAgent` wiring
- `8c0a886` — Breakpoint detection (`Debugger.IsAttached` + debugger-process scan)
- `55f37d2` — Crash dialog suppression (`SEM_NOGPFAULTERRORBOX`) + pipe-disconnect detection
- `3308961` — Full crash capture (`AppDomain.UnhandledException` + `FirstChanceException` + `VectoredExceptionHandler` → crash log)
- `17e75f4` — `TestRunner` uses `ExecuteTimeoutMs` (the real 120s hardcode)
- `2be407c` — `InvokeOnUi` 180s made configurable via `CANARY_EXECUTE_TIMEOUT_MS` env var

**The `cpig-59-field-point-cloud-r6` hang** was actually a GH type-conversion error (FieldPointCloud outputs `GH_PointCloud`, Custom Preview expects `GH_Point`-compatible geometry). The conversion error balloon caused the solution to keep re-running. Fixed in the Slop test (removed Custom Preview wiring — `cbbf027`). Test now completes in 24s (was 200s+).

**Verification:**
- `cpig-58-connected-components-r6`: PASS (25.6s, NEW) ✅
- `cpig-59-field-point-cloud-r6`: runs in 24s (no longer hangs) ✅ — BUT `SlopSuccess=False` is a SEPARATE issue (the GH solution fails, not a timeout). Tracked in **bug 0017**. The timeout fixes here were valid (three hard-coded timeouts are now configurable), but they didn't fix the test — they just stopped it from hanging. The remaining failure is a GH type-conversion / component error that needs the SlopLog content + GH error balloon to diagnose (visual inspection required).

**Note:** The crash-capture infrastructure (VectoredExceptionHandler, AppDomain handlers, crash log) was built speculatively for this bug but never fired (no crash log was ever written — the test doesn't crash, it fails gracefully). The infrastructure is retained as valuable for future native faults, but it was not the fix for this issue.

## Fix directions (for another session)

1. **Increase the pipe-connect timeout** — `RhinoSessionAgent.cs:118` hard-codes
   120s. Make it configurable via the workload config (e.g.
   `pipeConnectTimeoutMs`) or bump to 300s for headless agent sessions. This is
   the cheapest fix if the root cause is just slow plugin load.

2. **Investigate why the agent plugin is slow to connect from a headless launch** —
   is the UI-less Rhino process deferring plugin initialization? Does the
   `/runscript` on launch (which sets the view) block before plugins load? Check
   whether `appArgs` in `workloads/rhino/workload.json` delays plugin startup.

3. **Add a readiness probe** — before declaring the pipe connection failed, poll
   for the Rhino process + the plugin's named-pipe endpoint existence, and only
   start the 120s countdown once the pipe is *expected* to exist.

4. **Cross-check with MultiVerse memory** — the agent's persistent memory notes
   "UI launches flake from agent sessions" as a known issue. This bug is the
   concrete manifestation. The fix likely lives in Canary, not MultiVerse.

## Workaround (current)

Run Slop tests **interactively** (no `--headless`), so Rhino opens with its UI
and the plugin connects within the timeout:
```
canary run --workload rhino --test cpig-56-plane-analysis-r6
canary run --workload rhino --test cpig-58-connected-components-r6   # confirmed working
```

## Related

- Existing memory note: "UI launches flake from agent sessions per the
  `canary_launch_from_session` auto-memory"
- `canary run --workload rhino --test cpig-14-graph --headless` is the canonical
  repro (known-good test, crashes headless)
- All 7 R6 Slop tests (56-62) are authored, validated, committed, and pushed;
  they just need interactive runs to close the verification loop.