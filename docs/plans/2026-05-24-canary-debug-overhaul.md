---
date: 2026-05-24
tags: [plan, canary, debug, ui, telemetry, debug-overhaul]
status: shipped
project: canary
component: full-surface
---

> **Shipped 2026-05-24.** All 9 design phases landed across ~30 commits.
> Driving prompt: `MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md`.
> Progress log with per-phase detail: `docs/progress/2026-05-24-canary-debug-overhaul.md`.
> Retrospective appended below.


# Canary debug-overhaul — design proposal

Parent prompt: `MultiVerse/prompts/canary-debug-overhaul-audit-2026-05-24.md` (Phase C).
Inputs: `docs/research/2026-05-24-canary-surface-audit.md` (Phase A), `docs/research/2026-05-24-test-harness-prior-art.md` (Phase B).
Child prompt (to be written after operator review): `MultiVerse/prompts/canary-debug-overhaul-implement-YYYY-MM-DD.md`.

## Executive summary

Canary evolves from "visual regression harness" into "the debugging cockpit Claude reads from and the operator drives from." Nine asks from the operator translate into nine design sections (C1–C9) covering: a universal telemetry envelope that captures console + network + click/key + heartbeat-state uniformly across Rhino / Penumbra / Qualia; a Claude-readable `REPORT.md` per run that supplements (not replaces) `result.json` + `report.html`; CLI-launches-UI enforcement implementing `STANDARD.md` §16 locked rule 8; a UI overhaul keeping WinForms but adding telemetry + past-runs + localhost panels (with WPF islands via `WindowsFormsHost` only where annotation needs it); a sketch+annotate surface with PNG-overlay + JSON-coords storage; a file-based feedback inbox (canonical) wrapped by an MCP server (live convenience); a tiered localhost manager (passive port enumeration + Claude-spawn registry + name-heuristic listing); a live-+-past-runs browser that opens snapshots without re-running; and a settings-toggleable demotion path for VLM and pixel-diff that keeps them functional but moves them out of the headline UI.

VLM and pixel-diff stay throughout. Headless mode stays available behind `--headless`. The named-pipe + CDP plumbing stays. The overhaul **adds and reorganizes**, it doesn't rewrite.

## Locked defaults (recap from audit prompt §0.3)

- Doc location for plans: this file. New `docs/plans/` directory created in Canary; precedent in Qualia.
- Frontmatter schema: `date / tags / status / project / component` per `Canary/AGENTS.md` auto-journaling rule 7.
- IPC for new features: named pipes + JSON-RPC (no HTTP, no sockets) per `Canary/AGENTS.md` Key Rules.
- MCP transport: stdio.
- Annotation storage: PNG + JSON sidecar (diffable, viewable original-untouched).
- Tier 1 ports: 3000, 3001, 4173, 4200, 5173, 5174, 8000, 8080, 8081, 1420, plus any port Canary itself binds.

---

## C1 — Universal telemetry envelope

### Problem

Per §A3 of the surface audit, telemetry capture is workload-private and inconsistent. Rhino emits a state-dict heartbeat + a hardcoded diagnostic file. Penumbra + Qualia emit a state-dict heartbeat from `__canaryGetRendererInfo` / `__canaryGetAppInfo` — no console capture, no network capture, no click/key log. The `ITestLogger` interface produces unstructured text (`[HH:mm:ss] [Canary] message`). Claude has no canonical shape to ingest.

### Proposal

**One JSON envelope shape** — `TelemetryRecord` — emitted by every workload agent and aggregated by `TestRunner` into a per-run NDJSON stream `runs/<timestamp>/telemetry.ndjson`.

```json
{
  "t": "2026-05-24T14:23:01.123Z",  // ISO 8601
  "runId": "20260524-142300-a3f1",
  "testName": "diag-pencil-baseline",
  "checkpointName": "diag-pencil-baseline",
  "kind": "console" | "network" | "input" | "agent-state" | "agent-action" | "log" | "screenshot",
  "level": "info" | "warn" | "error" | "debug",
  "source": "rhino" | "penumbra" | "qualia" | "canary-harness",
  "data": { ... }  // kind-specific payload
}
```

**Kind-specific payloads:**

| `kind` | `data` shape | Producer |
|---|---|---|
| `console` | `{ text, args, sourceUrl, lineNumber }` | CDP `Runtime.consoleAPICalled` (Penumbra + Qualia); future Rhino `RhinoApp.WriteLine` subscription. |
| `network` | `{ method, url, status, durationMs, errorText }` | CDP `Network.responseReceived` + `Network.loadingFailed`. Skip the body (size). |
| `input` | `{ type: "mouse"|"key", subtype, vx, vy, key, button }` | Logged by CDP `Input.dispatchMouseEvent` wrappers (Penumbra + Qualia) and `InputReplayer.InjectMouseMove` (Rhino). |
| `agent-state` | The existing `HeartbeatResult.State` dict. | Heartbeat poll, every 2s. |
| `agent-action` | `{ action, params, success, message, durationMs }` | Wrap every `ExecuteAsync` call. |
| `log` | `{ text }` | The existing `ITestLogger.Log` text, captured into the stream. |
| `screenshot` | `{ path, width, height }` | After `CaptureScreenshotAsync` writes the file. |

**Implementation surface:**
- New `Canary.Core.Telemetry` namespace (`src/Canary.Core/Telemetry/`).
- `TelemetryRecord` POCO + `JsonSerializerContext` for source-generated serialization (perf — high event volume).
- `TelemetrySink` interface + `NdjsonFileSink` (writes to `runs/<timestamp>/telemetry.ndjson`) + `EventStreamSink` (fanout to `ITestProgressEvents` for live UI).
- `ITestProgressEvents` extended with `OnTelemetry(TelemetryRecord)`; existing methods (`OnTestStarted`, `OnCheckpointStarted`, …) become convenience wrappers that emit a typed `TelemetryRecord`.
- Each agent gains a `RegisterTelemetrySink(ITelemetrySink)` method called by `TestRunner` post-construction.
- CDP-side: agents enable `Console`, `Network`, `Runtime` domains on `InitializeAsync` and subscribe to `Runtime.consoleAPICalled` / `Network.*` events via a new `CdpClient.OnEvent(string method, Action<JsonNode>)` subscription API.
- Rhino-side (v1): wrap `RhinoApp.WriteLine` interception via the existing `Rhino.Commands.Command` events; emit `console` records. Phase 2 can subscribe to GH solver events for richer telemetry.

### Open questions

- **STATUS: unresolved — does CDP `Console` capture include `console.log` from page scripts or only from the runtime layer?** Both: `Runtime.consoleAPICalled` covers `console.*` from page JS; `Log.entryAdded` covers browser-internal warnings (XHR errors, etc.). For Canary v1, enable both.
- **Network filter scope:** capture all requests or filter to non-Vite-asset requests? Recommendation: capture all initially, add a filter parameter to `RegisterTelemetrySink` later if volume becomes a problem (Penumbra dev pages can hit 200+ HMR requests).
- **STATUS: unresolved — Rhino console interception API.** `RhinoApp.WriteLine` is one-way; the cleanest interception path is `RhinoApp.CommandLineOut += ...` (if available in RhinoCommon 8) or a `TextWriter` swap. Investigate during implementation.

