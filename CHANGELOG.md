---
title: "Changelog"
tags:
  - changelog
---

# Changelog

All notable changes to Canary are documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added — Canary.UI Avalonia migration (2026-05-27)

Phases 0–3 — migration from WinForms to **Avalonia 11.2 + FluentAvaloniaUI + CommunityToolkit.Mvvm**. Beginning with the Sessions panel (most layout-pained surface), expanding to the full nav shell + four read-only panels (Localhost / Feedback / Telemetry / Settings), then the **Tests tab** (workload tree + TestRunner + ResultsViewer + Recording), then the **editors** (Test / Suite / Workload) with JSON round-trip property tests. New project `src/Canary.UI.Avalonia/` builds alongside the existing `src/Canary.UI/` through phases 0–5; cutover at Phase 6.

Phase 3 (shipped 2026-05-27):
- Three new editors — `TestEditorView` (tabbed: Basic / Checkpoints / Actions+Asserts), `SuiteEditorView` (test-picker checklist), `WorkloadEditorView` (4-column form + setup-commands list). All three wrap an underlying POCO so unmanaged fields (Penumbra `Setup.Scene/Canvas/DisplayPreset/Commands`, VLM provider config, `TestAction.Extra` JsonExtensionData) round-trip byte-identical when Save re-serializes.
- 15 new unit tests under `tests/Canary.Tests/UI.Avalonia/Editors/` covering Load → BuildDefinition idempotence, validation, Add/Remove command mutation, and unmanaged-field preservation. 299 → 314 total.
- Editors are **orphan code** in Phase 3 — wire-in via tree context menus lands in Phase 5 with `DragDropHandlers`.



Phase 2 (shipped 2026-05-27):
- **Tests tab** is now the lead nav item — workload tree on the left (Workloads → Suites / All Tests / Recordings hierarchy with kind-tagged WorkloadNode rows), content swap on the right (Welcome / TestRunner / ResultsViewer / Recording).
- **TestRunnerViewModel** drives the existing orchestrator paths (`RunQualiaAsync` / `RunPenumbraAsync` / `RunSharedSuiteAsync` / `RunSuiteAsync`) unchanged; implements `ITestProgressEvents` with one progress card per checkpoint (status + status color + VLM prompt + reasoning + screenshot path). `AvaloniaTestLogger` marshals to the UI thread via `Dispatcher.UIThread.Post`.
- **ResultsViewerViewModel** ships `LoadResult` / `LoadSuiteResult` + Approve/Reject/ApproveAll commands wired to `BaselineManager`. View shows checkpoint cards with baseline/candidate/diff thumbnails.
- **RecordingViewModel** ports the InputRecorder + AppLauncher launch-record-save flow.
- **Tests-only toolbar items** — Run Tests (F5), Mode picker (None/PixelDiff/Vlm/Both), Record — bind `IsVisible` to `IsTestsActive` so they appear only on the Tests tab. F5 KeyBinding registers globally on `MainWindow`.
- 12 new unit tests across `WorkloadTreeViewModelTests` (3), `TestRunnerViewModelTests` (4), `ResultsViewerViewModelTests` (5). 287 → 299 total.



Phase 1 (shipped 2026-05-27):
- Four nav-panel ports — `LocalhostViewModel` + `FeedbackViewModel` + `TelemetryViewModel` + `SettingsViewModel` with matching Views. Each panel uses Avalonia layout primitives (Grid + WrapPanel + GridSplitter + DataGrid) so it reflows on narrow widths instead of clipping.
- Full `NavigationView` shell wired in `MainWindow.axaml` — Sessions / Localhost / Feedback / Telemetry / Settings — plus a top toolbar row with **Open workloads folder…** + a workloads-dir indicator. Folder picker via Avalonia's `StorageProvider.OpenFolderPickerAsync`.
- 17 new unit tests under `tests/Canary.Tests/UI.Avalonia/` (Localhost × 4, Feedback × 4, Telemetry × 3, Settings × 6). One real bug caught + fixed: `TelemetryViewModel` was short-circuiting source-filter changes when the underlying NDJSON file's mtime hadn't moved.

Phase 0 (shipped 2026-05-27):

- New `Canary.UI.Avalonia` csproj (net8.0-windows, WinExe): FluentAvalonia `NavigationView` shell, Sessions panel (Live + Past sub-views), Avalonia port of `AnnotationCanvas` + `AnnotateWindow`, Win32 hotkey hook via Comctl32 `SetWindowSubclass`, single-instance pipe forwarder copied verbatim from the WinForms project.
- ViewModels use `[ObservableProperty]` + `[RelayCommand(CanExecute=...)]` source generators; the four Sessions-Live button gates fall out of `CanStart` / `CanCapture` / `CanEnd` predicates instead of manual `Enabled = false`.
- 12 new unit tests under `tests/Canary.Tests/UI.Avalonia/` (`SessionsLiveViewModelTests` × 9, `SessionsPastViewModelTests` × 3) — 258 → 270 total.
- Plan + feature doc + per-phase progress log: `docs/plans/2026-05-27-canary-ui-avalonia-migration.md` + `docs/features/canary-ui-avalonia.md` + `docs/progress/2026-05-27-canary-ui-avalonia-migration.md`.

Combined Phase 0 + 1 + 2 + 3 unit test delta: 258 → 314 (+56 net new). All remaining phases queue after operator review at each phase boundary; no push until Phase 6.

### Fixed — bug 0008: `canary session start` REPL crashed on redirected stdin (2026-05-27)
- `SessionCommand.RunReplAsync` now detects `Console.IsInputRedirected` and branches to a line-mode REPL using `Console.In.ReadLineAsync` when stdin is piped or file-redirected. The original single-key `Console.ReadKey` path remains for interactive terminals. Found during the Phase 1 verification smoke (the smoke itself was the repro); fix verified by re-running the smoke with `printf "c\nq\nclose-out\n" | canary session start --workload qualia` and confirming a real PNG capture + clean exit code 0. See `docs/bugs/0008-session-repl-crashes-on-redirected-stdin.md`.

