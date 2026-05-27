---
date: 2026-05-27
tags: [progress, canary, supervised-session]
status: in-progress
project: canary
component: session
---

# Supervised session mode — progress log

Driving prompt: operator-authored, pasted as the first message of the
2026-05-27 session. Three planned phases (CLI/storage → UI → MCP+docs)
each with its own verification gate.

## Phase 0 — Pre-flight (2026-05-27)

- Clean tree confirmed at HEAD `da9357b` (`docs(build-log): 2026-05-27 — Qualia eager-L3 Move 4 follow-up`).
- Snapshot tag `pre-impl-supervised-session-2026-05-27` created at that SHA as the rollback anchor for the implementation.
- Baseline build: `dotnet build Canary.sln` = 0 warnings, 0 errors.
- Baseline tests: `dotnet test --filter "Category=Unit"` = 220 Passed, 0 Failed, 0 Skipped.
- Canon read: `CLAUDE.md`, the driving prompt, `src/Canary.Harness/Cli/RunCommand.cs` (mirror pattern), `src/Canary.Agent.Qualia/QualiaBridgeAgent.cs` (lifecycle), `src/Canary.Core/Cdp/CdpClient.cs` (screenshot RPC at line 276), `src/Canary.UI/Annotation/AnnotatedImageForm.cs` (entry point for Phase 2), `src/Canary.Core/Feedback/FeedbackInboxWriter.cs` + `FeedbackSlugGenerator.cs` (slug + atomic-write patterns), `src/Canary.Core/Telemetry/NdjsonFileSink.cs` + `TelemetryRecord.cs` (envelope shape).

## Phase 1 — CLI + storage layer (2026-05-27)

### Shipped