### Effort: L
- New telemetry namespace + serialization (~3 days).
- CDP domain enabling + event subscription in both Penumbra + Qualia bridge agents (~2 days each).
- Rhino-side console interception (~2 days; gated on the API question above).
- `ITestProgressEvents` extension + GUI hook-up (~1 day).

### Dependencies
- None upstream.
- Downstream of this: C2 (REPORT.md ingests the NDJSON), C4 (telemetry panel renders the live stream), C8 (past-runs browser scrolls through it).

---

## C2 — Claude-readable REPORT.md

### Problem

Per §A4, reconstructing a failing run today requires reading `result.json` + `report.html` + the UI's transient run-log + per-workload side channels (`agent_viewport_diag.log`, Slop LogHub file). Five paths, no inline summary. The HTML report has the data but is base64-image-bloated and not easy to grep.

### Proposal

Generate `workloads/<w>/results/[<suite>/]<test>/runs/<timestamp>/REPORT.md` per run alongside the existing `result.json` (which moves into the same dir). One Markdown file per run; stable section structure for parsing.

**Required sections, in order:**

```markdown
# Canary run — <test-name> — <verdict>

> Run ID: `<timestamp>-<short-hash>`
> Workload: <displayName> (<agentType>) | Mode: pixel-diff | Duration: 12.3s
> Started: 2026-05-24T14:23:01Z | Finished: 2026-05-24T14:23:13Z

## Verdict
**FAIL** — 1 of 3 checkpoints failed; 2 console errors; 1 network failure.

## Checkpoints
| # | Name | Status | Mode | Diff | Confidence | Duration | Links |
|---|------|--------|------|------|-----------|----------|-------|
| 1 | init | PASS | pixel-diff | 0.1% | — | 1.2s | [baseline](baselines/init.png) · [candidate](candidates/init.png) · [diff](diffs/init.png) |
| 2 | after-click | FAIL | pixel-diff | 12.4% | — | 0.8s | [baseline](baselines/after-click.png) · [candidate](candidates/after-click.png) · [diff](diffs/after-click.png) |

## Errors and warnings
- (console / network / agent-action errors, in order, inline)

## Console output (last 50 lines)
```
<plaintext tail>
```
[Full console log](telemetry.ndjson)

## Network failures
- `GET http://localhost:5173/missing.json → 404 (5ms)`

## Agent actions
| # | Action | Params | OK? | Message |
| ... |

## Input dispatched (last 20 events)
- t+0ms · mouseMoved (0.50, 0.50)
- t+15ms · mousePressed (0.50, 0.50) left
...

## Files
- result.json — typed verdict (input to UI / CI tooling)
- telemetry.ndjson — full event stream (input to Claude / external tooling)
- composite.png — baseline | candidate | diff strips
- baselines/ candidates/ diffs/ — per-checkpoint images
```

**Tunable caps (locked defaults from prompt §7):** last 50 console lines, all network errors, last 20 input events. More via the links to the full NDJSON.

**Cross-link via `computer://` URIs:** `[diff](diffs/after-click.png)` works as a relative Markdown link; the operator clicking it in Obsidian / VS Code Markdown preview opens the image. No special URI scheme needed for v1; relative paths suffice.

**Implementation surface:**
- `Canary.Core.Reporting.MarkdownReportGenerator` (new file) alongside `HtmlReportGenerator`, `JUnitReportGenerator`.
- Takes `TestResult` + telemetry NDJSON path (or stream) + run metadata; produces Markdown string.
- Called from both `RunCommand.RunAsync` (CLI) AND `TestRunnerPanel.RunAsync` (GUI) — currently the CLI path doesn't write per-test `result.json` at all; this design unifies the two paths.

### Open questions

- **Per-run dir naming:** `<timestamp>` or `<timestamp>-<short-hash-of-test-config>`? Recommendation: timestamp only (sortable, simple). Hash collisions across same-second runs are vanishingly unlikely.
- **Retention:** `STANDARD.md` §16 says candidates/diffs auto-clean after 14 days. Should `REPORT.md` + `telemetry.ndjson` follow the same retention or stay longer? Recommendation: per-run dirs purge together (single retention knob). Operator can keep specific runs by moving them out of `runs/`.
- **STATUS: unresolved — backward compat with the current overwriting layout.** Existing `workloads/<w>/results/<test>/result.json` (no `runs/` subdir) is referenced by `ResultsHistory.ScanAsync` and the UI. Migration: scan both shapes; new path wins; old path is read-only legacy. Decide whether to migrate-on-first-write or leave both forever.

### Effort: M
- Markdown generator + tests (~2 days).
- Re-pathing the result tree to `runs/<timestamp>/` + migration shim (~2 days).
- Update `ResultsHistory.ScanAsync` to read both layouts (~1 day).

### Dependencies
- **Upstream:** C1 (telemetry NDJSON is the input for the "Errors and warnings" + "Console" + "Network" sections).
- **Downstream:** C8 (past-runs browser opens the per-run dir).

---

## C3 — Non-headless enforcement (implement §16 rule 8)

### Problem

Per §A5: source grep for `Canary.UI.exe` returns zero matches. The CLI does not launch the UI. Operators today open the UI manually then click Run Tests. The §STANDARD.md §16 locked rule 8 names this as queued V1 work.

### Proposal

**Flip the CLI default:** `canary run` launches `Canary.UI.exe` with auto-run args; `canary run --headless` (or the existing absence of UI co-location) bypasses to today's text-only path.

**CLI side (`src/Canary.Harness/Cli/RunCommand.cs`):**

```csharp
var headlessOption = new Option<bool>(
    "--headless",
    "Run without launching the Canary UI. Required for CI; default is to open UI.");

// In handler:
if (!headless && CanLocateUiExe(out var uiPath)) {
    LaunchUiWithAutoRun(uiPath, workload, test, suite, mode);
    return 0;
}
// else fall through to today's text-only path
```

`CanLocateUiExe` searches: same directory as `canary.exe` → sibling `Canary.UI/bin/Release/net8.0-windows/Canary.UI.exe` → the `Canary UI.lnk` resolved target → `null` if none found (fall back to headless with a one-line warning).

**UI side (`src/Canary.UI/Program.cs`):**

```csharp
[STAThread]
static void Main(string[] args) {
    ApplicationConfiguration.Initialize();
    var form = new MainForm();
    if (AutoRunArgs.TryParse(args, out var auto)) {
        form.Load += async (_, _) => await form.AutoRunAsync(auto);
    }
    Application.Run(form);
}
```