### Added — Supervised session mode Phase 3 (2026-05-27)
- Two new MCP tools in `Canary.McpServer` bringing the total from 8 to 10:
  - `list_sessions [--workload <w>] [--limit <n>]` — enumerates supervised sessions across workloads (row per `workloads/<w>/sessions/<id>/session.json`).
  - `get_session_report --sessionId <id>` — returns the full `SESSION_REPORT.md`.
- Cross-repo doc pass: Canary CLAUDE.md Quick Reference + nav-tab list updated; Canary README.md features list updated; `docs/mcp-server.md` tool table bumped to 10; `MultiVerse/BUILD_LOG.md` cross-repo entry; `Qualia/CLAUDE.md` Canary integration section gains the supervised-session pointer.
- 5 new unit tests (`SessionsToolsTests`).

### Added — Supervised session mode Phase 2 (2026-05-27)
- New **Sessions** nav tab in `Canary.UI` (between Feedback and Telemetry). `SessionsPanel` hosts two sub-tabs:
  - **Live**: workload picker, Start/Capture/Capture+Annotate/Capture+Note/End buttons, live thumbnail strip, status line, hotkey hint. Constructs a `SupervisedSession` via `Canary.Harness.Session.SessionAgentFactory` so the same factory used by the CLI also serves the UI.
  - **Past sessions**: SplitContainer with session list (Started / Workload / SessionId / Caps) and `SESSION_REPORT.md` preview pane. Filter box + Refresh.
- Global hotkeys (while a session is armed): **Ctrl+Shift+C** = capture; **Ctrl+Shift+A** = capture + open annotation surface. Registered against MainForm's HWND on Start, unregistered on End — mirrors the existing `AbortHotkey` pattern; MainForm.WndProc routes WM_HOTKEY through both hooks.
- `AnnotatedImageForm` gains a constructor overload that takes a sink callback `Action<sourcePng, annotatedPng, annotationsJson>`. SessionsLiveSubPanel uses it to write the annotated triad into the session's `captures/` dir (not the global feedback inbox) and calls `SupervisedSession.AttachAnnotation` so the report's image embed switches to the annotated PNG.
- `Canary.UI` now references `Canary.Harness` (single-sourced agent dispatch in `SessionAgentFactory`).
- 9 new unit tests (`SessionsPanelTests` covering NavMode identity, panel construction, `ScanRows` against a temp dir with multiple workloads, missing-json skip, null/missing dir → empty) + 3 added `NavModeTests` theory rows for `SessionsNavMode`.

### Added — Supervised session mode Phase 1 (2026-05-27)
- New `canary session` subcommand family: `start --workload <w>` launches the workload's target app under Canary supervision (no automated tests) and enters a single-key REPL for on-demand screen captures; `list` enumerates past sessions; `report --id <id>` prints the matching `SESSION_REPORT.md`. Closes the gap where exploratory debugging required running a suite first.
- New per-session storage at `workloads/<w>/sessions/<yyyyMMdd-HHmmss-xxxx>/` containing `SESSION_REPORT.md` (markdown bundle), `session.json` (machine-readable), `telemetry.ndjson` (same envelope as test runs), and `captures/NNN-<hh-mm-ss>[-slug].png`.
- `Canary.Core.Session` namespace: `SupervisedSession` orchestrator (`IAsyncDisposable`), `SessionPaths`, `CaptureSlugGenerator`, `SessionReportWriter` (atomic frontmatter + capture rows), `ISessionAgentFactory` interface keeping the orchestrator agent-agnostic.
- `Canary.Harness.Session.SessionAgentFactory` dispatches on `WorkloadConfig.AgentType` to `qualia-cdp` → `QualiaBridgeAgent` or `penumbra-cdp` → `PenumbraBridgeAgent` (v1 scope — Rhino workload deferred to v2).
- 24 new unit tests in `tests/Canary.Tests/Session/` covering id format, slug rules, markdown shape, file round-trip, and end-to-end orchestration via a stub agent.
- Phase 2 (UI nav tab + hotkeys + annotate-into-session) and Phase 3 (MCP `list_sessions` / `get_session_report` tools + cross-repo doc pass) are queued follow-ups.

### Changed — Debug-overhaul polish (post-Phase 9, 2026-05-24)
- **Toolbar mode picker** widened (110 → 140 px) so "pixel-diff" + the dropdown chevron fits without truncation.
- **Nav tabs** upgraded from default styling to `TabAppearance.FlatButtons` + fixed `ItemSize(140, 32)` + Segoe UI 10.5pt + `Padding(12, 6)`. Now visually the primary surface rather than an easy-to-miss strip.
- **Tests-only toolbar items** (Run Tests / Mode label + picker / Record / Approve / View Report / Deploy Agent / Close Workload / Expand All + their grouping separators) hide when the operator switches to any non-Tests nav tab. Only Open Folder stays visible everywhere (it discovers the workloads dir all panels read). Implements the §C4 polish deferred from Phase 7.
- **Localhost toolbar button dropped** — was a Phase 4 leftover (originally opened a popup; Phase 7 had it just switch tabs) fully redundant with the Localhost nav tab. `OnShowLocalhost` handler removed.

