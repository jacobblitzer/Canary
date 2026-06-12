---
date: 2026-06-11
tags: [bug, canary, penumbra]
status: resolved
project: canary
severity: high
component: "penumbra-agent, cdp"
---

# Penumbra sweep crashes ~35% of tests: `Timed out waiting for CDP event 'Page.loadEventFired'`

## Symptom

During the 2026-06-11 full ~90-test headless penumbra sweep, ~35% of tests
ended `Crashed` with `Timed out waiting for CDP event 'Page.loadEventFired'`.
Every crashed `result.json` had **exactly 60.0s duration and zero
checkpoints**. Failures were intermittent, worsened late in the sweep, and
the same tests passed when run individually or in small batches. The harness
process stayed healthy throughout.

## Root cause

Two hard-coded `NavigateAsync(url, TimeSpan.FromSeconds(60))` calls in
`PenumbraBridgeAgent` (initial load in `InitializeAsync`, and `SetBackendAsync`).

The non-obvious part: **every penumbra test re-navigates the page even in a
shared-agent suite run.** A full-workload/suite invocation creates ONE
Vite + Chrome for all tests (`RunCommand.RunPenumbraSuiteAsync`), but each
test's `setup.backend` field makes `TestRunner.SendAgentSetupAsync` dispatch
`SetBackend`, which does a full page reload. Each reload re-runs Penumbra's
module top-level init — WebGPU adapter request + Dawn pipeline builds, 30–90s
on this hardware (the same work that already forced the CDP eval ceiling to
240s and `WaitForPenumbraReadyAsync` to 120s). As the long-lived browser ages
over dozens of reloads, load time creeps past the 60s navigate ceiling →
CRASH at exactly 60s. The 60s ceiling was the only init-path timeout never
raised when the others were.

Secondary teardown gaps (relevant to per-test `canary run` loops):

- `ViteManager.KillStaleListenerAsync` killed the stale port-3000 listener
  but returned before the OS released the socket — the next `--strictPort`
  Vite could hit EADDRINUSE or the buffered-"ready" race.
- `ChromeLauncher.LaunchAsync` never checked the CDP port: a leftover
  automation browser holding 9222 means the new Chrome silently fails to
  bind and `PollForCdpEndpointAsync` connects to the **stale** browser.
- `ChromeLaunchResult.Dispose` attempted the temp-profile delete once while
  Chrome children still held file locks → 28 leaked
  `%TEMP%\canary-chrome-*` dirs.
- **`ViteManager.StopInternal`'s `Kill(entireProcessTree)` races toolhelp
  child enumeration against the `cmd → npm(node) → cmd → node(vite)` chain
  and can orphan the actual vite node** (caught live during fix validation:
  vite survived teardown, kept port 3000 listening, and — because spawned
  children inherit the harness's console handles — kept the external driver
  script blocked on canary's redirected output until the orphan died). This
  is why the 2026-06-11 retry pass needed an external 20s "janitor" loop
  killing `node.exe`.

## Fix (2026-06-11)

- `PenumbraConfig.PageLoadTimeoutMs` (default 180_000, wired in
  `workloads/penumbra/workload.json`) replaces both hard-coded 60s waits.
- `PenumbraBridgeAgent.NavigateWithRetryAsync` retries the navigation once
  on load-event timeout (a reload is idempotent) and writes a `Log`/warn
  telemetry record when it does.
- `ViteManager.KillStaleListenerAsync` now polls up to 5s for the port to
  actually go free, throwing loudly if it never does.
- `ViteManager.StopInternal` now falls back to kill-by-port after the
  tree-kill when something still listens on the Vite port, so an orphaned
  vite node dies before the harness exits (no janitor loop needed).
- `ChromeLauncher.LaunchAsync` kills any stale listener on the configured
  CDP port before launching, and GCs `canary-chrome-*` temp dirs older than
  2h; `ChromeLaunchResult.Dispose` retries the profile delete 3× with
  backoff.

## Validation

Re-ran the remaining crashed-test roster (19 tests) as individual headless
`canary run` invocations with no external kill/sleep workarounds
(`.retry-validation.log`, 2026-06-11 19:22–19:52): **zero load-timeout
crashes**; port 3000 verified released after every run; temp-profile dir
count flat across all 19 spawns. Typical per-invocation wall time 20–40s
(atlas-heavy tests 90–160s) — comfortably under the 180s ceiling, so the
retry never had to fire (0 `retrying navigation` telemetry records). The
orphaned-vite failure was caught live during validation: iteration 1 ran
on the pre-fallback binary, leaked the vite node, and blocked the driver
script for 13 minutes until the orphan was killed; iterations 2–19 ran on
the fixed binary with clean teardown each time.

## Not this bug

`feature-loader-mutex-rejection`, `feature-loader-quality-profile`, and
`stl-import-benchy` crash with `JavaScript evaluation failed: Uncaught`
even on clean solo spawns — a separate test-side/Penumbra-side failure
being fixed in a parallel session (`feature-loader-all-off` was the first
of these repaired there: its setup assertion was stale since the
2026-05-08 default-profile flip, and it passes again as of 2026-06-11).