`AutoRunArgs` carries `--workload`, `--test`, `--suite`, `--mode`. `MainForm.AutoRunAsync` walks the same code paths as a tree-click → Run Tests button press but skips the operator interactions.

**Launching the SUT visibly:**

| Workload | Today | Change needed? |
|---|---|---|
| Rhino | Visible (GUI app, can't be hidden). | None. |
| Penumbra | Visible (Chrome opens; no `--headless` flag passed). | None. |
| Qualia | Visible (Chrome). With future Tauri wrap per `qualia-desktop-mvp-2026-05-22.md`, Canary will pass `--headed` or equivalent to `tauri dev`. | Cross-repo coordination flagged; not blocking this phase. |

**Recursion guard:** `Canary.UI` calls `TestRunner` directly (in-process). The UI's Re-run button does NOT shell out to `canary run`. Confirmed by the existing TestRunnerPanel code.

### Open questions

- **STATUS: unresolved — does spawning a WinExe from `dotnet`-invoked `canary.exe` work cleanly without zombie consoles?** The `Canary/AGENTS.md` "How to reproduce bugs" already warns: "do NOT use `dotnet run` for the UI (background mode fails)." Direct `Process.Start("Canary.UI.exe")` should work; validate during implementation.
- **What about `--quiet`?** Should `canary run --quiet` imply `--headless`? Recommendation: yes. CI use case.
- **STATUS: unresolved — what if the UI is already open and the operator runs `canary run` from a terminal?** Two UI instances would collide on workload-folder loading. Recommendation: use a Mutex (`Global\Canary.UI.SingleInstance`); the second instance posts its `AutoRunArgs` to the running instance via a named-pipe message + exits. Standard WinForms single-instance pattern.

### Effort: S–M
- CLI `--headless` flag + UI auto-launch (~1 day).
- UI auto-run path (~1 day).
- Single-instance + auto-run forwarding (~1 day).

### Dependencies
- **None upstream.**
- **Standalone deliverable.** This is the smallest section, but the highest-leverage operator-visible change. Recommended to land first.

---

## C4 — UI overhaul

> **SUPERSEDED 2026-05-27.** Canary.UI has been migrated to **Avalonia 11 + FluentAvaloniaUI + CommunityToolkit.Mvvm** — see [`docs/features/canary-ui-avalonia.md`](../features/canary-ui-avalonia.md) and [`docs/progress/2026-05-27-canary-ui-avalonia-migration.md`](../progress/2026-05-27-canary-ui-avalonia-migration.md). The "WinForms additive + WPF island" approach below shipped through Phase 7 of this overhaul and ran into the recurring layout-regression pattern (clipped buttons, overlapping tabs, hidden status lines) the Avalonia migration was designed to eliminate. The nav structure (workloads tree + per-tab content swap + Sessions / Localhost / Feedback / Telemetry / Settings tabs) carried forward; the WinForms control implementations were replaced. The WPF annotation island is gone — `AnnotationCanvas` is now an Avalonia control.

### Problem

Per §A1, the current UI is a workload-and-test-centric tree with a content-panel-replacement model. Reasonable for "manage tests + view results"; insufficient for "see live console + network + past runs + localhost ports + feedback inbox at-a-glance." The operator (ask #6) wants this evaluated.

### Proposal: WinForms additive, with WPF islands via `WindowsFormsHost` for annotation surfaces

**Recommendation: keep WinForms; add panels; don't WPF-reshell the whole app.** Justification (3 sentences): the existing WinForms shell works, is dark-themed, and embeds the operator's mental model (workload → suites → tests). Reshelling to WPF or a browser sidecar means rewriting MainForm + every editor control for marginal UX gain. The places WPF is genuinely better — `InkCanvas` for sketch+annotate (§C5) — get an island inside the existing shell via `WindowsFormsHost`, which is the documented bridge.

**Left navigation stays a TreeView at top, gains a horizontal Panel-strip below for new modes:**

```
┌─ TreeView (workloads/suites/tests/recordings) ────┐  ┌─ Content ─────────────────────┐
│                                                    │  │                                │
│  rhino / penumbra / qualia (current shape)         │  │  TestRunnerPanel | ResultsViewer
│                                                    │  │  PastRunsPanel    | LocalhostPanel
├────────────────────────────────────────────────────┤  │  FeedbackPanel    | TelemetryPanel
│  Tabs strip (radio-buttons, horizontal):           │  │
│  [ Tests ] [ Past Runs ] [ Localhost ]             │  │
│  [ Feedback ] [ Telemetry ] [ Settings ]           │  │
└────────────────────────────────────────────────────┘  └────────────────────────────────┘
```

Each tab switches the LEFT pane's mode AND the default RIGHT pane content:

| Tab | Left pane | Default right pane |
|---|---|---|
| **Tests** (current) | TreeView of workloads/suites/tests/recordings | WelcomePanel or selected test's last result |
| **Past Runs** | Filterable run list (workload + date + verdict) | REPORT.md of selected run + screenshot gallery |
| **Localhost** | Tiered port list (T1+T2+T3 with toggles) | Selected row's command-line + start time + kill/restart buttons |
| **Feedback** | Inbox / Triaged / Resolved lists | Selected feedback item: screenshot + annotation + body |
| **Telemetry** | Currently-running test (or last) + run history selector | Live console + network + input streams |
| **Settings** | (none — tabbed-panel only) | Theme / UI-mode (Stabilization/Maturation) / Tier 3 toggle / Retention slider |

**Test Runner panel changes (within Tests tab):**
- Add mode picker (the §A1 gap): radio-group `[Pixel-diff] [VLM] [Both]` defaulting to `Pixel-diff`.
- Add headless toggle for CI parity (default off — UI runs are visible).
- Live-run-display mode flips automatically when a run starts (existing behavior).

**Implementation approach:** introduce a `INavMode` interface with implementations `TestsNavMode`, `PastRunsNavMode`, `LocalhostNavMode`, `FeedbackNavMode`, `TelemetryNavMode`, `SettingsNavMode`. Each carries: `BuildTreeView(TreeView)`, `BuildDefaultContent(Panel)`. MainForm's existing `_treeView` + `_contentPanel` become INavMode-driven.

### Open questions

- **STATUS: unresolved — does WindowsFormsHost cleanly handle the dark theme?** WPF's default chrome is light. WPF resources can be themed but the integration with WinForms's `Color.FromArgb(30,30,30)` palette is fiddly. Validate by prototyping the InkCanvas embedded in the existing dark MainForm before committing.
- **Tab strip vs. side nav?** ASCII shows tabs; an alternative is a left-rail icon strip (VS Code style). Recommendation: tabs (horizontal) — fewer pixels, easier discoverability.
- **What happens to "Open Folder" / "Deploy Agent" / "View Report" toolbar buttons?** They're test-mode operations. Move into the Tests-tab toolbar (mode-specific).

### Effort: L
- INavMode refactor (~3 days).
- Each new panel (Past Runs, Localhost, Feedback, Telemetry, Settings) (~2–3 days each — 10–15 days total).
- Mode picker on TestRunnerPanel (~0.5 day).
- WindowsFormsHost integration for annotation island (~1 day; full annotation is §C5).

### Dependencies
- **Upstream:** C1 (Telemetry tab needs the live stream); C2 (Past Runs tab opens REPORT.md); C5 (annotation island in Past Runs viewer); C6 (Feedback tab content); C7 (Localhost tab content).
- This is the integration layer for everything else. Schedule LATE in implementation.

---

## C5 — Sketch + annotate feedback

### Problem

Per ask #7: operator wants to point at a screenshot region (rectangles, freehand, text labels) and say "this is wrong" — then Claude reads it during a future session and goes to investigate. Today the UI's image viewer (`ImageViewerForm`) shows the image and nothing else. No annotation surface.

### Proposal

**UI flow:** Past Runs panel → click a checkpoint's candidate / diff thumbnail → image opens in `AnnotatedImageForm` (new, WPF-hosted via `WindowsFormsHost`). Toolbar: `[ Pointer ] [ Rectangle ] [ Freehand ] [ Text ]` + color picker (red/yellow/green) + Save + Cancel. Operator annotates, types a one-paragraph note in the bottom Body field, clicks Save → feedback item lands in `docs/feedback/inbox/`.

**Storage layout** (PNG + JSON sidecar per §0.1 decision):

```
docs/feedback/inbox/2026-05-24-001-pencil-toon-bg-too-bright.md   <- frontmatter + body
docs/feedback/inbox/2026-05-24-001-pencil-toon-bg-too-bright/
  source.png                      <- original screenshot (copy)
  annotated.png                   <- source + rendered overlay
  annotations.json                <- vector data (rectangles, strokes, text labels)
```

**Item Markdown shape:**

```markdown
---
date: 2026-05-24
id: 2026-05-24-001
status: open  # open | triaged | resolved
project: canary | qualia | penumbra | rhino | cross-repo
runRef: "workloads/qualia/results/diag-pencil-baseline/runs/20260524-142300-a3f1/"
checkpointRef: "diag-pencil-baseline"
imageRef: "annotated.png"
urgency: low | normal | high
tags: [feedback, sketch]
---

# Pencil-toon background too bright at 50% opacity

The marked region (red rectangle bottom-left) shows the pencil-toon shader
bleeding into the background plane. Compare to the baseline at
`workloads/qualia/results/diag-pencil-baseline/baselines/diag-pencil-baseline.png`
where the same region is dark gray.

Likely cause: the pencil-toon pass writes to all pixels regardless of
material; should mask out pixels with no node geometry.
```

**`annotations.json` shape:**

```json
{
  "version": 1,
  "sourceImage": "source.png",
  "imageWidth": 1280,
  "imageHeight": 720,
  "shapes": [
    { "id": "s1", "type": "rect", "x": 100, "y": 400, "w": 200, "h": 150, "stroke": "#ef4444", "strokeWidth": 3 },
    { "id": "s2", "type": "freehand", "points": [[120, 405], [125, 410], …], "stroke": "#fbbf24", "strokeWidth": 2 },
    { "id": "s3", "type": "text", "x": 320, "y": 460, "text": "bleeding here", "color": "#ffffff", "background": "#000000aa", "fontSize": 14 }
  ]
}
```

**Overlay rendering for `annotated.png`:** WPF Canvas drawn-to-RenderTargetBitmap, exported as PNG. Same render pipeline as the live WPF canvas; just renders once at Save time. This produces an annotated PNG that's viewable on its own without the JSON (useful for sharing / embedding in bug reports).

**Lifecycle:**
1. Operator creates → `inbox/`.
2. Claude session-start: scans `inbox/`, lists open items, may move to `triaged/` after acknowledging.
3. Resolution (Claude or operator): edit frontmatter `status: resolved`, move to `resolved/`. Symlink-style movement so `runRef` paths stay valid.

### Open questions

- **STATUS: unresolved — pure WPF InkCanvas or custom WPF canvas?** InkCanvas is built for stylus + smoothing; rectangle / text label aren't native. Recommendation: custom WPF Canvas with hit-testing — gives uniform behavior for all four tool modes. Effort cost is ~1–2 days more than InkCanvas + extension.
- **Stroke serialization fidelity:** lossless polyline coords or simplified path data? Recommendation: polyline (lossless — JSON small enough; the JSON is for editing, the PNG is for viewing).
- **STATUS: unresolved — annotation on a diff image vs. an original screenshot.** Both should be supported. Today the diff image is rendered fresh by `CompositeBuilder`; we'd need to persist it separately so annotations on it stick.

### Effort: M–L
- WPF custom-canvas annotation control (~3–4 days).
- WindowsFormsHost embedding + WinForms-side launch / save flow (~1 day).
- File-write side (Markdown + JSON + PNGs) (~1 day).
- Past Runs panel "Annotate" button + integration (~1 day; landed as part of C8).

### Dependencies
- **Upstream:** C4 (Past Runs panel is the launch surface).
- **Downstream:** C6 (file-inbox writer here is the producer; MCP server is the consumer).

---

## C6 — Feedback channel architecture

### Problem

Per ask #7: feedback from operator → Claude. Per §0.1 decision: file inbox is canonical; MCP server wraps it for live queries.

### Proposal

**Two layers. File inbox is the source of truth; MCP server is a convenience read-API.**

### File inbox (canonical)

**Directory layout:**

```
docs/feedback/
  inbox/              <- new items (operator-written)
  triaged/            <- Claude has acknowledged + classified
  resolved/           <- handled (by Claude or operator)
  README.md           <- pointer / convention summary
```

Items live as `<slug>.md` plus a sidecar dir `<slug>/` containing source.png + annotated.png + annotations.json. Slug format: `YYYY-MM-DD-NNN-<3-to-5-word-slug>` per the prompt's §7 default.

**Lifecycle:**

1. **Create:** AnnotatedImageForm Save → atomic write to `docs/feedback/inbox/`.
2. **Discover:** Claude session-start hook (AGENTS.md addition): "If `docs/feedback/inbox/` is non-empty, list new items before proceeding."
3. **Triage:** Claude reads, edits frontmatter (`project`, `tags`, possibly `urgency`), moves to `triaged/`.
4. **Resolve:** Set `status: resolved` + add a `### Resolution` section to the body + move to `resolved/`. The `runRef` path stays valid (resolved items are historical record).

**AGENTS.md addition:** the Canary `AGENTS.md` gains a "Feedback inbox" rule pointing here. The relevant child-repo AGENTS.mds (Penumbra, Qualia, etc.) get a back-reference per `STANDARD.md` §7 cross-repo protocol when items affect them.

### MCP server (convenience wrapper)

**Package:** new csproj `src/Canary.McpServer/` (added to `Canary.sln`).
- TargetFramework: `net8.0`.
- Transport: stdio (per any AI coding agent MCP convention).
- ProjectReference: `Canary.Core` (reads `TestResult`, `ITelemetrySink` output) + `Canary.UI.Services` (only if needed for ResultsHistory).
- Distribution: a single exe alongside `canary.exe` + `Canary.UI.exe`; user adds a `.mcp.json` entry pointing at it.

**Tool surface (initial — minimal):**

| Tool | Inputs | Returns |
|---|---|---|
| `list_feedback` | `status?: open|triaged|resolved`, `limit?: int` | Array of `{ id, slug, date, project, urgency, status, summary }`. |
| `get_feedback` | `id: string` | Full body + frontmatter + annotation paths. |
| `mark_feedback_triaged` | `id`, optional patch `{ urgency, tags, project }` | Updated item. |
| `list_recent_runs` | `workload?`, `verdict?`, `limit?` | Array of `{ runId, testName, verdict, timestamp, reportPath }`. |
| `get_run_report` | `runId` | REPORT.md content + telemetry NDJSON path. |
| `list_localhost_ports` | none | Array per C7 Tier 1 (port, pid, command, intent if registered). |
| `list_running_apps` | none | Array of Canary-spawned children per C7 Tier 2 registry. |
| `kill_localhost_port` | `port: int` | Status. |

**Relationship to existing named-pipe JSON-RPC:** orthogonal. The named pipes carry IN-RUN agent communication. The MCP server reads at-rest artifacts (feedback files, REPORT.md, ResultsHistory) AND active state (localhost ports — via the same `netstat -ano` + spawn registry as C7). When Canary is running a test, the MCP server CAN observe live telemetry by tailing `runs/<active>/telemetry.ndjson`, but it does not duplex with the agents.

### Open questions

- **STATUS: unresolved — should the MCP server be a separate process or hosted by `Canary.UI.exe`?** Separate process (own csproj) is cleaner: works without UI running; survives UI crashes; matches the "Claude has its own console" mental model. Hosted-in-UI is simpler initially but conflates concerns. Recommendation: separate process.
- **What about authentication?** any AI coding agent MCP stdio is local-only (Claude spawns the server as a child process). No auth needed at the transport layer. If the MCP server ever exposes write actions to remote consumers, revisit.
- **STATUS: unresolved — Tier 2 spawn registry voluntary or hook?** See §C7 below — same question, decided there.

### Effort: M
- File inbox writer in WinForms (~1 day).
- File inbox lifecycle CLI helpers / convention docs (~1 day).
- MCP server skeleton + 8 tool implementations (~3–4 days).
- `.mcp.json` plumbing + Canary-side discovery doc (~0.5 day).

### Dependencies
- **Upstream:** C5 (writer side); C2 (`get_run_report` reads REPORT.md); C7 (`list_localhost_ports` reads the manager state).
- **Downstream:** C4 (Feedback tab content).

---

## C7 — Tiered localhost manager

### Problem

Per ask #9 + §A6: today there are two duplicate `ViteManager.KillStaleListenerAsync` implementations using `netstat -ano` + `taskkill /F /T`. Vite + Chrome processes bypass `ProcessManager`. Operator can't see what's bound to which port without running terminal commands.

### Proposal: three tiers, all shipping in v1

### Tier 1 — Passive dev-port enumeration

**Data shape:**

```csharp
public sealed record PortEntry(
    int Port,
    int? Pid,
    string? ProcessName,
    string? CommandLine,
    string? WorkingDirectory,
    DateTime? StartTime,
    PortProvenance Provenance);  // Unknown | DevServerHeuristic | CanarySpawn | CanaryHarness
```

**Mechanism:**
- Poll every 2s when the Localhost panel is foregrounded; every 30s when backgrounded.
- For each port in the §0.3 default list (3000, 3001, 4173, 4200, 5173, 5174, 8000, 8080, 8081, 1420): parse `netstat -ano` output for `LISTENING` entries.
- For each found PID: enrich via `Process.GetProcessById(pid)` → ProcessName + StartTime; via WMI `Win32_Process.CommandLine` → CommandLine + WorkingDirectory. Cache (the enrichment is the slow part).
- Plus any port Canary itself binds: at process start, dump `IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()` filtered to Canary's own PID and emit those as `Provenance = CanaryHarness`.

**UI:** table with columns `[Port | Process | Command | Started | Provenance | Actions]`. Per-row actions: **Kill** (taskkill /F /T), **Restart** (taskkill then re-spawn — only available for `CanarySpawn` rows), **Open** (open `http://localhost:<port>` in default browser), **Show in Process Explorer** (best-effort — `Process.Start("procexp.exe")` if installed, else just the explorer pattern).

**Kill default:** tree-kill (§C7 lesson from Process Explorer). Confirmation modal for non-`CanarySpawn` rows ("This process wasn't started by Canary — confirm kill").

### Tier 2 — Claude-spawn registry

**Storage:** `%LocalAppData%\Canary\claude-spawns\<session-id>.json` per the §0.3 default. Each session's file is rewritten on each new spawn; cleared on session end (best-effort — surviving stale files don't hurt, Tier 1 still works without them).

**Shape:**

```json
{
  "sessionId": "claude-2026-05-24-142301",
  "started": "2026-05-24T14:23:01Z",
  "spawns": [
    {
      "pid": 12345,
      "name": "node.exe",
      "commandLine": "cmd /c npm run dev -- --port 5173 --strictPort",
      "workingDirectory": "C:\\Repos\\Qualia",
      "port": 5173,
      "intent": "Qualia Vite dev server (Canary qualia workload)",
      "spawnedAt": "2026-05-24T14:23:05Z"
    }
  ]
}
```

**Producer:**
- `Canary.Core.Telemetry.SpawnRegistry` writes to the file on every `ProcessManager.Track()`.
- ViteManager / ChromeLauncher gain a `RegisterWithSpawnRegistry(SpawnRegistry, intent: "Qualia Vite", port: 5173)` call after Process.Start.

**Tier-2 voluntariness:** Canary-internal spawns register automatically. CLAUDE-launched processes outside Canary (e.g., the operator runs `npm run dev` in a terminal) won't be in the registry — they fall back to Tier 1 (port enumeration without provenance) or Tier 3 (name heuristic).

### Tier 3 — Name-heuristic process listing

**Mechanism:** opt-in toggle in the Localhost panel ("Show all dev-server-likely processes" checkbox). When on: enumerate via WMI `Win32_Process` filtered by name in `{node, npm, deno, bun, dotnet, cargo, tauri, vite, python}` AND command line containing one of `{dev, serve, run, watch, http.server, --port}`. Show as separate "Heuristic" section with explicit "may be false positive" caption.

**Per-row actions:** same as Tier 1 (Kill / Show in Process Explorer). Restart disabled (no provenance to replay).

### Open questions

- **STATUS: unresolved — Tier 2 voluntary vs. OS hook.** Operator decision per the prompt §0.1. Recommendation: voluntary for v1 (simpler, no OS-level invasiveness, relies on Canary-side discipline which is feasible since Canary spawns all relevant processes through its own code paths). Document the limitation: non-Canary spawns fall back to Tier 1/3.
- **WMI dependency:** WMI is built-in but slow (~50-100ms per query). Cache aggressively. Worth measuring.
- **STATUS: unresolved — what about WSL or Docker bridges?** Out of scope for v1. The default port list assumes Windows-native listening sockets.

### Effort: M
- Tier 1 (extract from ViteManager, generalize, panel) (~3 days).
- Tier 2 (SpawnRegistry + ViteManager/ChromeLauncher integration) (~1 day).
- Tier 3 (WMI listing + toggle) (~1 day).
- Promote `Canary.Core.LocalhostManager` from the two `ViteManager` copies (~1 day, refactor — replace `KillStaleListenerAsync` callsites).

### Dependencies
- **Upstream:** none.
- **Downstream:** C4 (Localhost panel content), C6 (MCP server tools).

---

## C8 — Live test panel + past-results browser

### Problem

Per asks #8 + §A1 + §A4: live runs already display via TestRunnerPanel + ProgressFeedPanel — but pre-checkpoint pipeline progress is invisible, and `result.json` overwrites every run so there's no history. Past runs are not browsable.

### Proposal

### Live test panel (extends existing TestRunnerPanel)

**Updates already informed by C4:**
- Mode picker added (the §A1 gap).
- Headless toggle (for parity with CLI; default off).

**Update vs. push model:** stick with the existing event-stream pattern (`ITestProgressEvents`). Polling is not needed — the runner fires events synchronously on the test thread; the GUI marshals to UI thread via `BeginInvoke`. C1's `OnTelemetry` extension is additive; the existing ProgressFeedPanel keeps its card-per-checkpoint view.

**Add a "Telemetry tail" sub-panel below the card flow:** a tail of the last N `TelemetryRecord`s color-coded by `kind` (console errors red, network failures orange, info gray). One line per record. Click a line → switches to the Telemetry tab focused on that record.

### Past-results browser (new PastRunsPanel)

**Structure:**

```
┌─ Filter strip ──────────────────────────────────┐
│  Workload: [all v]  Verdict: [all v]  Date: 7d  │
│  Search: [...........................]          │
├─ Run list (sortable cols) ──────────────────────┤
│  | When            | Workload | Test  | Verdict |
│  | 2026-05-24 14:23| qualia   | diag- | FAIL    |
│  | 2026-05-24 14:20| qualia   | diag- | PASS    |
└──────────────────────────────────────────────────┘
```

Click a row → right pane loads the run's REPORT.md (markdown-rendered with image previews) + screenshot gallery (baseline / candidate / diff per checkpoint, click-zoom + annotate button per §C5). NO re-run from this panel — re-runs go through the Tests tab explicitly (rule from prompt §C8).

**Index:** `workloads/<w>/results/<test>/runs/<timestamp>/REPORT.md` is the natural index entry. PastRunsPanel scans `workloads/*/results/**/runs/*/REPORT.md` and parses frontmatter for filtering. Cache the parse for performance.

**Pagination:** load most-recent 100; "load more" button for older. No hard retention cap — `STANDARD.md` §16's 14-day auto-clean applies to candidates/diffs, not REPORT.md. Operator can prune `runs/` manually.

**Diff view inside a past run:** side-by-side baseline + candidate + diff, with the C5 annotation tools active. The C8 view re-uses the AnnotatedImageForm from C5 for the actual annotation surface.

### Open questions

- **STATUS: unresolved — frontmatter-only index or full REPORT.md scan?** Frontmatter-only is faster; REPORT.md tail is richer (could match on "Network failures: GET /missing.json"). Recommendation: frontmatter for the list, lazy-load body on row-click.
- **What about migrating old result.json-only results?** Per §C2 open questions. Migration shim handles it.

### Effort: M
- TestRunnerPanel updates: mode picker + telemetry tail (~1 day; piggybacks on C1 + C4).
- PastRunsPanel new control (~3 days).
- Frontmatter index + filter (~1 day).
- Diff view with annotation hand-off (~1 day; C5 surface re-used).

### Dependencies
- **Upstream:** C1 (telemetry tail data), C2 (REPORT.md is the entry), C5 (annotation surface).
- **Downstream:** C4 (Past Runs tab content).

---

## C9 — VLM / visual-regression demotion path

### Problem

Per ask #5 + the operator's framing: VLM and pixel-diff stay as features but stop being the headline. The current Test Runner panel hardcodes pixel-diff (no mode picker — §A1 gap). The HTML report has VLM-detail sections inline. As apps mature, VLM/visual-regression should be promotable back to first-class.

### Proposal: a Settings → UI mode toggle, defaulting to "Stabilization"

**Toggle in Settings tab:**

```
UI mode:
( ) Stabilization — debugging-focused; VLM/pixel-diff under Past Runs tab.
( ) Maturation     — regression-focused; VLM/pixel-diff in main nav.

[Apply]
```

**Stabilization mode (DEFAULT for v1):**
- Tests tab keeps the mode picker on TestRunnerPanel (default pixel-diff).
- Past Runs tab shows the mode column + per-checkpoint mode + VLM reasoning when expanding a checkpoint.
- No standalone VLM dashboard in the nav.
- REPORT.md inlines VLM verdicts in the Checkpoints table (already proposed in C2).
- HTML report unchanged (`HtmlReportGenerator` is fine as-is per §A4).

**Maturation mode (operator toggles when apps stabilize):**
- Add a "VLM" tab in main nav showing aggregated VLM verdict trends across runs (pass rate, confidence distribution, drift detection).
- Add a "Regression" tab showing pixel-diff failure clusters across runs.
- These are reads on top of `runs/*/REPORT.md` + `result.json` — no new data, just new views.

**Why a toggle rather than just-remove-VLM-UI:** the operator's stated end-state is "VLM/visual-regression come back to the front when apps mature." A toggle makes that reversible without code changes. The Stabilization mode is the v1 default because debug-overhaul is what we're shipping; Maturation mode is queued for when apps reach the operator's "mature" threshold.

**No code deletion in v1:**
- `OllamaVlmProvider`, `ClaudeVlmProvider`, `VlmEvaluator`, `IVlmProvider` stay.
- `--mode {pixel-diff|vlm|both}` flag stays.
- `setup.vlm.provider` + `setup.vlmDescription` JSON fields stay.
- Per-checkpoint `mode: "vlm"` JSON field stays.

### Open questions

- **STATUS: unresolved — what's the trigger to switch to Maturation mode?** Recommendation: leave it as a manual operator decision for v1 (the toggle exists). Auto-promotion (e.g., "switch when 90% of tests have stable baselines for >30 days") is a future enhancement.
- **VLM panel under Past Runs in Stabilization mode:** does it stay an expandable per-checkpoint section, or get its own filter ("show only VLM-mode runs")? Recommendation: filter — operator already wants tags-based filtering per §C8.

### Effort: S
- Settings tab + toggle storage (~1 day).
- Future Maturation-mode panels are NOT in v1 scope — only the toggle ships.
- Validating no regressions to existing VLM paths (~1 day).

### Dependencies
- **Upstream:** C4 (Settings tab is part of the new nav).
- **Standalone otherwise.**

---

## Implementation Plan (appendix)

Proposed phasing for the follow-up implementation prompt. Pressure-tested against the audit findings; deviations from the prompt's suggested ordering are flagged.

| Phase | Goal | Lands | Effort | Depends on |
|---|---|---|---|---|
| **Phase 0** | Pre-flight (canon, snapshot tag, toolchain) | — | XS | — |
| **Phase 1** | C3 non-headless enforcement | `--headless` flag + UI auto-launch + single-instance | **S–M** | none |
| **Phase 2** | C1 universal telemetry envelope (data + producer side) | `TelemetryRecord`, `NdjsonFileSink`, `EventStreamSink`, agent CDP Console+Network subscription, Rhino `RhinoApp.WriteLine` interception | **L** | Phase 1 (so we can see it in the UI) |
| **Phase 3** | C2 REPORT.md + per-run dir migration | `MarkdownReportGenerator`, `runs/<timestamp>/` layout, `ResultsHistory` dual-shape scan | **M** | Phase 2 |
| **Phase 4** | C7 localhost manager (Tier 1 first; standalone, high operator value) | `Canary.Core.LocalhostManager`, refactor `ViteManager` to use it; UI panel via C4-lite (drop-in TabPage on TestRunnerPanel pre-overhaul) | **M** | none functionally; coordinate UI with Phase 7 |
| **Phase 5** | C6 file-inbox half + C5 sketch UI surface | `docs/feedback/` convention, AnnotatedImageForm (WPF island), Inbox writer | **M–L** | Phase 3 (`runRef` paths point to per-run dirs) |
| **Phase 6** | C6 MCP server half + C7 Tier 2 spawn registry | `Canary.McpServer` csproj with 8 initial tools; SpawnRegistry plumbing | **M** | Phases 3, 4, 5 (data sources) |
| **Phase 7** | C4 UI overhaul (the big one) | INavMode refactor, all six new tabs, mode picker on TestRunnerPanel, integration of all prior phases' panels | **L** | Phases 1–6 (this IS the integration) |
| **Phase 8** | C7 Tier 3 + C8 polish + C9 settings toggle | Tier 3 toggle, PastRunsPanel filter polish, Settings tab + UI-mode toggle | **S–M** | Phase 7 |
| **Phase 9** | Documentation pass + AGENTS.md updates across affected child repos | Canary AGENTS.md updates; cross-repo entries in `Penumbra/AGENTS.md`, `Qualia/AGENTS.md`, `Rhino/AGENTS.md`, MultiVerse BUILD_LOG | **S** | Phase 8 |

**Deviations from prompt's suggested ordering:**

1. The prompt suggested C5 (sketch UI surface) lands with C6 file-inbox half in Phase 5. I'm keeping that — the sketch surface and the file inbox are tightly coupled (the sketch is the inbox's producer).
2. The prompt suggested C7 Tier 1 lands in Phase 4 standalone. I'm keeping that order — Tier 1 has no dependencies and is high-value. Adding a single TabPage on the existing TestRunnerPanel as a stopgap until C4's full nav lands.
3. The prompt suggested Phase 7 is C4 UI overhaul saved for last because C1–C6 inform what panels need. Confirmed — INavMode refactor is the integration layer for everything else.
4. The prompt suggested Phase 8 wraps polish. I'm splitting it: Phase 8 handles operator-visible polish (Tier 3, PastRunsPanel filter, Settings toggle); Phase 9 handles the cross-repo doc pass per `STANDARD.md` §14.