### Changed — Debug-overhaul Phase 9 (cross-repo doc pass, 2026-05-24)
- `CLAUDE.md` Quick Reference rewritten to point operators at the new debug-overhaul surfaces: toolbar mode picker, 6 nav tabs, per-run dir layout, telemetry NDJSON path, MCP server, feedback inbox. The §16 rule 8 line updated to reflect Phase 1 having shipped (no longer "queued").
- `docs/features/FEATURE_STATUS.md` gains a Debug-overhaul section table mapping each shipped feature to its phase number plus the consolidated deferred follow-ups list.
- `docs/plans/2026-05-24-canary-debug-overhaul.md` status flipped `in-progress → shipped`; retrospective appended (what shipped exactly as designed, scope deviations, deferred items table, counts, operator-visible deltas).
- Child repo updates: `C:/Repos/Penumbra/CLAUDE.md` and `C:/Repos/Qualia/CLAUDE.md` each gain a "Canary integration (debug-overhaul shipped 2026-05-24)" section documenting telemetry capture + per-run REPORT.md + MCP server + spawn registry + feedback inbox. No Penumbra/Qualia-side code changes needed.
- `C:/Repos/MultiVerse/BUILD_LOG.md` gains one consolidated cross-repo entry summarising the implementation outcome + deferral list.
- `MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md` frontmatter flipped `status: READY → EXECUTED` + `executed: 2026-05-24` + banner mirroring the design doc retrospective.

