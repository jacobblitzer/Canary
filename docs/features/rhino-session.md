---
date: 2026-06-02
tags: [feature, canary, supervised-session, rhino]
status: landed-v1
project: canary
component: session
landed-date: 2026-06-02
---

## v1 status (2026-06-02)

Shipped: `canary session start --workload rhino` launches Rhino under supervision, connects the existing `canary-rhino-<pid>` named-pipe agent, and drops into the same REPL loop Qualia + Penumbra use. Captures go to `workloads/rhino/sessions/<yyyyMMdd-HHmmss-xxxx>/captures/`. `session list` and `session report` work via the existing dispatch (no Rhino-specific changes needed there).

Files shipped:
- `src/Canary.Harness/Session/RhinoSessionAgent.cs` — `ICanaryAgent` adapter wrapping `AppLauncher.Launch` + `HarnessClient`. Owns the launched process; `DisposeAsync` kills it.
- `src/Canary.Harness/Session/SessionAgentFactory.cs` — new `"rhino"` case in the agent-type switch.
- `src/Canary.Harness/Cli/SessionCommand.cs` — `--workload` help text updated to list rhino.

## v2 (partial — Penumbra preview telemetry, 2026-06-17)

`RhinoSessionAgent` now implements `ITelemetryAware` and tails Penumbra's in-Rhino preview NDJSON
(`%LocalAppData%\Penumbra\preview\telemetry.ndjson`, via `PenumbraPreviewTelemetryTail` in
`src/Canary.Core/Telemetry/`) into the session's `telemetry.ndjson`; `SessionReportWriter` renders a
"Penumbra preview telemetry" section in `SESSION_REPORT.md`. So a hand-driven CPig/Penumbra-in-Rhino session
is debuggable from the report — `scene.loaded` (+tape/+grid + bounds), `gl.field.transform` (gumball moves),
`rep.live` (display-rep switches), `frame.real`, `render.error`. Penumbra events are wrapped as
`Kind=Log, Source="penumbra"` (the domain kind in `Data.event`), since Penumbra's free-form `kind` doesn't map
onto Canary's `TelemetryKind` enum. The tail baselines at the file's current end (this session's events only)
and stops on agent dispose. `SessionAgentFactory.CreateRhinoAsync` now REGISTERS the session sink (it
previously discarded it).

**Still deferred to v2:**
- The OTHER Rhino telemetry sources: Rhino command-line history + the Slop `LogHub` / `cpig_debug.log` tail.
- `--file <path.gh>` / `--mech <path.kin.json>` shortcuts that pre-open a fixture in Rhino. Today the operator opens whatever they want from inside Rhino after the session arms.
- Bring-Rhino-to-foreground hint at session-start so the operator doesn't have to alt-tab to find it.

**Known v1 caveat (2026-06-02 smoke test):**
- `RhinoSessionAgent.DisposeAsync` calls `Process.Kill(entireProcessTree: true)` but the kill didn't actually tear down Rhino on closeout — two zombie Rhino processes remained alive after the smoke-test session exited. Suspect `SupervisedSession.DisposeAsync` doesn't propagate to the bundle's `IAsyncDisposable Agent` (need to verify). Manual workaround: `Get-Process Rhino | Stop-Process -Force` between sessions. v1.1 fix: trace the disposal chain and ensure agent disposal fires before process exit.

# Rhino session mode

A third supervised-session workload alongside `qualia` (browser-based) and `penumbra` (browser-based) — boots Rhino + Grasshopper under Canary supervision so the operator can drive a Slop / CPig.Kinematics / retopo fixture by hand, capture screenshots on demand, and produce a `SESSION_REPORT.md` bundle without writing a Canary test definition first.

Today Canary launches Rhino only during automated test runs (`canary run --workload rhino [--test … | --suite …]`). The supervised session pattern (Qualia + Penumbra, shipped 2026-05-27) gives the operator a no-tests-running mode for "boot the app, drive, capture, end". Adding the Rhino workload to that pattern would let the operator:

- Iterate on a Slop JSON fixture interactively, capturing the canvas state on each change without authoring a test.
- Drive a CPig.Kinematics mechanism by manually dragging the AnimSlider on the GH canvas, capturing intermediate poses to visually validate before adding scrub-capture to a test.
- Debug a Rhino agent crash by running the agent under supervision and capturing the canvas + the Canary side panel side-by-side at the moment of failure.

## Why it's not free

Rhino is different from Qualia/Penumbra in three concrete ways:

1. **Rhino is a desktop app, not a localhost web app.** No Vite dev server, no Chrome devtools CDP bridge, no telemetry NDJSON via WebSocket. The agent uses a named-pipe RPC (`canary-rhino-<pid>`) for in-process control; the existing Rhino agent (`Canary.Agent.Rhino.rhp`) already exposes the capture surface needed.
2. **The capture surface is the active Rhino viewport, not a browser canvas.** `RhinoScreenCapture` already handles this for test runs; the supervised session would reuse the same code path with a manual trigger.
3. **There's no "URL" to navigate to.** The session would either boot Rhino with a specific `.3dm` / `.gh` file open, or boot it empty and let the operator load whatever they want.

## CLI surface (proposed)

```
canary session start --workload rhino [--file <path.gh>] [--mech <path.kin.json>]
canary session list --workload rhino
canary session report --id <yyyyMMdd-HHmmss-xxxx> --workload rhino
```

`--file fixtures/cpig_slop_loader.gh` would mirror what the suite-run pattern does today.

`--mech` would be a convenience that pre-fills the Slop loader's JsonPath panel with the mechanism file.

REPL keys identical to Qualia/Penumbra:
- `c` = capture active viewport → `<sessionDir>/captures/<n>.png`
- `a` = capture + open in default viewer
- `n` = capture + prompt for a one-line note saved as `<n>.note.txt`
- `q` = end + write `SESSION_REPORT.md`

## Session output layout (mirrors Qualia/Penumbra)

```
C:\Repos\Canary\workloads\rhino\sessions\<yyyyMMdd-HHmmss-xxxx>\
  ├ SESSION_REPORT.md             # operator-facing summary; opens automatically on `q`
  ├ session.json                  # machine-readable: timestamps, workload, captures[]
  ├ telemetry.ndjson              # Rhino command-history + Slop log lines, line-delimited
  └ captures\
      ├ 001.png
      ├ 001.note.txt              # if `n` was used
      ├ 002.png
      └ ...
```

## What it would deliver to CPig

- **Slop-fixture authoring loop:** edit a kin_*.json or kin_*.slop.json → drop into Rhino → capture pose → tweak → re-capture. The current alternative (write a Canary test, run it, view past-runs) is high-friction for one-off exploration.
- **CPig.Kinematics manual scrub:** the AnimSlider can be dragged by hand without setting up a Canary test's `capture.scrub` config. Captures from manual scrubbing populate the session bundle alongside notes.
- **Crash repro capture:** the operator can re-run a known-crash repro under supervision; if Rhino survives long enough to issue `c`, the captured state ends up in the session report alongside the agent's exit log.

## Implementation scope (rough)

Most code already exists. New work:

1. **`Canary.Agent.Rhino` REPL hooks** — a way to receive a capture trigger from outside the test pipeline. Reuse the existing named-pipe RPC; add a `SessionCapture` action that takes a session dir + frame index and writes the PNG there.
2. **`Canary.Core/Session/RhinoSessionRunner.cs`** — analogue of `QualiaSessionRunner` / `PenumbraSessionRunner` (see `src/Canary.Core/Session/`). Boots Rhino, attaches to the named pipe, runs the REPL.
3. **`Canary.Harness` `session` verb** — extend the existing `session start --workload <w>` dispatch to handle `rhino`.
4. **`Canary.UI.Avalonia` Sessions tab** — Rhino entry in the workload picker. **(Fixed 2026-06-17: the v1 ship MISSED this — `SessionsLiveViewModel.SetWorkloads` filtered the picker to `qualia-cdp`/`penumbra-cdp` only, so the Sessions tab offered no rhino option even though the factory + CLI supported it. Now includes `"rhino"`.)**
5. **Telemetry source.** Qualia/Penumbra get CDP Console + Log + Network. Rhino's analogue is the Rhino command history + the Slop log panel content. Need to decide whether to tail `C:\Repos\CPig\cpig_debug.log` and the Slop log panel into the session's `telemetry.ndjson`, or skip telemetry for v1.

## Out of scope (v1)

- Multi-instance Rhino (one supervised session = one Rhino process — same as the test runner).
- Time-machine scrubbing (replay a session's captures via Canary.UI). The captures are static PNGs in the session dir; the operator browses them in Explorer.
- VLM evaluation of captures. Sessions are operator-driven; if VLM is wanted, the operator promotes the capture into a test.

## Related
- Existing supervised-session feature: [supervised-session.md](supervised-session.md) (Qualia + Penumbra, 2026-05-27).
- Rhino agent design: `src/Canary.Agent.Rhino/RhinoAgent.cs` and the related action handlers.
- CPig.Kinematics manual-scrub demand: tracked via the recent bug investigation around `cpig-kin-15-watt-straight-line` — operators have been wanting to drag the slider by hand to validate animation tuning before committing scrub configs to a test.