### Cross-repo touch points

Per §0.2 hard rule 9, noted but NOT modified in this prompt:

| Phase | Cross-repo work |
|---|---|
| Phase 2 (C1) | **Penumbra:** Penumbra exposes `window.__canaryLogBuffer` already — could be optionally drained on telemetry start. Net-new code in Canary, no Penumbra change required for v1. **Qualia:** same. **Rhino-side / CPig-side:** the Slop LogHub file currently lives on disk; Canary could read it post-test as a telemetry batch import. Coordinate with CPig AGENTS.md if that lands. |
| Phase 3 (C2) | None — REPORT.md is Canary-internal. |
| Phase 4 (C7) | None — localhost manager is Canary-internal but observes processes owned by child repos. Document in Penumbra/Qualia AGENTS.md that Canary now tracks their Vite/Chrome PIDs. |
| Phase 5–6 (C5/C6) | **MCP server packaging:** the user adds an `.mcp.json` entry pointing at `Canary.McpServer.exe`. This convention should be documented in `MultiVerse/SKILLS.md` if it's session-wide useful. |
| Phase 7 (C4) | None directly. |
| Phase 9 | **All child repos with AGENTS.md.** Per `STANDARD.md` §7, any change that leaves a child's AGENTS.md stale needs a cross-repo update. Telemetry hooks + report format + feedback inbox locations are the candidates. |