### Added — Debug-overhaul Phase 8 (C7 Tier 3 + C8 polish + C9 settings, 2026-05-24)
- `Canary.Localhost.HeuristicProcessLister` — Tier 3 of design §C7. `Enumerate()` returns Process.GetProcesses() filtered by the default dev-server-likely name list (node, deno, bun, npm/npx/yarn/pnpm, python, dotnet, cargo, tauri, ruby/rails, go). Custom name filter accepted. WMI command-line filtering deliberately deferred — name-only ships with a "may be false positive" caveat.
- `LocalhostPanel` gains a "Show all dev-server-likely processes" inline CheckBox (initial state from `CanarySettings.ShowTier3Processes`). When checked, heuristic processes not already in the Tier 1/2 enumeration append below with `Provenance = DevServerHeuristic` and a dimmer row color. Status footer shows e.g. "5 listening + 12 heuristic".
- `Canary.Settings.CanarySettings` — JSON-persisted per-user knobs at `%LocalAppData%\Canary\settings.json`. Three fields shipped: `UiMode` (stabilization / maturation), `ShowTier3Processes`, `RetentionDays` (default 14, range 1–365). Atomic save (write-to-.tmp + rename). Missing-file load returns defaults.
- `SettingsPanel` (Phase 7 placeholder) wires through to `CanarySettings.Load`/`Save`. Radio + checkbox + numeric input changes persist immediately. Status label confirms saves; `SettingsChanged` event for future consumers. The Stabilization radio's label clarifies that Maturation-mode panels are NOT in v1 per §C9 (only the toggle ships).
- `PastRunsPanel` quick-date filters per §C8: All / Last 7d / Last 30d toggle buttons highlight the active range (blue) and re-apply the substring filter on top. TableLayoutPanel column count bumped from 3 → 4 to accommodate.
- 8 new unit tests: 4 `Tier3HeuristicTests` (default name list shape, Enumerate doesn't throw, custom-filter constraint, empty-filter returns empty); 4 `CanarySettingsTests` (defaults, save+load round-trip, missing-file load returns defaults, settings file path shape).

### Added — Debug-overhaul Phase 7 (C4 UI overhaul, 2026-05-24)
- `Canary.UI.Navigation.INavMode` interface + 5 concrete implementations (`PastRunsNavMode`, `LocalhostNavMode`, `FeedbackNavMode`, `TelemetryNavMode`, `SettingsNavMode`) per design §C4. Each lazy-creates + caches its content control on first activation.
- `MainForm` wraps the existing TreeView + content `SplitContainer` inside a `TabControl`'s first "Tests" tab and adds 5 more tabs for the nav modes. Tab content is created on first selection; `_workloadsDir` is propagated to PastRuns + Telemetry panels on load. The historic Tests flow is preserved verbatim under the new shell.
- `PastRunsPanel` (`src/Canary.UI/Panels/PastRunsPanel.cs`) — operator-visible history of Phase 3 per-run dirs. SplitContainer with run list (When/Workload/Test/Verdict, color-coded) and REPORT.md preview. Filter textbox matches across workload + test name + verdict + run id. Refresh button re-scans. Verdict parsed from the REPORT.md first line ("# Canary run — <test> — <VERDICT>").
- `FeedbackPanel` (`src/Canary.UI/Panels/FeedbackPanel.cs`) — TreeView of `docs/feedback/{inbox,triaged,resolved}/*.md` items with markdown preview. Open-inbox-folder button. Discovery walks up from `AppContext.BaseDirectory`.
- `TelemetryPanel` (`src/Canary.UI/Panels/TelemetryPanel.cs`) — live tail of the most recent `telemetry.ndjson` (the Phase 2 per-suite NDJSON sink). 2s poll cycle when visible; re-reads only when LastWriteTimeUtc changes. ListView with time / kind / source / level / data summary; color-coded by level. Source filter dropdown (penumbra / qualia / rhino / canary-harness / all).
- `SettingsPanel` (`src/Canary.UI/Panels/SettingsPanel.cs`) — Stabilization / Maturation radio per design §C9 (in-memory only this phase; Phase 8 wires persistence + the actual UI-mode flip). Placeholder labels for Tier 3 toggle + retention slider.
- **Mode picker on the MainForm toolbar** — `ToolStripComboBox` with `pixel-diff` / `vlm` / `both`. Resolves the §A1 gap (GUI runs previously ignored `--mode`). `MainForm.OnRunTests` reads the picker (or the one-shot `_autoRunModeOverride` from CLI handoff) and passes to `TestRunnerPanel.RunAsync`.
- The Phase 4 interim Localhost popup-form launcher is replaced — the toolbar Localhost button now switches to the Localhost nav tab.
- `InternalsVisibleTo("Canary.Tests")` added to Canary.UI so tests can hit panel-internal helpers (`PastRunsPanel.ParseVerdict`, `FeedbackPanel.DiscoverFeedbackRoot`).
- 21 new unit tests: 16 `NavModeTests` (Theory across all 5 modes — name/description present, CreateContent returns Control, idempotent caching; plus unique-names assertion); 5 `PastRunsIndexTests` (verdict parser for standard / pass / missing-emdash / nonexistent-file cases; FeedbackPanel discovery sanity).
- **Scope notes for Phase 7:** the design doc's exact ASCII places the tab strip nested below the TreeView on the left pane. Phase 7 ships a simpler top-level TabControl wrapping the existing SplitContainer (preserves all current Tests behavior verbatim; reduces refactor blast radius). The INavMode contract is unchanged; future polish can rearrange placement. SettingsPanel persistence + Tier 3 toggle land in Phase 8. PastRuns annotation hand-off (per §C5/§C8) deferred — the existing ImageViewerForm Annotate button covers the operator path.

### Added — Debug-overhaul Phase 6 (C6 MCP server + C7 Tier 2 spawn registry, 2026-05-24)
- New `Canary.McpServer` csproj (`src/Canary.McpServer/`, `net8.0-windows`, OutputType Exe) — self-contained MCP server over stdio JSON-RPC per the MCP 2024-11-05 spec. No external NuGet dep (rolled the ~120-line protocol handler in-house for visibility + zero version churn). 8 tools registered: `list_feedback`, `get_feedback`, `mark_feedback_triaged`, `list_recent_runs`, `get_run_report`, `list_localhost_ports`, `list_running_apps`, `kill_localhost_port`. Each tool wraps the corresponding `Canary.Core` service (`FeedbackInboxWriter`, per-run dir scan, `LocalhostManager`, `SpawnRegistry`). Discovery walks up from `AppContext.BaseDirectory` to find `docs/feedback/` + `workloads/`.
- `Canary.Telemetry.SpawnRegistry` — Tier 2 of design §C7. Voluntary registration per operator decision Q3. Writes to `%LocalAppData%\Canary\claude-spawns\<session-id>.json`. `Default` singleton lazily creates a session file per Canary process; `Register(pid, name, commandLine, workingDirectory, port, intent)` + `Unregister(pid)` for callers; `LoadAllSessions()` for cross-session view (used by `LocalhostManager` overlay + `list_running_apps`); `PurgeOldSessions(maxAge)` for ops cleanup.
- `LocalhostManager.EnumeratePorts` now unions Tier 1 (netstat) + Tier 2 (spawn registry) — PIDs found in both sources get `PortProvenance.CanarySpawn` plus the intent string surfaced into the CommandLine field. Tier 3 (heuristic) still ships in Phase 8.
- `Penumbra.ViteManager`, `Qualia.ViteManager`, and `Canary.Cdp.ChromeLauncher` register their spawned processes with `SpawnRegistry.Default` at start + unregister on stop / dispose. Intent strings: `"Penumbra Vite dev server (port N, projectDir=X)"`, `"Qualia Vite dev server (port N, projectDir=X)"`, `"Chrome for CDP bridge (port N)"`.
- `docs/mcp-server.md` — tool table, `.mcp.json` setup snippet, discovery-root behavior, spawn registry storage path, stdio smoke command, wire-protocol note.
- 15 new unit tests: 5 `SpawnRegistryTests` (register persists, same-pid replaces, unregister, snapshot immutability, JSON round-trip via SessionDocument), 10 `McpServerToolDispatchTests` (every tool returns valid JSON; `GetFeedbackTool` + `GetRunReportTool` graceful not-found; `McpProtocol` round-trip for initialize + tools/list with 8 tools; tools/call invokes tool; unknown-tool JSON-RPC error; notifications produce no response; malformed JSON dropped).

### Added — Debug-overhaul Phase 5 (C5 sketch UI + C6 file-inbox half, 2026-05-24)
- WPF + WindowsFormsHost wiring in `Canary.UI.csproj` — `<UseWPF>true</UseWPF>` enables the design §C5 WPF-island annotation surface. Restored `System.IO` to implicit usings via explicit `<Using Include="System.IO" />` (UseWPF + UseWindowsForms combo drops it from defaults).
- `AnnotationCanvas` (`src/Canary.UI/Annotation/AnnotationCanvas.cs`) — custom WPF Canvas with Pointer / Rectangle / Freehand / Text tool modes, red/yellow/green color picker, source-image background at native resolution. Renders annotations + background to PNG via `RenderTargetBitmap`. Serializes vector annotations to JSON per the §C5 schema (shape array with rect/freehand/text discriminator).
- `AnnotatedImageForm` (same dir) — WinForm hosting `AnnotationCanvas` via `ElementHost`. Dark-themed toolbar, title + body text boxes, Save / Cancel buttons. Save invokes `FeedbackInboxWriter` and reports the resulting slug via the status label.
- `Canary.Feedback` namespace (`src/Canary.Core/Feedback/`): `FeedbackItem` POCO, `FeedbackSlugGenerator` (per-date sequence-counting, 3-to-5-word title slugify), `FeedbackInboxWriter` (atomic per-file writes producing `<slug>.md` + sidecar `<slug>/{source,annotated}.png` + `annotations.json`).
- `docs/feedback/{inbox,triaged,resolved}/` tree created with `.gitkeep` markers + `docs/feedback/README.md` documenting layout, slug format, lifecycle, and item shape.
- `CLAUDE.md` gains a "Feedback inbox" section pointing to the convention; session-start scan rule documented.
- `ImageViewerForm` toolbar gets an Annotate button that opens the current image in `AnnotatedImageForm`. Inbox root discovered by walking up from `AppContext.BaseDirectory` looking for `docs/feedback/`.
- 13 new unit tests: 7 `FeedbackSlugGeneratorTests` covering first-of-day numbering, sequence continuation, date rollover, max-five-words clamp, punctuation strip, empty-title fallback, malformed-existing-slug skip; 5 `FeedbackInboxWriterTests` covering disk layout, markdown frontmatter shape, null-ref omission, ExistingSlugs enumeration, empty-dir behavior.

### Added — Debug-overhaul Phase 4 (C7 Tier 1 localhost manager, 2026-05-24)
- New `Canary.Localhost` namespace (`src/Canary.Core/Localhost/`): `PortEntry` record + `PortProvenance` enum (Unknown / DevServerHeuristic / CanarySpawn / CanaryHarness) + `LocalhostManager`. Tier 1 of design §C7 — passive port enumeration via `netstat -ano` + `Process.GetProcessById` enrichment, plus authoritative `IPGlobalProperties.GetActiveTcpListeners` for Canary's own listeners. Default port filter list: 3000, 3001, 4173, 4200, 5173, 5174, 8000, 8080, 8081, 1420. `KillByPortAsync` succeeds the duplicate `ViteManager.KillStaleListenerAsync` helpers.
- Penumbra + Qualia `ViteManager.KillStaleListenerAsync` now delegate to `LocalhostManager.KillByPortAsync` — ~100 lines of duplicated netstat + taskkill code removed per workload.
- `LocalhostPanel` UserControl in `Canary.UI.Controls` — interim drop-in surface (per impl §6 + design Implementation Plan deviation #2). Polls every 2s when visible. ListView of port / pid / process / provenance / start-time / path; per-row kill action with confirmation. Wired via a new "Localhost" toolbar button on MainForm that opens it in a popup form; Phase 7's INavMode refactor migrates this into a proper Localhost nav tab.
- 9 new unit tests covering netstat parsing for IPv4 / IPv6 listeners, ignoring ESTABLISHED / TIME_WAIT / UDP, malformed-line skip, default port list shape, PortProvenance enum default, and a real-machine `EnumeratePorts` smoke that asserts the API doesn't throw and returns plausible ports.
- **Tier 2 (SpawnRegistry, voluntary)** ships in Phase 6 alongside the MCP server. **Tier 3 (name heuristic)** ships in Phase 8.

### Added — Debug-overhaul Phase 3 (C2 Claude-readable REPORT.md + per-run dir, 2026-05-24)
- `Canary.Reporting.MarkdownReportGenerator` — generates a per-run `REPORT.md` next to `result.json` with the stable section structure from design §C2: header (test name + verdict + run id + workload + mode + timing) → Verdict summary → Checkpoints table → Errors (only when present) → VLM evaluations (only when present) → Files. Cross-link convention uses relative Markdown links (`[baseline](../baselines/<name>.png)`) so the report renders correctly when opened from its location at `<test>/runs/<timestamp>/REPORT.md`.
- Per-run dir layout: `TestRunner` writes both `result.json` and `REPORT.md` to `workloads/<w>/results/[<suite>/]<test>/runs/<timestamp>/` at the end of every `RunTestAsync` / `RunAgentTestAsync` call. Timestamp format: `yyyyMMdd-HHmmss-xxxx` (4 hex chars to avoid same-second collisions in suite mode). Both CLI runs and GUI runs hit the same code path — CLI parity per design §C2 (prior to this, CLI didn't write per-test result.json at all).
- Baselines, candidates, diffs, composite.png stay flat at `<test>/` for Phase 3 (overwriting per run). Phase 7 PastRuns can revisit if image-history preservation becomes required. `MarkdownReportGenerator` links account for the layout via `../<dir>/`.
- `ResultsHistory.ScanAsync` extended to dual-shape scan: recursive `result.json` walk picks up both `<test>/result.json` (legacy) AND `<test>/runs/<timestamp>/result.json` (new). New layout entries don't displace legacy — both surface in the returned list, sorted by file timestamp desc.
- `Canary.Maintenance.ResultRetention` — `PurgeOlderThan(workloadsDir, TimeSpan)` walks every `results/.../runs/<timestamp>/` directory and removes those older than the threshold. Default retention 14 days (matches `STANDARD.md` §16 candidates/diffs convention). Legacy flat-layout artifacts are intentionally untouched. Returns a report with scanned / purged / bytes-freed counts.
- `TestRunnerPanel.RunAsync` no longer writes per-test `result.json` itself — `TestRunner` now owns the write (CLI parity goal). Existing in-UI behavior (ResultsHistory feed, RunCompleted event) is unchanged.
- 15 new unit tests (8 `MarkdownReportGeneratorTests` covering header / required sections / runId / link relative paths / errors-section-conditional / VLM-section-conditional / telemetry-footer-conditional / table-header; 3 `ResultsHistoryDualShapeTests` for legacy-only / per-run-only / both layouts; 4 `ResultRetentionTests` for old-dir purge / legacy untouched / non-existent root no-op / default value).

### Added — Debug-overhaul Phase 2 (C1 universal telemetry envelope, 2026-05-24)
- `Canary.Telemetry` namespace (`src/Canary.Core/Telemetry/`): `TelemetryRecord` POCO + `TelemetryKind` enum + `ITelemetrySink` + `NullTelemetrySink` + `CompositeTelemetrySink` + `ITelemetryAware` + `NdjsonFileSink` + `EventStreamSink`. One JSON envelope for every workload agent's telemetry per design §C1; serialized as NDJSON (one record per line). Kind discriminator + camelCase property names; null fields omitted; 500 KB per-line cap with truncation marker on overflow.
- `Canary.Cdp.CdpClient.Subscribe(method, handler)` — continuous CDP-event subscription API (returns `IDisposable` for detach). Coexists with the existing one-shot `WaitForEventAsync` waiters: events fan to both surfaces, so a long-lived telemetry subscriber doesn't preclude a transient wait.
- `Canary.Cdp.CdpTelemetryStream.EnableAndSubscribeAsync(cdp, sink, source, ct)` — shared helper that enables `Runtime` + `Console` + `Log` + `Network` CDP domains and registers subscribers translating `Runtime.consoleAPICalled`, `Log.entryAdded`, `Network.requestWillBeSent`, `Network.responseReceived`, `Network.loadingFailed` into `TelemetryRecord`s. Network records carry duration via per-request `Stopwatch.GetTimestamp` deltas. Used by both `PenumbraBridgeAgent` and `QualiaBridgeAgent`.
- `PenumbraBridgeAgent` + `QualiaBridgeAgent` implement `ITelemetryAware`. `TestRunner.TelemetrySink` is registered on the agent before `InitializeAsync` so CDP subscriptions are live from the start of the run. Subscriptions detach on agent `Dispose`.
- `Canary.Orchestration.TestRunner.TelemetrySink` property (default `NullTelemetrySink.Instance`); `RunCommand` instantiates a per-suite `NdjsonFileSink` at `workloads/<w>/results/[<suite>/]telemetry.ndjson` alongside the existing `result.json`. Phase 3 moves both into `runs/<timestamp>/`.
- `ITestProgressEvents.OnTelemetry(TelemetryRecord)` default interface method (no-op default; the Phase 7 Telemetry tab will subscribe).
- 12 new unit tests (5 `TelemetryRecordSerializationTests` covering envelope shape / kind enum / camelCase / null-omission / ISO 8601; 7 `NdjsonFileSinkTests` covering single + multi line, 8-writer concurrency without torn writes, `CompositeTelemetrySink` fan-out + isolation, `NullTelemetrySink` + `EventStreamSink` contracts).
- **Deferrals (documented per impl §4):** Rhino-side console interception not landed in this phase — `RhinoCommon` 8 doesn't expose a clean `RhinoApp.WriteLine` intercept hook; queued for a v2 follow-up. `InputReplayer` wrapping deferred — refactoring it to take a sink crosses Phase 7 territory. `ProcessManager.Track` agent-action records deferred to Phase 6 (when `SpawnRegistry` lands per §C7 Tier 2). Live CDP integration tests deferred to operator-side smoke — they require Chrome + Vite for the workload under test.

### Added — Debug-overhaul Phase 1 (C3 non-headless enforcement, 2026-05-24)
- CLI now launches `Canary.UI.exe` by default for `canary run` invocations and exits 0 — implements `STANDARD.md` §16 locked rule 8. `--headless` flag bypasses for CI / scripted use (and `--quiet` implies `--headless`). UI is located via search order: same directory as `canary.exe` → sibling solution layout (`Canary.UI/bin/{Release|Debug}/net8.0-windows/Canary.UI.exe`) → `Canary UI.lnk` shortcut. If no UI exe found, falls through to today's text-only path with a one-line warning.
- UI single-instance behavior: a second `canary run` (or direct UI launch) detects the first via a `Global\Canary.UI.SingleInstance` mutex and forwards its auto-run args via a named pipe (`canary-ui-singleinstance-pipe`) to the running instance, which dispatches them through the same code path as a tree-click → Run Tests button press. Operator decision Q5.
- `Canary.Cli.AutoRunArgs` POCO (in `Canary.Core`) carries the auto-run-relevant subset of CLI args (`--workload`, `--test`, `--suite`, `--mode`) with JSON + argv round-trips. Used by both the CLI-handoff path and the pipe-forwarded path.
- `Canary.UiLocator` helper (in `Canary.Harness`) — file/COM-based search for `Canary.UI.exe` with the search order above.
- `Canary.UI.SingleInstancePipeServer` + `SingleInstancePipeClient` — async one-shot named-pipe server + fire-and-forget client; the server raises `AutoRunRequested` on the pipe-loop thread which `Program.cs` marshals to the UI thread.
- `MainForm.AutoRunAsync(AutoRunArgs)` — workload-tree-aware auto-run driver. Polls up to 10s for the workloads tree to populate (handles the constructor's fire-and-forget `AutoDetectWorkloadsDir`), selects the matching workload / test / suite node, sets the one-shot `_autoRunModeOverride`, then triggers the existing `OnRunTests` handler.
- `TestRunnerPanel.RunAsync` gains an optional `modeOverride` parameter (default `PixelDiff`) — wires the mode through to `TestRunner.ModeOverride`. The corresponding UI mode picker arrives in Phase 7.
- 13 new unit tests (`AutoRunArgsTests` × 10 round-trip / parse / IsEmpty cases; `HeadlessFlagTests` × 3 flag-accepted / help-mentions / UI-locator-contract).
- 2 new integration tests (`SingleInstancePipeTests` — full pipe round-trip; client-without-server returns quickly without throwing).

### Fixed — Debug-overhaul implementation (2026-05-24)
- CLI exit code: `canary run` now returns `1` when any test fails or crashes (`0` when all pass; `New` baselines count as pass). Previously `RunCommand.RunAsync` was void-returning and CLI always exited `0` — silent false-positives for any CI consumer. Regression against `spec/PHASES.md` Phase 4. Bug 0007. Precursor commit before Phase 1 of the debug-overhaul implementation (`MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md`).

### Added — Debug-overhaul audit (2026-05-24)
- `docs/research/2026-05-24-canary-surface-audit.md` — full inventory of Canary's UI / CLI / agent / report / localhost / screenshot surface. Phase A output of the audit prompt `MultiVerse/prompts/canary-debug-overhaul-audit-2026-05-24.md`. Drives the debug-overhaul design (Phase C, in flight) — telemetry envelope, Claude-readable REPORT.md, non-headless enforcement, sketch+annotate feedback, tiered localhost manager.
- `docs/research/2026-05-24-test-harness-prior-art.md` — Phase B prior-art survey. Three references (Playwright Inspector + Trace Viewer, Cypress App + Cloud, Sysinternals Process Explorer) with steal / skip per reference and a cross-tool synthesis table mapping conventions to Canary's §C design sections.
- `docs/plans/2026-05-24-canary-debug-overhaul.md` — Phase C design proposal. Nine design sections (C1 universal telemetry envelope, C2 Claude-readable REPORT.md, C3 non-headless enforcement, C4 UI overhaul, C5 sketch+annotate, C6 feedback channel (file inbox + MCP server), C7 tiered localhost manager, C8 live+past-runs browser, C9 VLM/visual-regression demotion path) plus Implementation Plan appendix proposing a 9-phase ordering totaling ~9.5–11.5 weeks of focused work (with a Phases 1–4 v1 cut at ~4.5 weeks delivering ~70% of operator-visible value). First entry under new `docs/plans/` directory in Canary.

### Added — Qualia workload (backfill May 2026; promoted from Stub → Active)
- `spec/QUALIA_WORKLOAD.md` workload specification (2026-05-24) — promoted from `workloads/qualia/AGENT_NOTES.md` per the 2026-05-24 testing+Canary audit (`MultiVerse/audit/2026-05-24-testing-canary-audit-and-plan.md`).
- `spec/PEERS.md` Qualia section (2026-05-24) — documents the Canary↔Qualia contract (hook stability, port co-existence with Penumbra, baseline conventions, hook-addition workflow).
- Qualia workload buildout (2026-05-08) — initial commit `13771d9`. New `Canary.Agent.Qualia` CDP bridge agent, `workloads/qualia/workload.json` (agentType `qualia-cdp`, CDP port 9223, Vite port 5173 for co-existence with Penumbra), 5 fixtures + `landing-screen.json` suite. Pixel-diff + VLM (gemma4:e4b) supported. First fixture pins the May-8 LandingScreen overlap bug as a VLM regression.
- Wave 0.B Playground hooks for Qualia workload (2026-05-10) — gated by Qualia's `debug.playground` module; scenario + snapshot hooks (random / grid / tree / scale-free / stress-1k / stress-10k); `playground.json` suite (one test per scenario + snapshot round-trip).
- qualia-v4-ui suite (2026-05-12) — pointer / qverse / RAG UI fixtures. `__canaryPersona*` rename (legacy `__canaryModule*` aliases preserved for one transition release).
- RH-2 multi-display sweep (2026-05-14) — `workloads/qualia/suites/multi-display.json` with 11 `rh2-*.json` tests covering theme / junction / edge-width / bloom / particulate variants on Qualia's `examples/minimal/.qualia` (9 nodes / 4 contexts / 1 ghost / 1 cross-context edge). Three new Qualia hooks: `__canaryApplyPerfSnapshot`, `__canaryWaitForRenderSettled`, `__canaryLoadMinimalSample`. Tests use `setup.commands` raw JS — no new C# bridge action needed.
- Diagnostic dump hook (`__canaryDumpDiagnostics`, 2026-05-19) — renders pipeline state (persona enable map / edge type colors / tone mapping / bg / fog) as a top-right overlay before the next screenshot; dumps to console for offline capture. Pencil-profile debugging convention.
- README.md workload table updated: Qualia row `Browser (Vite + Chrome via CDP) | Canary.Agent.Qualia | Active — 6 suites / 78 tests` (was `Custom viewer | Built-in module | Stub`).

### Added
- File-source checkpoints: `TestCheckpoint` gains `source` (default `"viewport"`) and `panelNickname` fields — when `source: "file"`, the runner reads a file path from a GH panel instead of capturing a viewport screenshot, then copies it into candidates for normal pixel-diff comparison. Enables Pigture Cycles render output to flow through the checkpoint pipeline.
- VLM Oracle comparison mode (Phase 8): checkpoints with `mode: "vlm"` evaluate screenshots against natural-language descriptions using a Vision-Language Model, returning pass/fail verdicts without requiring baseline images
- `VlmConfig` type for configuring VLM provider/model in test setup
- `ClaudeVlmProvider`: calls Anthropic Messages API with base64 screenshot + description prompt
- `VlmEvaluator` factory with API key resolution from `CANARY_VLM_API_KEY` / `ANTHROPIC_API_KEY`
- Mixed-mode test support: pixel-diff and VLM checkpoints can coexist in the same test
- HTML report: VLM detail sections showing description, reasoning, confidence, and screenshot
- `OllamaVlmProvider`: calls local Ollama instance for VLM evaluation — no API key required, supports any vision-capable model (e.g., `gemma4:e4b`)
- 26 unit tests for VLM feature (98 total)
- 7 Penumbra VLM oracle test definitions: `vlm-tape-csg-geometry`, `vlm-atlas-blob-organic`, `vlm-cornell-box-layout`, `vlm-teapot-shape`, `vlm-multi-field-separation`, `vlm-terrain-landscape`, `vlm-teapot-metal-mixed`
- `vlm` suite definition (7 tests) for the Penumbra workload
- Full suite updated to 51 tests (was 44)

### Fixed
- Atlas cell boundary rectangular artifact in Penumbra — face-neighbor expansion in cascade manager ([bug 0005](docs/bugs/0005-atlas-cell-boundary-artifact.md))

### Added
- 10 new CPig gap buildout test entries: `cpig-24-shortest-path`, `cpig-25-feature-edges`, `cpig-26-graph-export`, `cpig-27-svg-export`, `cpig-28-unroll-overlap`, `cpig-29-unroll-tabs`, `cpig-30-vdb-morphology`, `cpig-31-vdb-nary-boolean`, `cpig-32-geodesic`, `cpig-33-adaptive-mesh`
- CPig suite updated to 33 tests (was 23) — covers graph shortest path, feature edges, export formats, unroll overlap/tabs, VDB morphology, geodesic distance, and adaptive meshing
- 5 new CPig field modifier test definitions: `cpig-19-noise-field`, `cpig-20-domain-modifiers`, `cpig-21-field-mix`, `cpig-22-turbulence`, `cpig-23-displace-mesh`
- CPig suite updated to 22 tests (was 17) — covers noise, domain modifiers, value modifiers, and geometry effects
- `--keep-open` flag for `canary run` — keeps the target application open after tests complete for manual inspection (press Ctrl+C to close)
- Phase 9 material preset tests: 5 new Canary tests (metal, wood, marble, zebra, damascus on SDF Teapot)
- Phase 9 environment preset tests: 4 atlas-blob env tests (outdoor, sunset, night, neutral) + 2 custom lighting/bg tests
- `materials.json` + `environment.json` suite definitions
- Full suite updated to 44 tests (was 33)
- Phase 7+8 effects regression suite: 8 new Canary tests (fog, ACES, exposure, emissive, Fresnel, contours, onion, noise) exercising Penumbra shader effects via CDP hooks
- `effects.json` suite definition for the 8 Phase 7+8 effect tests
- Full suite updated to 33 tests (was 25)
- Named test suites: `--suite` CLI option for running grouped subsets of tests (e.g., `canary run --workload penumbra --suite smoke`)
- `SuiteDefinition` model class for suite JSON files (`workloads/{workload}/suites/*.json`)
- Suite discovery and test resolution in `TestDiscovery` (`DiscoverSuitesAsync`, `DiscoverTestsForSuiteAsync`)
- Suite-scoped results directories: `results/{suiteName}/{testName}/` — running one suite never clobbers another's baselines/candidates/diffs
- Suite-scoped reports: `results/{suiteName}/report.html` and `junit.xml`
- 5 Penumbra suite definitions: smoke (4 tests), scenes (10), display-modes (8), overlays (7), full (25)
- Documentation system: `docs/` directory with bugs, decisions, debug-sessions, features, and templates
- Auto-journaling rules in CLAUDE.md for maintaining living documentation
- 20 new Penumbra visual regression test JSONs covering all 10 scenes, display modes, and overlays (25 total)
- Penumbra display mode control hooks: `__canarySetDisplayMode`, `__canaryGetAvailableModes`, `__canaryGetDisplayMode`
- Visual expectations reference: `workloads/penumbra/VISUAL_EXPECTATIONS.md`

## [0.4.0] - 2026-04-24

### Added
- Penumbra CDP bridge agent (`Canary.Agent.Penumbra`) for browser-based visual regression testing
- CDP client library (`CdpClient`, `ChromeLauncher`) in `Canary.Core/Cdp/`
- Vite dev server management (`ViteManager`) for Penumbra test harness
- Penumbra test definitions: `tape-csg-orbit`, `atlas-blob-orbit`, `multi-field-orbit`, `stress-test-orbit`, `atlas-blob-bricks-overlay`
- Penumbra-side hooks (`canary-hooks.ts`) for scene/camera/animation control
- Workload documentation: `AGENT_NOTES.md`, `CLAUDE_CODE_RUNNER.md`, `BRICK_OVERLAY_TEST.md`

### Fixed
- Brick overlay invisible due to row-major/column-major matrix swap in Penumbra ([bug 0001](docs/bugs/0001-brick-overlay-invisible.md))
- Register overflow in 64-sphere stress test -- compiler never recycled registers ([bug 0002](docs/bugs/0002-register-overflow-64-sphere.md))
- coarseRes initialization ordering -- `initAtlas()` called before `computeCascadeParams()` ([bug 0003](docs/bugs/0003-coarse-res-ordering.md))
- atlas-diagnostic.ts temporal dead zone error ([bug 0004](docs/bugs/0004-atlas-diagnostic-tdz.md))

## [0.3.0] - 2026-04-11

### Added
- WinForms GUI application (`Canary.UI`) with dark theme (Phase 9)
- Results viewer with baseline/candidate/diff image comparison (Phase 10)
- Full-size image viewer with zoom and pan (Phase 10)
- Test definition editor with validation (Phase 11)
- Workload configuration editor (Phase 11)
- Test runner panel with live progress and stop button (Phase 11)
- Recording panel for input capture (Phase 11)
- Keyboard shortcuts: Ctrl+O, Ctrl+R, F5, Ctrl+Shift+R, Ctrl+A, Delete, Escape (Phase 12)
- Drag-and-drop support for `.json` and `.3dm` files (Phase 12)
- Context menus for workload and test tree nodes (Phase 12)

### Changed
- Extracted `Canary.Core` shared library from `Canary.Harness` (Phase 8)
- `TestRunner` now accepts `ITestLogger` via constructor injection (Phase 8)
- Extracted `BaselineManager` and `TestDiscovery` into Core (Phase 8)

### Fixed
- Rhino plugin now outputs `.rhp` extension correctly (Phase 8)

## [0.2.0] - 2026-04-04

### Added
- Rhino workload agent (`Canary.Agent.Rhino`) with RhinoCommon API integration (Phase 6)
- Workload stubs for Qualia and Penumbra (Phase 7)
- Agent creation guide: `docs/creating-a-workload.md` (Phase 7)
- HTML report generator with dark theme, embedded base64 images, status badges (Phase 5)
- JUnit XML report generator for CI integration (Phase 5)
- `canary report` command to open most recent report (Phase 5)
- `--verbose` and `--quiet` CLI flags (Phase 5)
- Color-coded console output: green/red/magenta/yellow for PASS/FAIL/CRASH/NEW (Phase 5)

## [0.1.0] - 2026-04-04

### Added
- Solution scaffold with `Canary.sln`, CLI shell, Ctrl+C handler (Phase 0)
- Named pipe IPC with JSON-RPC protocol (Phase 1)
- `ICanaryAgent` interface with Execute, CaptureScreenshot, Heartbeat, Abort (Phase 1)
- `AgentServer` and `HarnessClient` for bidirectional communication (Phase 1)
- Input recording and replay via Win32 `SendInput` (Phase 2)
- `ViewportLocator` for window discovery and coordinate normalization (Phase 2)
- Pixel diff comparer with configurable color threshold (Phase 3)
- SSIM comparer with 8x8 sliding window (Phase 3)
- Composite image builder (baseline | candidate | diff) (Phase 3)
- Test runner orchestrating full lifecycle: launch, connect, replay, capture, compare (Phase 4)
- App launcher, process manager, watchdog with 3-miss heartbeat timeout (Phase 4)
- `canary run` and `canary approve` CLI commands (Phase 4)
- 52 unit tests covering all core functionality
