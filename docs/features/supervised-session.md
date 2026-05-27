---
date: 2026-05-27
tags: [feature, canary, supervised-session]
status: in-progress
project: canary
component: session
---

# Supervised session mode

A no-tests-running mode where the operator launches a workload's target app under Canary supervision, drives it manually in the visible browser window, captures the screen on demand, and closes out with a single `SESSION_REPORT.md` bundling every capture + the universal telemetry NDJSON.

Turns Canary from "regression harness with annotation" into "operator-driven debugging cockpit" — the path that was previously suite-run → drill into Past Runs → Annotate now collapses to "boot Qualia, drive, capture, end."

## CLI surface (Phase 1, shipped 2026-05-27)

```
canary session start --workload qualia [--url <override>]
canary session list [--workload <w>] [--limit <n>]
canary session report --id <yyyyMMdd-HHmmss-xxxx> [--workload <w>]
```

`session start` blocks: it spins up Vite + Chrome (visible) + the CDP bridge, prints a one-line "session armed" banner, streams telemetry to the session dir, and enters a single-key REPL:

```
[supervised session armed] sessionId=20260527-143022-a3f1 workload=qualia url=http://localhost:5173/
   c = capture     a = capture + open in viewer     n = capture with note     q = end + write report
   dir: C:\Repos\Canary\workloads\qualia\sessions\20260527-143022-a3f1
```

The operator drives the app in the Chrome window while Canary observes. `q` (or Ctrl+C) closes out: it prompts for one-line closeout notes, writes `SESSION_REPORT.md` + `session.json`, finalizes the telemetry NDJSON, and tears down Vite + Chrome.

## Storage layout

Sessions live alongside `results/` under the workload dir:

```
workloads/<workload>/sessions/
  <yyyyMMdd-HHmmss-xxxx>/
    SESSION_REPORT.md          markdown bundle (frontmatter + close-out + per-capture embeds)
    session.json               machine-readable: { sessionId, workload, startedAt, endedAt, captures[], notes }
    telemetry.ndjson           universal envelope, same shape as `results/.../telemetry.ndjson` from §C1
    captures/
      001-<hh-mm-ss>-<slug>.png             raw screenshot
      001-<hh-mm-ss>-<slug>.annotated.png   (Phase 2 — when operator hits Annotate)
      001-<hh-mm-ss>-<slug>.annotations.json
      001-<hh-mm-ss>-<slug>.md              (optional per-capture note)
      002-...
```

`<slug>` is derived from the per-capture note title via the same lowercase/strip/cap-at-5-words rules as `FeedbackSlugGenerator`. Captures without a note get just `NNN-hh-mm-ss.png`.

## What it does NOT do (v1 scope)

- **No driving the app.** No keystroke replay. No assertions. No mode picker. The session is a passive observer plus a screenshot trigger.
- **No supervision of apps without a CDP bridge.** v1 is limited to workloads that already have one (`qualia-cdp`, `penumbra-cdp`). Rhino workload deferred — needs a different screenshot path.
- **No multi-session concurrency.** One session at a time per workload.
- **No editing past sessions.** Past sessions are read-only; new annotations against past captures still go through the existing Annotate button + feedback inbox.

## Phase status

- **Phase 1 (CLI + storage, shipped 2026-05-27)** — `canary session start/list/report` subcommands, `SupervisedSession` orchestrator, `SessionReportWriter`, telemetry NDJSON wiring. CLI-only; annotation in this phase opens the PNG in the default image viewer.
- **Phase 2 (UI, shipped 2026-05-27)** — Sessions nav tab in `Canary.UI` with Live + Past sub-panels, global Ctrl+Shift+C / Ctrl+Shift+A hotkeys, `AnnotatedImageForm` overload that writes the annotated triad into the session's `captures/` dir (not the global feedback inbox).
- **Phase 3 (MCP + docs, pending)** — `list_sessions` + `get_session_report` MCP tools.

## UI workflow (Phase 2)

Open Canary.UI, click the **Sessions** nav tab (between Feedback and Telemetry).

**Live sub-tab:**
1. Pick a workload from the dropdown (only `qualia-cdp` / `penumbra-cdp` workloads appear).
2. Click **Start session**. Vite + Chrome boot visibly (~10–30s); the panel transitions to the armed state with Capture / Capture+Annotate / Capture+Note / End buttons enabled.
3. Drive the app in the visible Chrome window. Hit a capture button (or **Ctrl+Shift+C** anywhere) when you want a screenshot. Each capture appears as a thumbnail in the strip; click a thumbnail to open the PNG.
4. **Ctrl+Shift+A** captures and immediately opens the annotation surface (`AnnotatedImageForm`). Save writes the annotated PNG + annotations.json into the session's `captures/` dir and refreshes the thumbnail.
5. **End session** prompts for one-line close-out notes, writes `SESSION_REPORT.md`, finalizes telemetry, tears down Vite + Chrome.

**Past sessions sub-tab:** read-only list (Started / Workload / SessionId / Caps) with a right-pane `SESSION_REPORT.md` preview. Filter by workload or session id.

The hotkeys are registered globally only while a session is armed and unregistered on End, so they don't compete with Chrome's own Ctrl+Shift+C (DevTools inspect) outside of an active session.

## Implementation pointers

- `src/Canary.Core/Session/` — `SupervisedSession`, `SessionPaths`, `CaptureSlugGenerator`, `SessionReportWriter`, `SessionTypes`, `ISessionAgentFactory`.
- `src/Canary.Harness/Session/SessionAgentFactory.cs` — dispatches on `WorkloadConfig.AgentType` to construct + initialize the right bridge agent.
- `src/Canary.Harness/Cli/SessionCommand.cs` — System.CommandLine wiring + the REPL.

## See also

- Driving prompt: see operator's 2026-05-27 supervised-session prompt.
- Feedback inbox (the cross-session annotation path): `docs/feedback/README.md`.
- Phase 5 / §C5 annotate flow that this complements: `docs/plans/2026-05-24-canary-debug-overhaul.md`.