### Total effort estimate

Sum of S=0.5, M=1, L=2, XL=3 weeks (rough):
- Phase 0: 0
- Phase 1 (S–M): 0.5–1
- Phase 2 (L): 2
- Phase 3 (M): 1
- Phase 4 (M): 1
- Phase 5 (M–L): 1–2
- Phase 6 (M): 1
- Phase 7 (L): 2
- Phase 8 (S–M): 0.5–1
- Phase 9 (S): 0.5

**Total: ~9.5–11.5 weeks** of focused work. Half of that is Phases 2 + 7 (the telemetry capture and the UI integration). The first 4 phases (Phase 1 + 2 + 3 + 4) deliver ~70% of the operator-visible value in ~4.5 weeks — recommend that as the v1 cut.

---

## Open questions for the operator (consolidated for Phase D)

1. **MCP server packaging:** confirm separate csproj `src/Canary.McpServer/` (recommended) vs. hosted inside `Canary.UI.exe`.
2. **Tier 2 spawn registry mechanism:** voluntary (Canary code paths register; recommended) vs. OS hook (more invasive). Voluntary is simpler and covers Canary-internal spawns; non-Canary spawns fall back to Tier 1 / Tier 3.
3. **UI overhaul approach:** WinForms additive with WPF islands (recommended) vs. WPF reshell vs. browser sidecar. Justification in §C4.
4. **v1 cut scope:** ship Phases 1+2+3+4 (~4.5 weeks, ~70% value) and defer Phases 5–9 to a v2 milestone, vs. ship the full 9 phases. Recommend v1 = Phases 1–4 + a thin C4 (single new Localhost TabPage on TestRunnerPanel rather than the full INavMode refactor).
5. **Single-instance UI behavior:** when a second `canary run` arrives and the UI is already open, forward auto-run args via named pipe to the running instance (recommended) vs. let it open a second window.
6. **CLI exit-code regression (separate from this design):** `RunCommand.RunAsync` is void-returning — CLI exits 0 even on test failure. Recommend filing as a separate bug in `docs/bugs/NNNN-cli-exit-code.md` and fixing in Phase 1.

