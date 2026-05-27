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

### Commits (Phase 1)

Created in the order specified by the driving prompt's commit shape:
- `7d2d766` feat(session): SupervisedSession core + session.json + SESSION_REPORT writer
- `e86d6aa` feat(cli): canary session start/list/report subcommands
- `0533325` test(session): SessionPaths + SessionReportWriter + SupervisedSession unit tests
- `b2cef49` docs: supervised-session feature + Phase 1 progress entry

## Phase 2 — UI Sessions nav tab (2026-05-27)

### Shipped

- **`src/Canary.UI/Hotkeys/SessionHotkeyHook.cs`** — global hotkey hook for Ctrl+Shift+C (capture) and Ctrl+Shift+A (capture + annotate). Mirrors the existing `AbortHotkey` pattern: registers against the host Form's HWND via `RegisterHotKey` P/Invoke; the Form's WndProc forwards WM_HOTKEY messages to `ProcessMessage`. Two separate HOTKEY_IDs, two events. Unregister on Dispose / explicit Unregister call. Registered on session armed, unregistered on session ended — so it doesn't intercept Chrome's own Ctrl+Shift+C outside a session.
- **`src/Canary.UI/Panels/SessionsLiveSubPanel.cs`** — the Live sub-tab. State machine: Idle (workload picker + Start enabled) → Starting (all disabled, status "starting Vite + Chrome...") → Armed (Capture / Capture+Annotate / Capture+Note / End buttons + thumbnail strip + hotkey registered) → Ending (status "writing report...") → Idle. `SetWorkloads(workloadsDir, workloads)` filters the picker to `qualia-cdp` + `penumbra-cdp` only. `SupervisedSession` is constructed via `SessionAgentFactory` (the same one the CLI uses). Annotate path opens `AnnotatedImageForm` with the new sink-callback overload; on Save the annotated triad goes into the session's `captures/` dir and `SupervisedSession.AttachAnnotation` is called so the thumbnail refreshes + the final report embed switches to the annotated PNG. Note + closeout prompts are simple modal Form dialogs (no full reshell needed).
- **`src/Canary.UI/Panels/SessionsPastSubPanel.cs`** — the Past sessions sub-tab. SplitContainer with ListView (Started / Workload / SessionId / Caps) and TextBox report preview. `ScanRows(workloadsDir)` is a static + side-effect-free helper so it's unit-testable. Scans `<workloadsDir>/*/sessions/*/session.json`, sorts newest first, reapplies on filter changes.
- **`src/Canary.UI/Panels/SessionsPanel.cs`** — TabControl wrapping Live + Past. Routes hotkey messages to the Live sub-panel. `SetWorkloads(workloadsDir, workloads)` fans out to both sub-panels.
- **`src/Canary.UI/Navigation/NavModes.cs`** — adds `SessionsNavMode`. Same lazy-create + cached-content pattern as the other nav modes. Exposes `SetWorkloads` (called by MainForm on workloads load) and `ProcessHotkeyMessage` (called by MainForm.WndProc).
- **`src/Canary.UI/MainForm.cs`** — registers the new tab between Feedback and Telemetry (matches the design's "right of Feedback, left of Telemetry"). Adds `_loadedWorkloads` field to track the result of `_explorer.LoadWorkloadsAsync` so it can be re-propagated to the Sessions panel on tab activation. Extends `PropagateWorkloadsDirToMode` with a `SessionsNavMode` case. WndProc gains `_sessionsNavMode.ProcessHotkeyMessage(ref m)` between the existing AbortHotkey forward and `base.WndProc`.
- **`src/Canary.UI/Annotation/AnnotatedImageForm.cs`** — adds a constructor overload `(string sourceImagePath, Action<byte[], byte[], string> sessionSink)`. The existing `OnSave` branches on the new private `_sessionSink` field: when set, fires the callback with (sourcePng, annotatedPng, annotationsJson) and closes; when null (the original feedback-inbox path), keeps the FeedbackInboxWriter logic verbatim. Three constructors chain into a single private constructor to avoid duplicate UI-setup code.
- **`src/Canary.UI/Canary.UI.csproj`** — adds Canary.Harness project reference so SessionsLiveSubPanel can use SessionAgentFactory directly. Single-sourced agent dispatch rather than duplicating the ~50-line switch.

### Tests added (9 net new)

- **`tests/Canary.Tests/UI/SessionsPanelTests.cs`** — 6 cases. SessionsNavMode Name/Description not empty. CreateContent returns a SessionsPanel + is idempotent. SessionsPanel constructs without throwing. `SessionsPastSubPanel.ScanRows` populates correctly from a temp dir containing two qualia sessions + one penumbra session (asserts workload + session id + capture count). Missing session.json → row skipped. Null or non-existent dir → empty list.
- **`tests/Canary.Tests/Navigation/NavModeTests.cs`** — adds SessionsNavMode to the AllNavModes theory member, picking up Name/Description checks + CreateContent idempotence + names-unique automatically.

### Verification gates (Phase 2)

- ✅ `dotnet build Canary.sln` — 0 warnings, 0 errors.
- ✅ `dotnet test --filter "Category=Unit"` — 253 Passed, 0 Failed (244 baseline + 9 net new).
- ⏳ **Hardware-bearing follow-up for operator**: launch Canary.UI.exe, click Sessions → Live → pick qualia → Start session → drive Qualia in the visible Chrome → Ctrl+Shift+C anywhere triggers a capture → Ctrl+Shift+A annotates → End session → switch to Past sessions tab + confirm the just-finished session row + report preview render correctly with the annotated PNG embedded.

### Commits (Phase 2)

To be created at the Phase 2 gate, per the driving prompt's commit shape:
- `feat(ui): Sessions nav tab — Live + Past sub-panels`
- `feat(ui): Ctrl+Shift+C / Ctrl+Shift+A hotkeys for in-session capture`
- `feat(ui): AnnotatedImageForm overload — direct capture pipe-through`
- `test(ui): SessionsPanel tests + NavMode coverage`
- `docs: supervised-session Phase 2 + progress entry`