- **`src/Canary.Core/Session/SessionPaths.cs`** — pure path helpers + `GenerateSessionId(DateTime utcNow)` returning `yyyyMMdd-HHmmss-<4hex>` (4 hex chars from `RandomNumberGenerator`). Filename helpers for `CapturePngFile`/`CaptureAnnotatedPngFile`/`CaptureAnnotationsJsonFile`/`CaptureMarkdownFile` — all `NNN-HH-mm-ss[-slug]` shapes per design.
- **`src/Canary.Core/Session/CaptureSlugGenerator.cs`** — `FromTitle(string?)` reusing the same lowercase / strip-punctuation / cap-at-5-words rules as `FeedbackSlugGenerator.SlugifyTitle`. Returns `null` for empty / punctuation-only input.
- **`src/Canary.Core/Session/SessionTypes.cs`** — `SessionCapture`, `SessionData`, `CaptureResult` POCOs.
- **`src/Canary.Core/Session/SessionReportWriter.cs`** — `Write(dir, data)` writes both `SESSION_REPORT.md` and `session.json` (atomic write-to-`.tmp` + rename, mirrors `FeedbackInboxWriter`). `BuildMarkdown(data)` produces the YAML frontmatter + close-out section + per-capture rows with image embed (annotated PNG preferred, falls back to source). `TryReadJson(dir)` for the `session list` subcommand.
- **`src/Canary.Core/Session/ISessionAgentFactory.cs`** — interface + `SessionAgentBundle` carrying `ICanaryAgent` + Vite URL. Keeps `SupervisedSession` agent-agnostic so the UI layer (Phase 2) and CLI layer can both drive it.
- **`src/Canary.Core/Session/SupervisedSession.cs`** — orchestrator. `StartAsync(workloadsDir, workload, configPath, factory, ct)` generates the session id, creates the dir + `captures/` subdir, opens the `NdjsonFileSink`, delegates to the factory for the bridge agent, returns the live session. `CaptureAsync(noteTitle?, noteBody?, ct)` writes the PNG via `ICanaryAgent.CaptureScreenshotAsync` and emits a `TelemetryKind.Screenshot` record. `AttachAnnotation(seq, ...)` mutates an existing capture record (called by Phase 2's UI annotate path). `EndAsync(closeout?, ct)` aborts the agent, writes the final record, closes the telemetry sink, writes the report. `IAsyncDisposable` — disposal without End still writes the report.
- **`src/Canary.Harness/Session/SessionAgentFactory.cs`** — dispatches on `WorkloadConfig.AgentType` to `qualia-cdp` → `QualiaBridgeAgent` or `penumbra-cdp` → `PenumbraBridgeAgent`. Registers the telemetry sink before `InitializeAsync`. Anything else → `InvalidOperationException` with the v1-scope note.
- **`src/Canary.Harness/Cli/SessionCommand.cs`** — `canary session` parent + `start`, `list`, `report` subcommands. `start` enters a single-key REPL (`c` = capture, `a` = capture + open in viewer, `n` = capture with note, `q` = end). `Console.KeyAvailable` polled every 50ms; Ctrl+C falls through to the close-out prompt + `EndAsync`. `list` scans `workloads/*/sessions/*/session.json`, sorts desc by `startedAtUtc`, applies `--workload` + `--limit`. `report --id <id>` prints the matching `SESSION_REPORT.md`.
- **Registered in `src/Canary.Harness/Program.cs`** — `rootCommand.AddCommand(Cli.SessionCommand.Create())`.

### Tests added (24 unit tests net)

- **`tests/Canary.Tests/Session/SessionPathsTests.cs`** — 8 cases. `GenerateSessionId` shape matches `^[0-9]{8}-[0-9]{6}-[0-9a-f]{4}$`, two calls differ, `SessionDir` composes correctly, `CapturePngFile` theory (3 rows) covers slug / null slug / empty slug, `CaptureAnnotatedPngFile` has `.annotated.png` suffix, `TelemetryPath` lives inside the session dir.
- **`tests/Canary.Tests/Session/CaptureSlugGeneratorTests.cs`** — 7 cases. Null / empty / whitespace → null. Simple title → `landing-screen`. Punctuation stripped → `foo-bar-baz`. Capped at 5 words. Only-punctuation → null.
- **`tests/Canary.Tests/Session/SessionReportWriterTests.cs`** — 5 cases. Frontmatter contains required fields. Embeds capture rows + annotated-preferred image refs + closeout when present. `Write` creates both files. `TryReadJson` round-trips.
- **`tests/Canary.Tests/Session/SupervisedSessionTests.cs`** — 4 cases using a stub `ICanaryAgent` + stub `ISessionAgentFactory`. End-to-end: start → 2 captures → end → assert PNGs + report + json + ndjson + report content. Capture-after-end throws. Dispose-without-end still writes report. AttachAnnotation updates the capture record + final report.

### Verification gates (Phase 1)

- ✅ `dotnet build Canary.sln` — 0 warnings, 0 errors.
- ✅ `dotnet test --filter "Category=Unit"` — 244 Passed, 0 Failed (220 baseline + 24 new).
- ✅ CLI surface: `canary --help` lists `session`; `canary session --help` lists `start`/`list`/`report`; `canary session list` returns "(no sessions found)" cleanly against an empty state.
- ⏳ **Hardware-bearing follow-up for operator**: live Qualia smoke (`canary session start --workload qualia` → `c` to capture → `q` to end → confirm `workloads/qualia/sessions/<id>/SESSION_REPORT.md` renders with embedded image + telemetry NDJSON contains the screenshot record). Deferred because driving the real Vite + Chrome bridge needs the operator's local Qualia dev env; the orchestrator path is verified via the stub-agent integration tests above.

### Commits

To be created at the Phase 1 gate, per the driving prompt's commit shape:
- `feat(session): SupervisedSession core + session.json + SESSION_REPORT writer`
- `feat(cli): canary session start/list/report subcommands`
- `test(session): SessionPaths + SessionReportWriter + SupervisedSession unit tests`
- `docs: supervised-session feature + Phase 1 progress entry`