---

## Frontmatter for future readers

This plan is `status: proposed`. After operator review (Phase D), the operator's resolved open-question answers feed into the implementation prompt. Once the implementation prompt is written, this plan's status flips to `status: in-progress`. Once Phases 1–9 (or the agreed v1 cut) ship, status flips to `status: shipped` + a retrospective section is appended.

---

## Retrospective (shipped 2026-05-24)

All 9 design phases (C1–C9) landed across 10 implementation phases (Phase 0
pre-flight + precursor + Phases 1–9). ~30 commits. Build 0/0 throughout.
Unit tests grew 107 → 220 (+113 new); integration tests 0 → 2.

### What shipped exactly as designed

- **C1 telemetry envelope** — `Canary.Telemetry.TelemetryRecord` shape +
  `NdjsonFileSink` + CDP `Console` + `Log` + `Network` capture for
  Penumbra + Qualia via shared `Canary.Cdp.CdpTelemetryStream` helper.
- **C2 REPORT.md + per-run dir** — `MarkdownReportGenerator` writing
  the §C2 spec template; per-run dir lives at
  `<test>/runs/<yyyyMMdd-HHmmss-xxxx>/`. CLI + GUI parity (both
  paths route through `TestRunner.SavePerRunArtifactsAsync`).
- **C3 non-headless enforcement** — `--headless` flag, UI auto-launch,
  single-instance mutex + named-pipe forwarding per operator decision Q5.
- **C5 sketch + annotate** — custom WPF `AnnotationCanvas` (not InkCanvas
  per §C5 open question) hosted in WinForms via `ElementHost`;
  rect / freehand / text tools; PNG + JSON sidecar storage.
- **C6 file inbox + MCP server** — `Canary.Feedback.FeedbackInboxWriter`
  produces `docs/feedback/{inbox,triaged,resolved}/<slug>.md` + sidecar.
  `Canary.McpServer` exposes 8 tools over stdio.
- **C7 tiered localhost** — Tier 1 + Tier 2 (SpawnRegistry voluntary
  per Q3) + Tier 3 (name heuristic) all shipped; LocalhostManager
  unions in `EnumeratePorts`.
- **C9 demotion path** — `CanarySettings.UiMode` toggle persists;
  Maturation-mode panels deliberately NOT built per §C9.

### Scope choices that deviated pragmatically

- **C4 UI overhaul layout** — design ASCII showed the tab strip nested
  below the TreeView on the LEFT pane (each mode swaps left + right).
  Shipped a simpler top-level `TabControl` wrapping the existing
  `SplitContainer` as the Tests tab. INavMode contract unchanged; the
  placement can be rearranged in future polish without touching panels.
- **C4 toolbar mode picker** — placed on `MainForm`'s toolbar rather
  than inside `TestRunnerPanel` because the panel is constructed lazily
  per-run, so an internal picker can't influence its own run.
- **C2 candidates/diffs flatness** — `runs/<timestamp>/` holds only
  `result.json` + `REPORT.md` for this phase. Baselines, candidates,
  diffs, composite.png stay flat at the test level (overwriting per
  run). The `MarkdownReportGenerator` uses `../<dir>/` relative links
  to compensate. Moving images per-run is a polish follow-up.
- **C6 MCP transport** — self-contained ~120-line MCP 2024-11-05
  stdio JSON-RPC handler rolled in-house instead of the
  `ModelContextProtocol` NuGet package. Zero external dep; wire shape
  visible from source.
- **C7 Tier 3 filter scope** — name-only filter ships; WMI Win32_Process
  command-line keyword filtering deferred. False positives surfaced with
  the explicit "may be false positive" UI caveat.

### Deferred to a v2 follow-up (intentional + documented)

| Deferral | Why | Where tracked |
|---|---|---|
| Rhino-side `RhinoApp.WriteLine` interception | No clean RhinoCommon 8 hook in scope | Phase 2 BUILD_LOG entry |
| `InputReplayer` event records | Refactor cross-cuts Phase 7 territory | Phase 2 BUILD_LOG entry |
| Per-test telemetry slicing in shared-suite mode | Boundaries ambiguous | Phase 2 BUILD_LOG entry |
| Moving candidates/diffs/composite into per-run dirs | Substantial refactor | Phase 3 BUILD_LOG entry |
| Per-test telemetry route (vs per-suite) | Phase 2 ships per-suite location | Phase 3 BUILD_LOG entry |
| WMI command-line filtering for Tier 3 | Name-only ships; if noisy, polish | Phase 8 BUILD_LOG entry |
| Maturation-mode UI panels | Explicit out per §C9 | Phase 8 BUILD_LOG entry |
| `ResultRetention` auto-wiring | Helper available; operator decides cadence | Phase 3 BUILD_LOG entry |
| PastRuns body-content search across REPORT.md | Metadata-only filter today | Phase 8 BUILD_LOG entry |
| `McpServerStdioIntegrationTests` | In-process StringReader/Writer covers the protocol | Phase 6 BUILD_LOG entry |
| `UIOverhaulSmokeTests` integration test | `NavModeTests` cover the contract | Phase 7 BUILD_LOG entry |

### Counts

- Commits past pre-impl tag: ~30 (`git log pre-impl-debug-overhaul-2026-05-24..HEAD --oneline | wc -l`)
- Unit tests: 107 → 220 (+113)
- Integration tests: 0 → 2 (SingleInstancePipeTests)
- New files: ~40 (Telemetry namespace, Reporting, Localhost, Feedback, Navigation, Panels, McpServer csproj + tools, Settings, ResultRetention, tests)
- New csproj: 1 (`Canary.McpServer`)
- Build: 0/0 warnings/errors throughout

### Operator-visible deltas

- `canary run` launches Canary.UI by default; `--headless` for CI.
- `canary run` returns exit code 1 on any failure (was 0 → bug 0007).
- Toolbar mode picker drives `--mode` from the GUI.
- 6 nav tabs above the workload tree: Tests / Past Runs / Localhost /
  Feedback / Telemetry / Settings.
- Per-run REPORT.md + result.json under
  `workloads/<w>/results/[<suite>/]<test>/runs/<timestamp>/`.
- Per-suite telemetry.ndjson under `workloads/<w>/results/[<suite>/]`.
- Operator Annotate flow on `ImageViewerForm`.
- `Canary.McpServer.exe` ready to register in `.mcp.json`.
- LocalhostPanel shows Tier 1 + Tier 2 (Canary-spawn intent strings) +
  Tier 3 (opt-in heuristic).
- `%LocalAppData%\Canary\settings.json` persists UI mode + retention +
  Tier 3 toggle.
