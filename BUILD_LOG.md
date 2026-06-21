# Build Log — Canary

## 2026-06-17 — Rhino session telemetry: surface the real event name (phase) in SESSION_REPORT — SHIPPED

Follow-up to the telemetry-capture ship below: the first session showed every Penumbra event as "Log" because the
tail used Penumbra's coarse top-level `kind` ("Log") as the event label, while the real event is its `data.phase`
("gl.scene.loaded", "rep.live", …). `PenumbraPreviewTelemetryTail.ParsePenumbraLine` now sets the captured record's
`Data.event = data.phase` (falling back to `kind`), so the SESSION_REPORT reads the actual events. (Pairs with the
Penumbra-side enrichment that adds rep + bounds to `gl.scene.loaded` + emits `rep.live` from the GLSL path.)
`dotnet build Canary.sln -c Release` → 0 errors.

## 2026-06-17 — Rhino session telemetry v2 (partial): capture Penumbra in-Rhino preview NDJSON — SHIPPED

- **Date**: 2026-06-17
- **Requested by**: operator — "oops there's no canary functionality for rhino. Build that into canary, while i run it by hand for now" (to debug the CPig.Rhino move + Penumbra-rep work via a Canary rhino session).
- **Gap found**: (a) the UI **Sessions tab didn't even OFFER rhino** — `SessionsLiveViewModel.SetWorkloads` filtered the workload picker to `qualia-cdp`/`penumbra-cdp` only (the 2026-06-02 ship wired the factory `"rhino"` case + the CLI `--workload`, but missed this UI filter), so the operator saw "there's no rhino available for sessions". (b) Even via the CLI path, a rhino session's `telemetry.ndjson` was nearly empty — `RhinoSessionAgent` didn't implement `ITelemetryAware` and `SessionAgentFactory.CreateRhinoAsync` DISCARDED the session sink (`_ = telemetrySink`). So a hand-driven session captured no behavioral trace of what Penumbra rendered.
- **Scope**:
  - `SessionsLiveViewModel.SetWorkloads` (`src/Canary.UI.Avalonia/ViewModels/`) filter now includes `"rhino"` → the Sessions tab lists "Rhino 8". (The CLI `canary session start --workload rhino` already worked.)
  - NEW `src/Canary.Core/Telemetry/PenumbraPreviewTelemetryTail.cs` — polling tail of `%LocalAppData%\Penumbra\preview\telemetry.ndjson` (the Penumbra Rhino plug-in's `NdjsonLog`); baselines at the file's current end (this session's events only), forwards each appended event to an `ITelemetrySink`. Penumbra's free-form `kind` (scene.loaded/frame.real/gl.field.transform/rep.live/render.error) doesn't map onto Canary's `TelemetryKind` enum → wrapped as `Kind=Log, Source="penumbra"` with the domain kind in `Data.event`, payload in `Data.payload`.
  - `RhinoSessionAgent` now implements `ITelemetryAware`; `RegisterTelemetrySink` starts the tail, `DisposeAsync` stops it. `SessionAgentFactory.CreateRhinoAsync` registers the session sink (was discarding it).
  - `SessionReportWriter.BuildMarkdown(s, sessionDir?)` renders a "Penumbra preview telemetry" section (last 80 penumbra events as `HH:mm:ss [level] event payload`) + links `telemetry.ndjson`. Optional `sessionDir` param → other callers unaffected.
- **Status**: `dotnet build Canary.sln -c Release` → 0 errors.
- **Operator gate**: `canary session start --workload rhino` → drive CPigSphere/CPigDisplay/move/CPigDisplayRep by hand → `q` → `SESSION_REPORT.md` now shows the Penumbra events (scene `+tape`/`+grid` + bounds, `gl.field.transform` on move, `rep.live` on CPigDisplayRep). Requires the Penumbra Rhino plug-in loaded (it writes the NDJSON). **Still v2**: Rhino command-history + Slop log-tail sources.

## 2026-06-16 — runMode:shared is the DEFAULT + WaitForPenumbraFrame made relative — SHIPPED

- **Date**: 2026-06-16
- **Requested by**: operator — penumbra-glsl tests "ran one after another, in separate Rhinos; they should chain in the same Rhino. Canary has a way to do this — find it, and make sure this is how all suites run in the future."
- **Scope**: `TestDefinition.RunMode` default `"fresh"` → `"shared"` (`src/Canary.Core/Config/TestDefinition.cs`). Every suite now chains in ONE app instance unless a test explicitly sets `"runMode": "fresh"`. The single-launch shared path engages only when ALL of a suite's tests are shared (`RunCommand.cs` dispatch, unchanged) — one `fresh` test still forces the whole suite per-test.
- **Blast radius (mapped, zero regressions)**: 81 rhino tests were already explicit `shared`; 2 omitted rhino (smoke-test, salimon) + 95 omitted qualia tests are stateless → inherit shared safely; the only intentional `fresh` is `qualia-v4-breadcrumb-nested` (explicit, process-global nav state — untouched). The 2 `penumbra-glsl-*` tests were the only rhino `fresh` holdouts → flipped to `shared`.
- **The blocker that had forced fresh**: the in-process GLSL frame-ready revision (`RealRevision`) is process-global and never reset, and `WaitForPenumbraFrame` gated absolutely (`target >= minRevision`, default 1) — so a chained test 2 passed INSTANTLY on test 1's stale revision and captured a stale frame. **Fix**: `HandleWaitForPenumbraFrame` (`src/Canary.Agent.Rhino/RhinoAgent.cs`) is now RELATIVE — it snapshots the revision on the first read and returns once it INCREASES; `minRevision` is deprecated/ignored (still parsed so old JSONs don't error). The progressive multi-frame render reliably pushes the revision past the baseline.
- **Status**: `dotnet build Canary.sln -c Release` → 0 errors. Agent (Canary.Agent.Rhino) + harness + Core rebuilt. No test-data migration needed.
- **Operator gate**: `canary run --workload rhino --suite penumbra-glsl` now runs both tests in ONE Rhino (restart Rhino first for the new agent + the A1/A2 Penumbra `.rhp`). Canary CLAUDE.md updated (Quick Reference runMode rule + the penumbra-glsl section).

## 2026-06-11 — BUG-0013 compute-smoke starvation fix (eventDrivenRender) — SHIPPED

- **Date**: 2026-06-11
- **Scope**: All 13 `atlas-blob-compute-*` tests crashed in both sweeps.
  Original theory (helper budget < ~90s pipeline build) disproven — every
  test already prebuilds via `__canaryPrebuildComputeMarchPipeline()`.
  Actual chain: Penumbra's 2026-05-08 graduation flipped
  `eventDrivenRender` ON in the default profile → Canary's
  `?autostart=true` boot reads the launch checkboxes initialized from
  that profile (fresh temp Chrome profile = no localStorage) → C2 render
  gate idles the loop → the smoke gets 1 dirty frame but needs ~10
  dispatches → `dispatchCount=0` at any budget.
- **Fix**: insert `__canarySetDisplayState({features: {eventDrivenRender:
  'off'}})` into all 13 test JSONs before the smoke (probe-validated on
  one test first). No Canary code change; no Penumbra change.
- **Validation**: 10/13 New (first-ever passes; 96–173s each;
  `.compute-validation.log`). 3 residuals are Penumbra-side (2× TDR-class
  device loss on heavy D2Cubic variants, 1× persistent-threading path
  never dispatching) — folded into ask
  `docs/asks/penumbra/0002-compute-smoke-self-mark-dirty.md` along with
  the upstream self-mark-dirty improvement that would let the C2-off
  lines be removed.
- **Docs**: `docs/bugs/0013-compute-smoke-starved-by-event-driven-render.md`,
  CHANGELOG entry, penumbra ask 0002.

## 2026-06-11 — BUG-0012 feature-loader stale assertions + benchy bare-await fix — SHIPPED

- **Date**: 2026-06-11
- **Scope**: Root-caused the four "JavaScript evaluation failed: Uncaught"
  penumbra test crashes. Three test JSONs were stale against Penumbra's
  2026-05-08 graduation rework (default profile ships 7 features ON;
  quality keeps meshDrivenClassification off; the A3↔A6 invalidates edge
  was removed — they compose); stl-import-benchy had a bare top-level
  `await` setup command (SyntaxError under Runtime.evaluate, broken since
  authoring, zero successful runs) and swallowed STL-load failures.
  No Penumbra-side changes — profiles/matrix/hooks behave as documented.
- **Also**: `CdpClient.DescribeException` — JS errors now surface
  `exception.description` (message + top stack frames) instead of the
  bare "Uncaught" that made this class of failure opaque.
- **Build/tests**: 0/0; 309/309 unit tests.
- **Validation**: all four tests pass as solo headless runs
  (mutex-rejection 76s, quality-profile 76s, benchy 17s — first-ever
  success; all-off confirmed earlier in the BUG-0011 sweep). Bonus signal:
  40 shared-agent sweep tests the same evening had ZERO load-timeout
  crashes (BUG-0011 holding); its 13 atlas-blob-compute-* crashes are a
  separate compute-pipeline-build-budget issue, flagged as a follow-up.
- **Docs**: `docs/bugs/0012-feature-loader-stale-assertions-and-benchy-await.md`,
  CHANGELOG `[Unreleased]` Fixed entry.

## 2026-06-11 — BUG-0011 penumbra spawn/teardown reliability fix — SHIPPED

- **Date**: 2026-06-11
- **Scope**: Root-caused and fixed the ~35% `Page.loadEventFired` timeout
  crashes from the 2026-06-11 90-test penumbra sweep. Three layered causes:
  (1) hard-coded 60s navigate ceiling vs 30–90s WebGPU/Dawn re-init on the
  per-test `setup.backend` → `SetBackend` reload; (2) `ViteManager` tree-kill
  racing child enumeration and orphaning the vite node (port 3000 held +
  inherited console handles blocking external drivers — the reason the prior
  session's retry pass needed a 20s node-janitor loop); (3) Chrome temp
  profile delete racing child file locks (28 leaked dirs).
- **Fix**: `PenumbraConfig.PageLoadTimeoutMs` (default 180s) +
  `NavigateWithRetryAsync` (retry-once, warn telemetry);
  `ViteManager.StopInternal` kill-by-port fallback + `KillStaleListenerAsync`
  waits for actual port release; `ChromeLauncher` CDP-port stale-listener
  kill + 2h-aged profile GC + delete retry in `ChromeLaunchResult.Dispose`.
- **Build/tests**: 0 errors / 0 warnings; 309/309 unit tests.
- **Validation**: 19 remaining roster tests re-run as individual headless
  `canary run` invocations with NO external kill/sleep workarounds
  (`.retry-validation.log`): 0 load-timeout crashes, port 3000 released
  after every run, profile-dir count flat. Typical per-invocation wall
  20–40s (atlas-heavy 90–160s), all far under the 180s ceiling. The 3
  remaining crashes are the unrelated flaky `JavaScript evaluation failed:
  Uncaught` bug (feature-loader trio + stl-import-benchy), tracked
  separately.
- **Docs**: `docs/bugs/0011-penumbra-page-load-timeout-sweep-crashes.md`,
  CHANGELOG `[Unreleased]` Fixed entry, CLAUDE.md "Running Penumbra tests"
  section (suite runs already share one Vite+Chrome; the per-test reload is
  the navigation that matters).

## 2026-05-27 — Canary.UI Avalonia migration Phase 6 (cutover) — SHIPPED

- **Date**: 2026-05-27
- **Commits**: pending (single Phase 6 cutover commit + docs commit).
- **Scope**: Final phase of the 7-phase Avalonia migration. The
  Avalonia project becomes the sole UI project; the WinForms shell is
  deleted; the cross-repo doc pass aligns Canary CLAUDE.md +
  README.md + the MultiVerse cross-repo BUILD_LOG to the new
  reality.
- **Files edited**:
  - `src/Canary.UI.Avalonia/Canary.UI.Avalonia.csproj` —
    `<AssemblyName>` flipped to `Canary.UI` so the produced exe
    keeps the legacy filename (UiLocator + every shortcut keeps
    working).
  - `src/Canary.UI.Avalonia/Program.cs` — single-instance mutex
    name unified with the legacy WinForms one
    (`Global\Canary.UI.SingleInstance`) so CLI pipe forwards land
    on the running Avalonia exe.
  - `src/Canary.Harness/UiLocator.cs` — sibling-solution path
    walks to `Canary.UI.Avalonia/bin/.../Canary.UI.exe` rather than
    the retired `Canary.UI/bin/...` directory.
  - `tests/Canary.Tests/Canary.Tests.csproj` — drops the Canary.UI
    ProjectReference + the `<UseWindowsForms>` flag.
  - `tests/Canary.Tests.Integration/Canary.Tests.Integration.csproj`
    — repointed to the Avalonia project.
  - `tests/Canary.Tests.Integration/SingleInstancePipeTests.cs` —
    `using Canary.UI;` → `using Canary.UI.Avalonia.Services;`.
  - `CLAUDE.md` — Quick Reference repro pattern path + Framework
    line + spec/PHASES_UI reference updated to flag the Avalonia
    migration.
  - `README.md` — Features bullet + Project Structure tree now
    reference `Canary.UI.Avalonia/` (outputs Canary.UI.exe).
  - `docs/features/canary-ui-avalonia.md` — status `in-progress` →
    `shipped`; Phase 6 row updated.
  - `docs/plans/2026-05-24-canary-debug-overhaul.md` — § C4 marked
    SUPERSEDED 2026-05-27 with a pointer to the Avalonia migration.
  - `docs/progress/2026-05-27-canary-ui-avalonia-migration.md` —
    Phase 6 section appended.
  - `CHANGELOG.md` — new `### Changed` block prepended; final
    unit-test landing recorded.
  - `Canary.sln` — Canary.UI project removed (`dotnet sln remove`).
- **Files deleted**:
  - `src/Canary.UI/` — entire WinForms project tree (~30 files).
  - `tests/Canary.Tests/UI/` — 8 WinForms-coupled test files
    (SessionsPanelTests, GuiTestLoggerTests, IntegrationTests,
    PastRunsIndexTests, ResultsHistoryTests,
    ResultsHistoryDualShapeTests, WorkloadExplorerTests). Each
    behavior either has Avalonia VM coverage or was tied to a
    surface that isn't part of this migration (PastRuns panel was
    never ported).
  - `tests/Canary.Tests/Navigation/` — NavModeTests.cs (INavMode
    was the WinForms lazy-nav abstraction; Avalonia uses a
    NavigationView + selected-item binding instead).
- **Tests**:
  - Pre-Phase-6: 330 unit tests (peak across the build phases).
  - Post-Phase-6: 283 unit tests (47 WinForms-only tests removed).
  - vs Pre-migration baseline (258): +25 net, all Avalonia VM
    coverage.
- **Build**: `dotnet build Canary.sln` = 0 warnings, 0 errors.
  Single UI project, single produced `Canary.UI.exe`.
- **Cross-repo touches** (per the prompt §0.3 + Canary
  CLAUDE.md Cross-Repo Change Protocol):
  - `C:/Repos/Canary/CLAUDE.md` — Avalonia framework note +
    Quick Reference path update.
  - `C:/Repos/MultiVerse/BUILD_LOG.md` — cross-repo entry
    flagging the migration shipped (Canary → operator workflow
    surfaces).
  - **Peer repos (`C:/Repos/Qualia/CLAUDE.md`,
    `C:/Repos/Penumbra/CLAUDE.md`)**: NOT TOUCHED. Their references
    to `Canary.UI.exe` continue to work — same filename at the
    same shortcut surface; only the producing project moved.
- **Verification gates (Phase 6)**: see Phase 6 smoke matrix in
  `docs/progress/2026-05-27-canary-ui-avalonia-migration.md`.
- **Snapshot tag**: `pre-impl-ui-avalonia-2026-05-27` deleted
  after the smoke matrix passed (rollback anchor no longer needed).
- **Status**: ✅ Migration shipped. 7 phases over one session;
  330 → 283 unit-test final count (net +25 vs pre-migration);
  build 0/0 throughout; pushed at the Phase 5 boundary plus the
  Phase 6 cutover commit.

---

## 2026-05-27 — Canary.UI Avalonia migration Phase 5 (services + glue)

- **Date**: 2026-05-27
- **Commits** (5 total):
  - `7ed215f` `feat(ui-avalonia): AbortHotkey ported against Avalonia HWND`
  - `865c815` `feat(ui-avalonia): SingleInstancePipeServer + AutoRunRequestHandler`
  - `a03dd95` `feat(ui-avalonia): drag-and-drop + tree context menus + editor host`
  - `71d6cc7` `test(ui-avalonia): AutoRunRequestHandler tests`
  - pending `docs(progress): Phase 5 — services + glue`
- **Scope**: Phase 5 of the 7-phase Avalonia migration. The last
  build phase before cutover. Lights up the operator-glue surfaces
  the earlier phases prepared but didn't wire — AbortHotkey, AutoRun
  pipe forwarding, tree drag-and-drop, tree context menus, and the
  editor wire-in that brings the Phase 3 editors out of orphan state.
- **Files added** (4 src + 1 test):
  - `Hotkeys/AbortHotkey.cs` — Win32 Pause hotkey via Comctl32
    SetWindowSubclass.
  - `Services/AutoRunRequestHandler.cs` — pure FindNode + ParseMode
    helpers.
  - `Views/EditorHostWindow.axaml` (+ `.cs`) — wraps editor Views in a
    standalone modal window for context-menu-launched editing.
  - `tests/Canary.Tests/UI.Avalonia/AutoRunRequestHandlerTests.cs`
    — 7 tests.
- **Files edited**:
  - `ViewModels/TestRunnerViewModel.cs` — OnRunStarted /
    OnRunFinished lifecycle hooks for the AbortHotkey wire-in.
  - `ViewModels/TestsViewModel.cs` — Edit / Approve / OpenInExplorer
    / CreateTestFromRecording RelayCommands + delegate slots.
  - `Views/TestsView.axaml` + `.cs` — TreeView ContextMenu + drag-
    drop handlers.
  - `Views/MainWindow.axaml.cs` — AbortHotkey lifecycle, editor +
    prompt delegate wiring, PersistAndRefreshAsync.
  - `ViewModels/MainWindowViewModel.cs` — HandleAutoRunAsync routing.
  - `docs/features/canary-ui-avalonia.md` — Phase 4 → shipped,
    Phase 5 → in-progress.
  - `docs/progress/2026-05-27-canary-ui-avalonia-migration.md` —
    Phase 5 section + commits + verification gates + wire-in
    completeness summary.
  - `CHANGELOG.md` — Phase 5 detail prepended; combined test delta
    258 → 330 (+72 across phases 0–5).
- **Tests**:
  - Pre-Phase-5: 323 unit tests, 0 failed.
  - Post-Phase-5: 330 unit tests, 0 failed (+7 net new — FindNode
    coverage 5, ParseMode 1, CreateTestFromRecording integration 1).
- **Build**: `dotnet build Canary.sln` = 0 warnings, 0 errors. Both
  exes build green.
- **Verification gates (Phase 5)**: 1) build 0/0 ✅; 2) pipe
  forwarding — pending operator smoke; 3) Pause hotkey — pending;
  4) drag-and-drop recording — pending; 5) tree context menus —
  pending; 6) VM tests ✅.
- **Status**: 🟡 Phase 5 code + tests + docs shipped locally. All
  build phases complete. Operator review at the phase boundary
  before Phase 6 cutover.
- **Wire-in summary after Phase 5**:
  - Phase 3 editors — wired via context-menu Edit + EditorHostWindow.
  - Phase 4 AnnotateWindow inbox-mode — still dormant (Past Runs
    tab not in this migration; the constructor is available for
    any future caller).
  - AbortHotkey — armed on run start, disarmed on run end.
  - AutoRun pipe forwarding — end-to-end functional.
- **Next phase**: Phase 6 — cutover (~1 day). Flip the default UI
  to the Avalonia build, delete `src/Canary.UI/`, update
  `UiLocator.cs`, run the 8-workflow smoke matrix from the prompt
  §7. Delete the snapshot tag once all 8 are green.

---

## 2026-05-27 — Canary.UI Avalonia migration Phase 4 (annotation surface)

- **Date**: 2026-05-27
- **Commits** (3 total):
  - `cf5d1ed` `feat(ui-avalonia): annotation polish — undo + tool palette + inbox parity`
  - `2b1c0c9` `test(ui-avalonia): AnnotateWindowViewModel tests`
  - pending `docs(progress): Phase 4 — annotation surface`
- **Scope**: Phase 4 builds on the Phase 0 AnnotationCanvas +
  AnnotateWindow baseline. Adds the undo stack (Ctrl+Z), refactors
  AnnotateWindow's code-behind into a testable AnnotateWindowViewModel
  with TWO constructors (session-sink mode + feedback-inbox mode),
  and ships a tool-palette ToggleButton group with an accent-colored
  active-tool indicator. Brings the inbox-write path to byte-parity
  with the WinForms AnnotatedImageForm so Phase 5's Past Runs
  Annotate wire-in only has to hook the constructor.
- **Files added** (2 src + 1 test):
  - `ViewModels/AnnotateWindowViewModel.cs`,
    `ViewModels/ToolModeConverter.cs`.
  - `tests/Canary.Tests/UI.Avalonia/AnnotateWindowViewModelTests.cs`.
- **Files edited**:
  - `Controls/AnnotationCanvas.cs` — undo stack + UndoCount /
    ShapeCount / StateChanged + Clear() snapshot-restore + Text
    shape sibling pairing via tb.Tag.
  - `Views/AnnotateWindow.axaml` — ToggleButton group with
    Classes=\"tool:checked\" style, colored Color buttons, Undo
    button, Ctrl+Z Window.KeyBinding.
  - `Views/AnnotateWindow.axaml.cs` — refactored to construct
    AnnotateWindowViewModel + wire callbacks; three constructors
    expose preview / session-sink / feedback-inbox modes.
  - `docs/features/canary-ui-avalonia.md` — Phase 3 → shipped,
    Phase 4 → in-progress.
  - `docs/progress/2026-05-27-canary-ui-avalonia-migration.md` —
    Phase 4 section + commits + verification gates + next-phase
    preview.
  - `CHANGELOG.md` — Phase 4 detail prepended; combined test delta
    258 → 323 (+65 across phases 0–4).
- **Tests**:
  - Pre-Phase-4: 314 unit tests, 0 failed.
  - Post-Phase-4: 323 unit tests, 0 failed (+9 net new — both
    save modes, error path, tool/color picker, undo/clear
    delegation, ToolModeConverter).
- **Build**: `dotnet build Canary.sln` = 0 warnings, 0 errors. Both
  exes build green.
- **Verification gates (Phase 4)**: 1) build 0/0 ✅; 2) annotation
  round-trip parity with WPF — annotations.json shape unchanged
  (Phase 0 covered); PNG rendering via Avalonia RenderTargetBitmap
  is visually equivalent but not bit-identical to WPF
  PngBitmapEncoder; operator smoke confirms visual correctness;
  3) both flows (session capture + feedback inbox) — session flow
  shipped Phase 0, inbox flow unit-tested + awaits Phase 5 wire-in;
  4) VM tests ✅.
- **Status**: 🟡 Phase 4 code + tests + docs shipped locally.
  Operator review at the phase boundary before Phase 5.
- **Wire-in status**: feedback-inbox mode is **dormant** — the
  AnnotateWindow ctor that accepts (sourcePath, inboxRoot,
  runRef?, checkpointRef?) is unused in Phase 4 because the
  Past Runs Annotate button hasn't been wired into TestsViewModel
  yet. Phase 5 hooks that wire.
- **Next phase**: Phase 5 — services + glue (~2 days). AbortHotkey
  (Pause) port; SingleInstancePipeServer wired into MainWindow +
  AutoRunRequestHandler; drag-and-drop for workload JSON +
  recordings; right-click context menus on tree nodes (which finally
  route to the Phase 3 editors + the Phase 4 inbox-mode
  AnnotateWindow).

---

## 2026-05-27 — Canary.UI Avalonia migration Phase 3 (editors)

- **Date**: 2026-05-27
- **Commits** (5 total):
  - `3e74731` `feat(ui-avalonia): port TestEditorView + VM`
  - `e4995ae` `feat(ui-avalonia): port SuiteEditorView + VM`
  - `37a0f0a` `feat(ui-avalonia): port WorkloadEditorView + VM`
  - `bbe05d5` `test(ui-avalonia): editor VM tests (Test / Suite / Workload)`
  - pending `docs(progress): Phase 3 — editors`
- **Scope**: Phase 3 of the 7-phase Avalonia migration. Port the three
  editors (TestEditor / SuiteEditor / WorkloadEditor) at
  JSON-round-trip-faithful shape — Load → BuildDefinition →
  re-serialize is byte-identical to the input, including for fields
  the editor doesn't surface (Penumbra Setup.Scene/Canvas/
  DisplayPreset/Commands, VLM provider config, TestAction.Extra
  JsonExtensionData). The protection against silent data loss is
  the underlying-POCO-mutation pattern + a dedicated test
  (UnmanagedFields_RoundTripUntouched).
- **Files added** (9 src + 3 tests):
  - `ViewModels/Editors/TestEditorViewModel.cs`,
    `SuiteEditorViewModel.cs`, `WorkloadEditorViewModel.cs` (with
    row-VMs: `CheckpointRow`, `AssertRow`, `TestPickRow`,
    `SetupCommandRow`).
  - `Views/TestEditorView.axaml` (+ `.cs`),
    `SuiteEditorView.axaml` (+ `.cs`),
    `WorkloadEditorView.axaml` (+ `.cs`).
  - `tests/Canary.Tests/UI.Avalonia/Editors/TestEditorViewModelTests.cs`,
    `SuiteEditorViewModelTests.cs`,
    `WorkloadEditorViewModelTests.cs`.
- **Files edited**:
  - `docs/features/canary-ui-avalonia.md` — Phase 2 → shipped,
    Phase 3 → in-progress.
  - `docs/progress/2026-05-27-canary-ui-avalonia-migration.md` —
    Phase 3 section + commits + wire-in status (orphan until
    Phase 5).
  - `CHANGELOG.md` — Phase 3 detail prepended; combined test delta
    258 → 314 (+56 across phases 0–3).
- **Tests**:
  - Pre-Phase-3: 299 unit tests, 0 failed.
  - Post-Phase-3: 314 unit tests, 0 failed (+15 net new — Test
    editor 6, Suite editor 4, Workload editor 5).
- **Build**: `dotnet build Canary.sln` = 0 warnings, 0 errors. Both
  exes build green.
- **Verification gates (Phase 3)**: 1) build 0/0 ✅; 2) edit-and-save
  round-trip covered by unit tests including the unmanaged-fields
  guard ✅; 3) VM tests ✅; 4) CLI regression smoke — CLI untouched
  ✅.
- **Status**: 🟡 Phase 3 code + tests + docs shipped locally.
  Operator review at the phase boundary before Phase 4.
- **Wire-in status**: editors are orphan ViewModels/Views — created
  and tested but not yet routed from the Tests tab. Tree-node context
  menus (edit test / edit suite / edit workload) land in Phase 5
  with `DragDropHandlers`.
- **Next phase**: Phase 4 — annotation polish (~2 days). Build on
  the Phase 0 `AnnotationCanvas` + `AnnotateWindow` baseline:
  tighten hit-testing for the Pointer tool, add an undo stack,
  polish the tool palette + color picker, verify the
  annotate-to-feedback-inbox path matches the WPF version
  byte-for-byte.

---

## 2026-05-27 — Canary.UI Avalonia migration Phase 2 (Tests tab)

- **Date**: 2026-05-27
- **Commits** (7 total):
  - `088b0bd` `feat(ui-avalonia): port WorkloadTree + Welcome to Avalonia`
  - `c12c7d5` `feat(ui-avalonia): port TestRunnerView (live log + progress feed)`
  - `748609c` `feat(ui-avalonia): port ResultsViewerView (approve / reject flows)`
  - `1bb481a` `feat(ui-avalonia): port RecordingView (input record + save)`
  - `fc803f7` `feat(ui-avalonia): TestsView shell + Tests-only toolbar wiring`
  - `c45a501` `test(ui-avalonia): TestRunner + WorkloadTree + ResultsViewer VM tests`
  - pending `docs(progress): Phase 2 — Tests tab`
- **Scope**: Phase 2 of the 7-phase Avalonia migration — the meat of
  the port. Tests tab gets a workload tree on the left + a content-
  swap pane on the right (Welcome / TestRunner / ResultsViewer /
  Recording). Tests-only toolbar items (Run Tests + Mode picker +
  Record) become visible only when Tests is selected. F5 keybinding
  + click handlers drive RunSelection through the Tests VM.
- **Files added** (16): WorkloadExplorer + AvaloniaTestLogger +
  WorkloadTree / Welcome / TestRunner / ResultsViewer / Recording /
  Tests ViewModels + matching Views + 3 VM-test files.
- **Files edited**: `Views/MainWindow.axaml` +
  `ViewModels/MainWindowViewModel.cs` (Tests prepended as first nav
  item; Run Tests / Mode picker / Record toolbar items + F5
  keybinding); docs/features/canary-ui-avalonia.md (Phase 1 →
  shipped, Phase 2 → in-progress); progress log; CHANGELOG.
- **Tests**:
  - Pre-Phase-2: 287 unit tests, 0 failed.
  - Post-Phase-2: 299 unit tests, 0 failed (+12 net new — Tree 3,
    Runner 4, Results 5).
- **Build**: `dotnet build Canary.sln` = 0 warnings, 0 errors. Both
  exes build green.
- **Verification gates (Phase 2)**: 1) build 0/0 ✅; 2) end-to-end Run
  Tests against qualia smoke — pending operator; 3) abort hotkey —
  deferred to Phase 5 (AbortHotkey port; Stop button works in Phase
  2); 4) Approve/Reject disk side covered by unit tests, UI side
  pending operator; 5) drag-and-drop recording — deferred to Phase 5
  (DragDropHandlers); manual Record flow ships in Phase 2; 6) VM
  tests ✅; 7) CLI regression smoke — pending (CLI untouched).
- **Status**: 🟡 Phase 2 code + tests + docs shipped locally.
  Operator review at the phase boundary before Phase 3.
- **Deferred to later phases**: AbortOverlayForm visual overlay
  (Phase 4 polish); Pause-key abort hotkey (Phase 5); drag-and-drop +
  context menus (Phase 5); pass-rate bars + expandable per-test
  sections in ResultsViewer (Phase 4 polish).
- **Next phase**: Phase 3 — editors (~2 days). Port
  `TestEditorControl`, `SuiteEditorControl`, `WorkloadEditorControl`
  with two-way binding against Canary.Core POCO wrappers; goal is
  bytes-identical edit-and-save round-trip.

---

## 2026-05-27 — Canary.UI Avalonia migration Phase 1 (shell + simple panels)

- **Date**: 2026-05-27
- **Commits** (7 total):
  - `52ad6f8` `feat(ui-avalonia): port LocalhostView to Avalonia`
  - `77cb8f7` `feat(ui-avalonia): port FeedbackView to Avalonia`
  - `7221a50` `feat(ui-avalonia): port TelemetryView to Avalonia`
  - `896f34f` `feat(ui-avalonia): port SettingsView to Avalonia`
  - `e56aada` `feat(ui-avalonia): full NavigationView shell + Open Folder toolbar`
  - pending `test(ui-avalonia): Phase 1 panel ViewModel tests`
  - pending `docs(progress): Phase 1 — shell + simple panels`
- **Scope**: Phase 1 of the 7-phase Avalonia migration. Stand up the
  full nav shell + port the four read-only panels (Localhost,
  Feedback, Telemetry, Settings) — mechanical AXAML conversions, no
  new architectural decisions. The toolbar gains an
  Open-workloads-folder action that re-routes both Sessions + Telemetry
  to the picked directory.
- **Files added** (12):
  - `Views/LocalhostView.axaml` + `.cs` + `ViewModels/LocalhostViewModel.cs`.
  - `Views/FeedbackView.axaml` + `.cs` + `ViewModels/FeedbackViewModel.cs`.
  - `Views/TelemetryView.axaml` + `.cs` + `ViewModels/TelemetryViewModel.cs`.
  - `Views/SettingsView.axaml` + `.cs` + `ViewModels/SettingsViewModel.cs`.
- **Files added (tests, 4)**: `LocalhostViewModelTests.cs`,
  `FeedbackViewModelTests.cs`, `TelemetryViewModelTests.cs`,
  `SettingsViewModelTests.cs`.
- **Files edited**:
  - `Views/MainWindow.axaml` + `.cs` — full NavigationView wiring +
    top toolbar + StorageProvider folder picker.
  - `ViewModels/MainWindowViewModel.cs` — eagerly constructs all five
    Phase 1 ViewModels; `OpenWorkloadsFolderCommand`;
    `ApplyWorkloadsDir` routes the picked dir into Sessions +
    Telemetry.
  - `ViewModels/TelemetryViewModel.cs` — test-driven fix:
    `OnSelectedSourceChanged` invalidates the path/mtime cache so the
    source-filter combo actually re-filters rows instead of
    short-circuiting on unchanged mtime.
  - `docs/features/canary-ui-avalonia.md` — Phase 0 status → shipped,
    Phase 1 status → in-progress.
  - `docs/progress/2026-05-27-canary-ui-avalonia-migration.md` — Phase 1
    section + commits + verification gate status + next-phase preview.
  - `CHANGELOG.md` — Phase 1 detail prepended above the Phase 0 block.
- **Tests**:
  - Pre-Phase-1: 270 unit tests, 0 failed.
  - Post-Phase-1: 287 unit tests, 0 failed (+17 net new — Localhost
    4, Feedback 4, Telemetry 3, Settings 6).
- **Build**: `dotnet build Canary.sln` = 0 warnings, 0 errors. Both
  `Canary.UI.exe` and `Canary.UI.Avalonia.exe` build.
- **Verification gates (Phase 1)**: 1) build 0/0 both exes ✅;
  2) unit tests green ✅; 3) manual panel-render smoke — pending
  operator; 4) toolbar visibility smoke — pending operator (Open
  Folder visible everywhere; Tests-only items deferred to Phase 2);
  5) CLI regression smoke — pending (CLI unaffected).
- **Status**: 🟡 Phase 1 code + tests + docs shipped locally.
  Operator review at the phase boundary before Phase 2.
- **Next phase**: Phase 2 — Tests tab (~4 days). Workload tree +
  TestRunnerView + ResultsViewerView + RecordingView; the
  TestRunnerViewModel is the most stateful piece.

---

## 2026-05-27 — Canary.UI Avalonia migration Phase 0 (spike — Sessions panel)

- **Date**: 2026-05-27
- **Commits**:
  - `768d259` `feat(ui-avalonia): Phase 0 spike — Sessions panel in Avalonia`
  - `21ca293` `test(ui-avalonia): SessionsLive + Past ViewModel tests`
  - pending `docs(features): canary-ui-avalonia feature + Phase 0 progress`
- **Scope**: Phase 0 spike of the 7-phase migration from WinForms to
  Avalonia 11 + FluentAvalonia + CommunityToolkit.Mvvm. New project
  `src/Canary.UI.Avalonia/` lives alongside `src/Canary.UI/` for the
  parallel period (phases 0–5). Sessions panel is the layout-pained
  spike target — if it reflows cleanly in Avalonia, the rest of the
  migration is mechanical.
- **Files added** (30, all under `src/Canary.UI.Avalonia/` unless noted):
  - `Canary.UI.Avalonia.csproj` + `app.manifest` — net8.0-windows +
    WinExe + Avalonia 11.2.5 + FluentAvaloniaUI 2.2.0 +
    CommunityToolkit.Mvvm 8.3.2.
  - `Program.cs` + `App.axaml` + `App.axaml.cs` — AppBuilder, dark
    theme, single-instance mutex (distinct from the WinForms one so
    both exes can run in parallel).
  - `Views/MainWindow.axaml` (+ .cs) + `ViewModels/MainWindowViewModel.cs`
    — FluentAvalonia `NavigationView` shell with the Sessions item only
    for the spike.
  - `Views/SessionsView` + `SessionsLiveView` + `SessionsPastView` (+
    .cs each) + matching ViewModels — `TabControl` wrapping Live + Past
    sub-views; Live uses `Grid` + `WrapPanel` so buttons reflow on
    narrow widths.
  - `Views/AnnotateWindow` + `TextInputWindow` + `NotePromptWindow` +
    `CloseoutPromptWindow` (+ .cs each) — modal dialogs sized via
    `SizeToContent=WidthAndHeight`.
  - `Controls/AnnotationCanvas.cs` — Avalonia port of the WPF island;
    same four tool modes + same annotations.json v1 shape.
  - `Hotkeys/SessionHotkeyHook.cs` — Win32 RegisterHotKey against the
    main window HWND via Comctl32 `SetWindowSubclass` (intercepts
    WM_HOTKEY because Avalonia doesn't expose a WndProc message
    filter).
  - `Services/SingleInstancePipeServer.cs` — copied verbatim from
    `src/Canary.UI/SingleInstancePipeServer.cs`.
  - `Services/WorkloadsLocator.cs` — Sessions-scoped subset of
    `MainForm.AutoDetectWorkloadsDir`.
  - `tests/Canary.Tests/UI.Avalonia/SessionsLiveViewModelTests.cs` (9
    tests) + `SessionsPastViewModelTests.cs` (3 tests).
- **Files edited**:
  - `Canary.sln` — adds the new project under the `src` solution
    folder.
  - `tests/Canary.Tests/Canary.Tests.csproj` — adds
    `Canary.UI.Avalonia` project reference.
  - `docs/plans/2026-05-27-canary-ui-avalonia-migration.md` —
    committed alongside the implementation prompt as the parent design
    doc (was untracked at session start).
- **Files created (docs)**:
  - `docs/features/canary-ui-avalonia.md` (status: in-progress).
  - `docs/progress/2026-05-27-canary-ui-avalonia-migration.md` (per-
    phase log).
  - `CHANGELOG.md` Unreleased entry above the bug 0008 entry.
  - This `BUILD_LOG.md` entry.
- **Tests**:
  - Pre-Phase-0: 258 unit tests, 0 failed.
  - Post-Phase-0: 270 unit tests, 0 failed (+12 net new — 9
    SessionsLive + 3 SessionsPast).
- **Build**: `dotnet build Canary.sln` = 0 warnings, 0 errors. Both
  `Canary.UI.exe` and `Canary.UI.Avalonia.exe` produced.
- **Snapshot tag**: `pre-impl-ui-avalonia-2026-05-27` created at HEAD
  (`d393e04` at the time, before this phase landed). Preserved as the
  rollback anchor through Phase 6.
- **Verification gates (Phase 0)**: 1) build 0/0 both exes ✅; 2) unit
  tests green ✅; 3) manual layout smoke — pending operator review;
  4) functional smoke (Sessions round-trip) — pending operator review;
  5) CLI regression smoke — pending; 6) decision gate — pending.
- **Status**: 🟡 Phase 0 code + tests + docs shipped locally. Operator
  review at the phase boundary before Phase 1 begins. **Not pushed**
  per the prompt rule (no push until Phase 6).
- **Next phase**: Phase 1 — shell + simple panels (Localhost, Feedback,
  Telemetry, Settings) + full nav toolbar with Tests-only visibility
  bindings.

---

## 2026-05-27 — Supervised session mode Phase 3 (MCP + cross-repo doc pass)

- **Date**: 2026-05-27
- **Commit**: pending
- **Scope**: ship the Claude-Code-visible half — MCP tools so Claude
  can enumerate + read supervised sessions like it does test runs and
  feedback items, plus the cross-repo doc pass so the next agent
  session in any of the three repos sees the new surface.
- **Files added** (2):
  - `src/Canary.McpServer/Tools/SessionsTools.cs` —
    `ListSessionsTool` (filter by workload + limit, sorted newest
    first) and `GetSessionReportTool` (looks up a session by id +
    returns the full SESSION_REPORT.md). Mirrors RunsTools.cs.
  - `tests/Canary.Tests/Mcp/SessionsToolsTests.cs` — 5 unit tests.
- **Files edited**:
  - `src/Canary.McpServer/Program.cs` — registers the two new
    tools in the dispatch array (8 → 10).
  - `CLAUDE.md` — Quick Reference gains the supervised-session
    line; nav-tab list updated to include "Sessions"; MCP tool
    count bumped to 10.
  - `README.md` — features list gains a supervised-sessions
    bullet; test count line updated.
  - `docs/mcp-server.md` — tool table gains
    `list_sessions` + `get_session_report` rows.
  - `docs/features/supervised-session.md` — Phase 3 status
    flipped to shipped.
  - `docs/progress/2026-05-27-supervised-session.md` — Phase 3
    section + final commit shape.
  - `CHANGELOG.md` — Unreleased Phase 3 entry above the Phase 2
    block.
- **Cross-repo touches** (per CLAUDE.md § Cross-Repo Change Protocol):
  - `C:/Repos/MultiVerse/BUILD_LOG.md` — one-line cross-repo entry
    flagging supervised-session shipped (Canary → operator workflow
    surfaces; no Penumbra/Qualia/CPig code changes needed).
  - `C:/Repos/Qualia/CLAUDE.md` — Canary integration section gains
    a supervised-session pointer ("for exploratory debugging of
    Qualia outside a suite, use the Sessions tab or
    `canary session start --workload qualia`").
- **Tests**:
  - Pre-Phase-3: 253 unit tests, 0 failed.
  - Post-Phase-3: 258 unit tests, 0 failed (5 net new —
    SessionsToolsTests: ListSessionsTool returns valid JSON,
    GetSessionReportTool nonexistent id → not-found message,
    GetSessionReportTool missing sessionId arg → throws,
    Name + schema shape for both tools).
- **Build**: `dotnet build Canary.sln` = 0 warnings, 0 errors.
- **Snapshot tag**: `pre-impl-supervised-session-2026-05-27`
  preserved as the rollback anchor through the three-phase
  implementation; deleted at the end of this session per the
  driving prompt's instruction ("Delete the snapshot tag once
  everything's green").
- **Status**: ✅ shipped (three phases). 12 new tests + 12 new
  files + 10 commits across the three phases. Working tree clean
  at session end (except `.claude/settings.local.json` which is
  the harness's allowlist append, not part of this work).

---

## 2026-05-27 — Supervised session mode Phase 2 (UI nav tab + hotkeys)

- **Date**: 2026-05-27
- **Commit**: pending
- **Scope**: ship the GUI half of the supervised-session feature so the
  operator never has to touch the CLI for an exploratory debug session.
  Click Sessions nav tab → pick workload → Start → Chrome opens visibly
  → Ctrl+Shift+C anywhere captures → End writes the report.
- **Files added** (5):
  - `src/Canary.UI/Hotkeys/SessionHotkeyHook.cs` — registers
    Ctrl+Shift+C (capture) + Ctrl+Shift+A (annotate) against
    MainForm's HWND. Mirrors the AbortHotkey pattern.
  - `src/Canary.UI/Panels/SessionsLiveSubPanel.cs` — armed-state
    machine (idle / starting / armed / ending), workload picker,
    capture buttons, thumbnail strip, note + closeout modal dialogs,
    AnnotatedImageForm integration.
  - `src/Canary.UI/Panels/SessionsPastSubPanel.cs` — list + report
    preview mirroring PastRunsPanel. Filter on workload/session id.
    `ScanRows` is static + testable.
  - `src/Canary.UI/Panels/SessionsPanel.cs` — TabControl wrapping
    Live + Past sub-tabs; routes hotkey messages to the live panel.
  - `tests/Canary.Tests/UI/SessionsPanelTests.cs` — 6 unit tests.
- **Files edited**:
  - `src/Canary.UI/Navigation/NavModes.cs` — adds
    `SessionsNavMode`.
  - `src/Canary.UI/MainForm.cs` — registers the new tab between
    Feedback and Telemetry; tracks loaded workloads for propagation;
    routes WM_HOTKEY through the SessionsNavMode hook.
  - `src/Canary.UI/Annotation/AnnotatedImageForm.cs` — new
    constructor overload taking a sink callback so the session can
    own the destination paths instead of FeedbackInboxWriter.
  - `src/Canary.UI/Canary.UI.csproj` — adds Canary.Harness project
    reference so SessionsLiveSubPanel can use SessionAgentFactory
    (single-sourced agent dispatch).
  - `tests/Canary.Tests/Navigation/NavModeTests.cs` — adds
    SessionsNavMode to the AllNavModes theory member.
  - `docs/features/supervised-session.md` — Phase 2 UI workflow
    section + status flip.
- **Tests**:
  - Pre-Phase-2: 244 unit tests, 0 failed.
  - Post-Phase-2: 253 unit tests, 0 failed (9 net new: 6
    SessionsPanelTests, 3 NavModeTests theory rows for
    SessionsNavMode).
- **Build**: `dotnet build Canary.sln` = 0 warnings, 0 errors.
- **Hardware-bearing follow-up for operator**: open
  `src/Canary.UI/bin/Debug/net8.0-windows/Canary.UI.exe`, click the
  new **Sessions** tab → **Live** → pick `qualia` → **Start session**
  → wait for Chrome → press **Ctrl+Shift+C** to capture →
  **Ctrl+Shift+A** to capture + annotate → **End session** → enter
  closeout → switch to **Past sessions** tab and confirm the entry
  appears with the just-written report embedded.
- **Status**: Phase 2 ✅ ready for review. Phase 3 (MCP +
  cross-repo docs) next.

---

## 2026-05-27 — Supervised session mode Phase 1 (CLI + storage)

- **Date**: 2026-05-27
- **Commit**: pending
- **Scope**: ship the no-tests-running mode operators have been wanting
  — boot a workload's target app under Canary supervision, drive it
  manually in the visible window, hit a single key to capture, close
  out with a bundled `SESSION_REPORT.md`. Phase 1 is CLI-only + storage
  layer; UI nav tab + hotkeys (Phase 2) and MCP tools (Phase 3) follow.
- **Files added** (10):
  - `src/Canary.Core/Session/{SessionPaths, CaptureSlugGenerator,
    SessionTypes, SessionReportWriter, ISessionAgentFactory,
    SupervisedSession}.cs`
  - `src/Canary.Harness/Session/SessionAgentFactory.cs`
  - `src/Canary.Harness/Cli/SessionCommand.cs`
  - `tests/Canary.Tests/Session/{SessionPathsTests,
    CaptureSlugGeneratorTests, SessionReportWriterTests,
    SupervisedSessionTests}.cs`
  - `docs/features/supervised-session.md`
  - `docs/progress/2026-05-27-supervised-session.md`
- **Files edited**: `src/Canary.Harness/Program.cs` (register
  `SessionCommand`); `CHANGELOG.md` (Unreleased entry).
- **Snapshot tag**: `pre-impl-supervised-session-2026-05-27` at
  `da9357b` — rollback anchor for the three-phase implementation.
- **Tests**:
  - Baseline at start of session: 220 unit tests, 0 failed.
  - Phase 1 end: 244 unit tests, 0 failed (24 new — 8 SessionPaths, 7
    CaptureSlugGenerator, 5 SessionReportWriter, 4 SupervisedSession
    end-to-end with stub agent).
  - Integration tests count unchanged (the live Qualia/Penumbra smoke
    is deferred to operator on a machine with the dev env ready).
- **Build**: `dotnet build Canary.sln` = 0 warnings, 0 errors.
- **CLI smoke**: `canary --help` shows `session`; `canary session
  --help` shows `start`/`list`/`report`; `canary session list`
  returns "(no sessions found)" cleanly against empty state.
- **Hardware-bearing follow-up for operator**: run
  `canary session start --workload qualia` in `C:\Repos\Canary\`,
  press `c` once Qualia mounts, press `q` to end, type a one-line
  close-out at the prompt, then open
  `workloads/qualia/sessions/<id>/SESSION_REPORT.md` to confirm the
  embedded screenshot renders + `telemetry.ndjson` contains the
  `Screenshot` envelope.
- **Status**: Phase 1 ✅ ready for review. Phase 2 (UI nav tab) and
  Phase 3 (MCP + cross-repo docs) queued.

---

## 2026-05-27 — Qualia eager-L3 Move 4 follow-up

- **Date**: 2026-05-27
- **Commit**: `e237503`
- **Scope**: new `eager-l3-provider-swap.json` fixture (asserts the
  Move 4 provider-aware cache short-circuit + the per-provider
  breakdown in the `sidecar.behavior-cache` dev test). Uses
  OpenAI-compat-at-Ollama as the swap target so the fixture is
  hermetic to the local machine; doesn't require Anthropic
  credentials. ~150s wall clock.
- **Coordinated with**: Qualia commits `4b1389e` → `5eee39a`
  (Move 4 — Phase 2 v2 closeout: byte-cap gate + provider id on
  cache entries + provider-aware freshness key).
- **Tests added**: 1 fixture (`eager-l3-provider-swap`).
- **Suite count**: `eager-l3.json` now 6 fixtures (was 5).
- **Status**: Pending operator baseline approval via `Canary.UI.exe`.

---

## 2026-05-27 — Qualia eager-L3 Move 3 follow-up

- **Date**: 2026-05-27
- **Commit**: `3ab6c08`
- **Scope**: new `eager-l3-progress-badge.json` fixture (asserts the Qualia
  EagerExtractionProgressBadge surfaces during a live sweep via the new
  `__canaryGetProgressBadgeState()` hook); updated `cold-launch.json` +
  `warm-launch.json` to enable the new `compute.rag.eager-l3` persona
  before Reload (Qualia Move 3 added a persona gate; default-off in the
  standard profile would otherwise regress these fixtures); suite +
  AGENT_NOTES + `spec/QUALIA_WORKLOAD.md` updated to reflect the 5-fixture
  roster.
- **Coordinated with**: Qualia commits `e2503b6` → `1f97a84` (Move 3 +
  Phase 2 v2 interface extension).
- **Tests added**: 1 fixture (`eager-l3-progress-badge`).
- **Suite count**: `eager-l3.json` now 5 fixtures (was 4).
- **Status**: Pending operator baseline approval for the 5 fixtures via
  `Canary.UI.exe` (cold/warm may need re-shoot per persona-enable timing
  shift).

---

## Phase 0: Solution Scaffold + CLI Shell

### Checkpoint 0.1: Solution Structure
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: N/A (structural checkpoint)
- **Tests Passed**: N/A
- **Tests Failed**: 0
- **Issues Found**: `dotnet new sln` creates `.slnx` by default on .NET 9 SDK — used `--format sln` flag to force classic `.sln` format as specified.
- **Resolution**: Recreated solution with `--format sln`
- **SUPERVISOR_FLAGS**: None

### Checkpoint 0.2: CLI Entry Point
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: Manual CLI verification
- **Tests Passed**: `canary --help` prints usage with all 4 subcommands; `canary run --help` prints `--workload` and `--test` options
- **Tests Failed**: 0
- **Issues Found**: `Console.TreatControlCAsInput = false` throws `IOException` when no real console handle is available (non-interactive terminals). Wrapped in try-catch.
- **Resolution**: Guarded with `try { } catch (IOException) { }`
- **SUPERVISOR_FLAGS**: None

### Checkpoint 0.3: Test Foundation
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: Program_NoArgs_PrintsHelp, Program_RunHelp_PrintsRunUsage, Program_CtrlCHandler_IsRegistered
- **Tests Passed**: 3
- **Tests Failed**: 0
- **Issues Found**: None
- **Resolution**: N/A
- **SUPERVISOR_FLAGS**: None

### Phase 0 Gate Verification
- [x] `Canary.sln` builds with `dotnet build` — 0 errors, 0 warnings
- [x] `canary.exe` runs and prints help text
- [x] `canary run --help` prints usage for the run command
- [x] `Ctrl+C` handler registered (verified by test)
- [x] `Canary.Tests` project with 3 passing unit tests
- [x] `.gitignore` excludes bin/obj/results
- [x] `README.md` with project overview

## Phase 1: Named Pipe IPC + Agent Protocol

### Checkpoint 1.1: RPC Message Types
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: RpcMessage_SerializeRequest_RoundTrips, RpcMessage_SerializeResponse_WithResult_RoundTrips, RpcMessage_SerializeError_RoundTrips, RpcMessage_DeserializeInvalid_ThrowsClear, RpcMessage_SerializeRequest_WithParams_RoundTrips
- **Tests Passed**: 5
- **Tests Failed**: 0
- **Issues Found**: `System.Text.Json` not available in net48 by default — added NuGet package.
- **Resolution**: Added `System.Text.Json` v10.0.5 NuGet to `Canary.Agent.csproj`
- **SUPERVISOR_FLAGS**: None

### Checkpoint 1.2: Agent Interface
- **Date**: 2026-04-04
- **Status**: PASS (completed in Phase 0 — ICanaryAgent + data contracts already defined)
- **SUPERVISOR_FLAGS**: None

### Checkpoint 1.3: Pipe Server (Agent Side)
- **Date**: 2026-04-04
- **Status**: PASS
- **Issues Found**: net48 doesn't support `StreamReader(Stream, leaveOpen:)` shorthand or `ReadLineAsync(CancellationToken)`. Fixed with explicit encoding constructors and `Task.WhenAny` pattern. Also UTF8 BOM corruption — used `new UTF8Encoding(false)`.
- **Resolution**: Used `UTF8Encoding(false)`, explicit constructor params, `Task.WhenAny` for cancellable reads.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 1.4: Pipe Client (Harness Side)
- **Date**: 2026-04-04
- **Status**: PASS
- **Issues Found**: Same net48 API issues as server. Also `StreamWriter.Dispose()` throws on broken pipe — guarded in `Dispose()`.
- **Resolution**: Wrapped `_writer?.Dispose()` in try-catch for `IOException`.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 1.5: IPC Round-Trip Tests
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: AgentServer_Heartbeat_ReturnsOk, HarnessClient_Timeout_ThrowsTimeoutException, HarnessClient_Execute_PassesParams, AgentServer_CaptureScreenshot_ReturnsMockPath, HarnessClient_SequentialRequests_AllSucceed, AgentServer_Shutdown_DisconnectsCleanly
- **Tests Passed**: 6
- **Tests Failed**: 0
- **Issues Found**: None remaining
- **SUPERVISOR_FLAGS**: None

### Phase 1 Gate Verification
- [x] `Canary.Agent.csproj` builds as multi-target `net8.0;net48` class library
- [x] `ICanaryAgent` interface with Execute, CaptureScreenshot, Heartbeat, Abort
- [x] `AgentServer` listens on named pipe, dispatches JSON-RPC to ICanaryAgent
- [x] `HarnessClient` connects to pipe, sends JSON-RPC, awaits responses with timeout
- [x] Round-trip test: heartbeat returns ok=true
- [x] Timeout test: throws TimeoutException when agent doesn't respond
- [x] Pipe name format: `canary-{name}-{pid}`
- [x] All Phase 0 tests still pass (regression check: 14/14)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 2: Input Recorder + Replayer

### Checkpoint 2.1: Input Event Model
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: InputEvent_Serialize_RoundTrips, InputRecording_Serialize_PreservesMetadata
- **Tests Passed**: 2
- **Tests Failed**: 0
- **Issues Found**: xUnit analyzer enforces `Assert.Single` over `Assert.Equal(1, count)` (TreatWarningsAsErrors)
- **Resolution**: Used `Assert.Single` as required
- **SUPERVISOR_FLAGS**: None

### Checkpoint 2.2: Viewport Locator
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: NormalizeDenormalize_RoundTrips, TopLeft_ReturnsZeroZero, BottomRight_ReturnsOneOne, DifferentViewportSize_ScalesCorrectly, Center_NormalizesToHalf, FindWindow_BadTitle_ReturnsZero, IsValidTarget_Zero_ReturnsFalse, GetViewportBounds_Zero_ReturnsEmptyBounds
- **Tests Passed**: 8
- **Tests Failed**: 0
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 2.3: Input Recorder
- **Date**: 2026-04-04
- **Status**: PASS
- **Issues Found**: `Thread.SetApartmentState` triggers CA1416 platform analyzer on `net8.0`. Changed target to `net8.0-windows` for Harness, Tests, and Tests.Integration projects. This is correct — Canary is Windows-only (SendInput, named pipes, Win32 hooks).
- **Resolution**: Changed TargetFramework from `net8.0` to `net8.0-windows` for Canary.Harness, Canary.Tests, Canary.Tests.Integration
- **SUPERVISOR_FLAGS**: None

### Checkpoint 2.4: Input Replayer
- **Date**: 2026-04-04
- **Status**: PASS
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Phase 2 Gate Verification
- [x] `InputEvent` and `InputRecording` serialize/deserialize correctly
- [x] Coordinate normalization/denormalization round-trips within ±1px
- [x] `ViewportLocator` finds windows by title, gets client area bounds
- [x] `InputRecorder` uses `SetWindowsHookEx` with WH_MOUSE_LL/WH_KEYBOARD_LL on STA thread
- [x] `InputReplayer` reads recordings, denormalizes coords, injects via SendInput with MOUSEEVENTF_ABSOLUTE
- [x] Replayer supports CancellationToken, SpeedMultiplier, checkpoint pause callbacks
- [x] Regression check: all Phase 0+1 tests pass (24/24 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 3: Screenshot Comparison Engine

### Checkpoint 3.1: Pixel Diff Comparer
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: PixelDiffComparer_IdenticalImages_ReturnsZeroDiff, PixelDiffComparer_SinglePixelDiff_ReturnsCorrectCount, PixelDiffComparer_TenPercentNoise_ReturnsApproxTenPercent, PixelDiffComparer_BelowThreshold_CountsAsSame, PixelDiffComparer_AboveThreshold_CountsAsDifferent, PixelDiffComparer_DimensionMismatch_ThrowsArgumentException, PixelDiffComparer_DiffImage_HighlightsChanges, PixelDiffComparer_ToleranceGate_PassesWhenBelowTolerance, PixelDiffComparer_ToleranceGate_FailsWhenAboveTolerance
- **Tests Passed**: 9
- **Tests Failed**: 0
- **Issues Found**: Nested `ProcessPixelRows` lambdas fail with CS9108 (ref-like type in lambda). Switched to `CopyPixelDataTo` + `Image.LoadPixelData` approach.
- **Resolution**: Use flat array comparison instead of nested pixel accessor lambdas.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 3.2: SSIM Comparer
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: SsimComparer_IdenticalImages_ReturnsOne, SsimComparer_CompletelyDifferent_ReturnsLow, SsimComparer_SlightShift_ReturnsHigh
- **Tests Passed**: 3
- **Tests Failed**: 0
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 3.3: Composite Builder
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: CompositeBuilder_ThreeCheckpoints_CorrectDimensions, CompositeBuilder_ZeroCheckpoints_ReturnsNull, CompositeBuilder_SingleCheckpoint_ProducesValidImage, CompositeBuilder_LabelsIncludeStatus
- **Tests Passed**: 4
- **Tests Failed**: 0
- **Issues Found**: `SixLabors.ImageSharp.Drawing` NuGet needed for `DrawImage` compositing (not included in base ImageSharp).
- **Resolution**: Added `SixLabors.ImageSharp.Drawing` v2.1.6 NuGet to Harness and Tests projects.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 3.4: Test Data
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: All test images generated programmatically in test fixture setup — no binary files committed. Tests use `CreateSolidImage`, `CreateGradientImage` helpers.
- **SUPERVISOR_FLAGS**: None

### Phase 3 Gate Verification
- [x] `PixelDiffComparer` compares images pixel-by-pixel with configurable colorThreshold
- [x] `ComparisonResult` includes DiffPercentage, ChangedPixels, TotalPixels, DiffImage, Passed
- [x] Diff image renders changed pixels in magenta, unchanged as semi-transparent
- [x] `SsimComparer` computes SSIM with 8x8 sliding window, returns [0,1] score
- [x] SSIM uses grayscale luminance (0.299R + 0.587G + 0.114B) and standard C1/C2 constants
- [x] `CompositeBuilder` stitches baseline|candidate|diff horizontally with 2px gaps
- [x] Composite stacks strips vertically with label bars (green=pass, red=fail)
- [x] Test images generated programmatically (no binary files committed)
- [x] Regression check: all Phase 0+1+2+3 tests pass (40/40 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 4: Test Runner + Orchestrator

### Checkpoint 4.1: Test Definition Parser
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: TestDefinition_Parse_ValidJson_AllFieldsPopulated, TestDefinition_Parse_MissingName_ThrowsClearError, TestDefinition_Parse_EmptyCheckpoints_IsValid, WorkloadConfig_Parse_AllFieldsPopulated
- **Tests Passed**: 4
- **Tests Failed**: 0
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 4.2: App Launcher + Process Manager
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: ProcessManager_Track_KillAll_TerminatesProcess, ProcessManager_KillAll_AlreadyExited_NoError
- **Tests Passed**: 2
- **Tests Failed**: 0
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 4.3: Watchdog
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: Watchdog_HealthyAgent_NoEvent, Watchdog_UnresponsiveAgent_FiresDeadEvent, Watchdog_Cancellation_StopsCleanly
- **Tests Passed**: 3
- **Tests Failed**: 0
- **Issues Found**: None. Used `IHeartbeatSource` interface for testability — mock heartbeat sources for unit tests, `HarnessClientHeartbeatSource` adapter for production.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 4.4: Test Runner Core
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `TestRunner` orchestrates full lifecycle: launch → connect → setup → checkpoint capture → compare → composite. `TestResult`/`CheckpointResult`/`SuiteResult` models with pass/fail/crash/new statuses. Handles missing baselines (NEW), dimension mismatches (FAIL), app crashes (via watchdog), cancellation.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 4.5: CLI Integration
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `canary run --workload <name>` discovers and runs all tests. `canary run --workload <name> --test <name>` runs single test. `canary approve --workload <name> --test <name>` copies candidates to baselines. Console output includes timestamped status with "Press Ctrl+C to abort". Summary shows pass/fail/crash/new counts.
- **SUPERVISOR_FLAGS**: None

### Phase 4 Gate Verification
- [x] `TestDefinition` and `WorkloadConfig` parse from JSON with validation
- [x] Missing required fields throw clear `JsonException` messages
- [x] `AppLauncher` starts processes and polls for named pipe availability
- [x] `ProcessManager` tracks processes and `KillAll()` terminates them safely
- [x] `Watchdog` monitors heartbeats, fires `OnAppDead` after 3 consecutive failures
- [x] `TestRunner` orchestrates full test lifecycle with checkpoint capture + comparison
- [x] Missing baseline → NEW status with guidance to run `canary approve`
- [x] `canary run --workload` and `canary approve --workload --test` functional
- [x] Console output: timestamped PASS/FAIL/CRASH/NEW with diff percentages
- [x] Ctrl+C propagated via CancellationToken, `ProcessManager.KillAll()` on shutdown
- [x] Regression check: all Phase 0+1+2+3+4 tests pass (49/49 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 5: HTML Report + Polish

### Checkpoint 5.1: HTML Report Generator
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: HtmlReportGenerator_SingleTest_ProducesValidHtml, HtmlReportGenerator_FailedTest_ShowsRed
- **Tests Passed**: 2
- **Tests Failed**: 0
- **Details**: Self-contained HTML with inline base64 images. Dark theme CSS. Header with pass/fail/crash/new summary badges. Per-test cards with status badge, checkpoint table (name/status/diff%/tolerance/SSIM), composite image. Failed tests sorted before passed.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 5.2: JUnit Report Generator
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: JUnitReportGenerator_ProducesValidXml
- **Tests Passed**: 1
- **Tests Failed**: 0
- **Details**: Standard JUnit XML via `System.Xml.Linq`. `<testsuite>` with `<testcase>` elements. Failed tests get `<failure>` with diff info. Crashed tests get `<error>`. New tests get `<skipped>`. Per-checkpoint details in `<system-out>`.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 5.3: Console UI Polish
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `Program.LogStatus(symbol, message, color)` provides color-coded console output (Green=PASS, Red=FAIL, Magenta=CRASH, Yellow=NEW). `--verbose` flag shows per-checkpoint diff/ssim/tolerance details. `--quiet` suppresses all output except summary line and exit code (for CI). Every status line includes "Press Ctrl+C to abort".
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 5.4: Edge Cases + Report Command
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `canary report` command finds most recent `report.html` and opens via `Process.Start(UseShellExecute: true)`. Supports optional `--workload` to scope the search. Empty suite (0 checkpoints) handled gracefully — no composite built, summary still prints. Missing baselines produce NEW status with guidance message. Report generation runs after every suite execution.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Phase 5 Gate Verification
- [x] `HtmlReportGenerator` produces self-contained HTML with embedded base64 images
- [x] Dark theme CSS, failed tests sorted first, status badges (pass/fail/crash/new)
- [x] Per-test checkpoint table with diff%, tolerance, SSIM columns
- [x] `JUnitReportGenerator` produces valid XML parseable by CI systems
- [x] `<failure>` elements for failed tests, `<error>` for crashed, `<skipped>` for new
- [x] `canary run` generates HTML + JUnit reports to `workloads/{name}/results/`
- [x] `canary report` opens most recent report in default browser
- [x] `--verbose` shows per-checkpoint details, `--quiet` suppresses non-summary output
- [x] Color-coded console output: Green/Red/Magenta/Yellow for PASS/FAIL/CRASH/NEW
- [x] "Press Ctrl+C to abort" in every status line
- [x] Regression check: all Phase 0+1+2+3+4+5 tests pass (52/52 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 6: Rhino Workload Agent

### Checkpoint 6.1: Rhino Plugin Shell
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `Canary.Agent.Rhino` project targeting `net48` with RhinoCommon v8.15.25013.13001 NuGet (compile-only). `CanaryRhinoPlugin` extends `Rhino.PlugIns.PlugIn`, starts `AgentServer` on a background thread via `Task.Run` on plugin load. Pipe name: `canary-rhino-{pid}`. Graceful shutdown via `CancellationTokenSource` in `OnShutdown()`.
- **Issues Found**: RhinoCommon v8.15.25012.13001 not available; resolved to v8.15.25013.13001 (NuGet NU1603 with TreatWarningsAsErrors). Used exact available version.
- **Resolution**: Pinned RhinoCommon to v8.15.25013.13001.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 6.2: Rhino Agent Implementation
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `RhinoAgent : ICanaryAgent` implements:
  - `ExecuteAsync("OpenFile", {"path": "..."})` → `RhinoDoc.Open(path)`
  - `ExecuteAsync("RunCommand", {"command": "..."})` → `RhinoApp.RunScript(command, echo: false)`
  - `ExecuteAsync("SetViewport", {...})` → configures projection (Perspective/Parallel/Top/Front/Right), display mode, viewport size
  - `ExecuteAsync("SetView", {"name": "..."})` → restores named view or uses `_-SetView` command fallback
  - `HeartbeatAsync()` → returns ok=true with rhinoVersion, documentName, objectCount state
  - `AbortAsync()` → sends `_Cancel` keystroke
- **Issues Found**: `NamedViewTable.Restore` API changed between Rhino versions — `RestoreAnimated(int, RhinoView, bool)` marked obsolete, `Restore(int, RhinoView)` signature wrong. Correct signature is `Restore(int, RhinoViewport)`.
- **Resolution**: Used `doc.NamedViews.Restore(index, view.ActiveViewport)`.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 6.3: Rhino Screenshot Capture
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `RhinoScreenCapture.Capture(CaptureSettings)` uses `ViewCaptureSettings` + `ViewCapture.CaptureToBitmap` to capture the active viewport at requested dimensions (72 DPI). Saves as PNG via `System.Drawing.Imaging.ImageFormat.Png`. Validates: no active viewport → `InvalidOperationException`, invalid dimensions → `ArgumentException`, null bitmap → `InvalidOperationException`. Ensures output directory exists before saving.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 6.4: Smoke Test Definition
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `workloads/rhino/workload.json` (Rhino 8 config with `/nosplash` arg, 30s startup timeout, pipe name `canary-rhino`) and `workloads/rhino/tests/smoke-test.json` (creates a sphere, sets perspective shaded viewport, captures one screenshot at 2% tolerance). End-to-end verification requires Rhino installed — this is an Integration test.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Phase 6 Gate Verification
- [x] `Canary.Agent.Rhino` builds as a `net48` class library referencing RhinoCommon (compile-only)
- [x] Plugin starts `AgentServer` on background thread on load, pipe name `canary-rhino-{pid}`
- [x] `RhinoAgent.Execute("OpenFile", {"path"})` opens .3dm files via `RhinoDoc.Open`
- [x] `RhinoAgent.Execute("SetViewport", {...})` configures projection, display mode, size
- [x] `RhinoAgent.Execute("RunCommand", {"command"})` runs commands via `RhinoApp.RunScript`
- [x] `RhinoAgent.Execute("SetView", {"name"})` restores named views or standard projections
- [x] `RhinoAgent.CaptureScreenshot` captures via `ViewCapture.CaptureToBitmap`, saves PNG
- [x] `RhinoAgent.Heartbeat` returns ok=true with rhinoVersion, documentName, objectCount
- [x] Smoke test definition created: `workloads/rhino/tests/smoke-test.json`
- [x] Agent server shuts down gracefully on plugin unload
- [x] Regression check: all Phase 0+1+2+3+4+5 unit tests pass (52/52 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 7: Future Workloads (Stub)

### Checkpoint 7.1: Agent Template
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `docs/creating-a-workload.md` — comprehensive guide covering: project setup, `ICanaryAgent` implementation, `AgentServer` startup on background thread, `workload.json` configuration, test definition authoring, tolerance guidelines, and full examples for WPF (`RenderTargetBitmap`) and Electron (Chrome DevTools Protocol) applications. Includes a pre-flight checklist.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 7.2: Qualia Stub
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `workloads/qualia/workload.json` with placeholder configuration and `workloads/qualia/AGENT_NOTES.md` documenting `ICanaryAgent` method mappings for Qualia — scene loader for OpenFile, command system for RunCommand, 3D viewport for SetViewport, camera presets for SetView, and framebuffer readback or `RenderTargetBitmap` for screenshot capture. Notes architecture considerations (framework target, GPU capture, UI thread marshalling).
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 7.3: Penumbra Stub
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `workloads/penumbra/workload.json` with placeholder configuration and `workloads/penumbra/AGENT_NOTES.md` documenting `ICanaryAgent` method mappings for Penumbra — geometry tree loader, shader parameter control, WebGL canvas capture via `toDataURL`/`toBlob`. Documents two agent approaches: Electron main-process agent vs Chrome DevTools Protocol bridge. Includes Penumbra-specific actions (SetGeometry, SetShaderParam, ToggleWireframe).
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Phase 7 Gate Verification
- [x] `docs/creating-a-workload.md` guide with step-by-step instructions
- [x] Guide covers: ICanaryAgent implementation, AgentServer startup, workload.json, test definitions
- [x] WPF example using `RenderTargetBitmap` with `Dispatcher.Invoke`
- [x] Electron/web example using Chrome DevTools Protocol
- [x] `workloads/qualia/workload.json` placeholder created
- [x] `workloads/qualia/AGENT_NOTES.md` with ICanaryAgent method mapping
- [x] `workloads/penumbra/workload.json` placeholder created
- [x] `workloads/penumbra/AGENT_NOTES.md` with ICanaryAgent method mapping and architecture notes
- [x] Regression check: all unit tests pass (52/52 total)
- [x] `dotnet build` — 0 errors, 0 warnings
- [x] Documentation review: all guides accurate against current codebase

## Phase 8: Rhino .rhp Fix + Canary.Core Extraction

### Checkpoint 8.1: Rhino Plugin .rhp Fix
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Added `<TargetExt>.rhp</TargetExt>`, `<UseWindowsForms>true</UseWindowsForms>`, `<ImportWindowsDesktopTargets>true</ImportWindowsDesktopTargets>`, `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` to `Canary.Agent.Rhino.csproj`. Created `Properties/AssemblyInfo.cs` with `[assembly: Guid]` and `[assembly: PlugInDescription]` attributes. Build produces `Canary.Agent.Rhino.rhp`.
- **Issues Found**: GUID "CANARY00A001" contains non-hex characters — `[assembly: Guid]` requires valid hex.
- **Resolution**: Changed to "CA0A4700A001" (valid hex).
- **SUPERVISOR_FLAGS**: None

### Checkpoint 8.2: Create Canary.Core Project
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `src/Canary.Core/Canary.Core.csproj` (net8.0-windows, `<RootNamespace>Canary</RootNamespace>`). NuGet: SixLabors.ImageSharp 3.1.12, SixLabors.ImageSharp.Drawing 2.1.6. ProjectReference: Canary.Agent. `<InternalsVisibleTo Include="Canary.Tests" />`. Added to Canary.sln.
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 8.3: Move Comparison Engine to Core
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Moved `PixelDiffComparer`, `SsimComparer`, `ComparisonResult`, `CheckpointComparison`, `CompositeBuilder` from Harness to Core. Namespace remains `Canary.Comparison` (no change needed thanks to matching RootNamespace). Removed ImageSharp NuGets from Harness (come transitively from Core).
- **Issues Found**: None
- **SUPERVISOR_FLAGS**: None

### Checkpoint 8.4: Move Config, Models, Reporting, Input to Core
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Moved all shared types:
  - `Config/`: TestDefinition, WorkloadConfig
  - `Orchestration/`: TestResult, ProcessManager, AppLauncher, Watchdog, TestRunner
  - `Reporting/`: HtmlReportGenerator, JUnitReportGenerator
  - `Input/`: InputEvent, InputRecording, InputRecorder, InputReplayer, ViewportLocator
  Harness retains only: Program.cs, ConsoleTestLogger.cs, Cli/*.cs commands. Updated Tests.csproj to reference both Core and Harness.
- **Issues Found**: TestRunner.cs references `Program.Log`/`Program.LogStatus`/`Program.Verbose` which don't exist in Core — 11 compile errors.
- **Resolution**: Resolved in checkpoint 8.5 (ITestLogger abstraction).
- **SUPERVISOR_FLAGS**: None

### Checkpoint 8.5: Decouple TestRunner via ITestLogger
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: ConsoleTestLogger_Log_WritesTimestampedOutput, ConsoleTestLogger_Quiet_SuppressesLog, ConsoleTestLogger_LogSummary_AlwaysWrites
- **Tests Passed**: 3 new (55 total)
- **Tests Failed**: 0
- **Details**: Created `ITestLogger` interface in Core with `Log`, `LogStatus`, `LogSummary`, `Verbose` members plus `TestStatusLevel` enum. Refactored `TestRunner` constructor to accept `ITestLogger`. Extracted `DiscoverTestsAsync` → `TestDiscovery` and `ApproveTest` → `BaselineManager` (both in Core). `BaselineManager` also includes `ApproveCheckpoint`/`RejectCheckpoint` for future GUI use. Created `ConsoleTestLogger` in Harness implementing `ITestLogger`. Updated `RunCommand` and `ApproveCommand` to wire new types. Added `InternalsVisibleTo` to Harness for test access to `BuildRootCommand`.
- **Issues Found**: Tests couldn't see `Program.BuildRootCommand` (internal) after `InternalsVisibleTo` moved from Harness to Core.
- **Resolution**: Added `<InternalsVisibleTo Include="Canary.Tests" />` to Harness csproj as well.
- **SUPERVISOR_FLAGS**: None

### Phase 8 Gate Verification
- [x] `Canary.Agent.Rhino` builds as `.rhp` (verified in bin/Debug/net48/)
- [x] `Properties/AssemblyInfo.cs` with valid GUID and PlugInDescription attributes
- [x] `Canary.Core` project exists with all shared logic
- [x] Harness is a thin CLI shell (Program.cs, ConsoleTestLogger.cs, Cli/ commands)
- [x] `ITestLogger` interface decouples logging from Console statics
- [x] `TestRunner` accepts `ITestLogger` via constructor injection
- [x] `BaselineManager` extracted with `ApproveTest`, `ApproveCheckpoint`, `RejectCheckpoint`
- [x] `TestDiscovery` extracted with `DiscoverTestsAsync`
- [x] `ConsoleTestLogger` in Harness implements `ITestLogger` with quiet/verbose support
- [x] Both CLI commands (`run`, `approve`) wired to new Core types
- [x] Regression check: all tests pass (55/55 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 9: WinForms Application Shell

### Checkpoint 9.1: Create Canary.UI Project
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Created `src/Canary.UI/Canary.UI.csproj` (WinExe, net8.0-windows, UseWindowsForms). References Canary.Core and Canary.Agent. `Program.cs` with `ApplicationConfiguration.Initialize()` and `Application.Run(new MainForm())`. Added to Canary.sln.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 9.2: Main Window Layout
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `MainForm` with dark theme (VS Code-inspired colors). `ToolStrip` with 5 buttons: Open Folder, Run Tests, Record, Approve, View Report. `SplitContainer` (vertical) with `TreeView` (250px left panel) and content `Panel`. `StatusStrip` with status label and test count. Minimum size 1024x768, default 1280x900. Custom `DarkToolStripRenderer` for consistent theming.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 9.3: Workload Discovery and Tree Population
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `WorkloadExplorer` service scans workloads directory, loads `WorkloadConfig` + `TestDefinition` per subdirectory. Tree populated with workload -> test hierarchy. Auto-detects `workloads/` relative to exe or CWD. "Open Folder" button shows `FolderBrowserDialog`. `WelcomePanel` with branding and instructions shown when no test is selected.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 9.4: UI Tests
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: LoadWorkloads_ValidDirectory_DiscoversWorkloads, LoadWorkloads_EmptyDirectory_ReturnsEmpty, LoadWorkloads_MissingTestsDir_ReturnsWorkloadWithNoTests
- **Tests Passed**: 3 new (58 total)
- **Tests Failed**: 0
- **Issues Found**: Test JSON for test definitions was missing required `workload` field.
- **Resolution**: Added `workload` field to test fixture JSON.
- **SUPERVISOR_FLAGS**: None

### Phase 9 Gate Verification
- [x] `Canary.UI.exe` builds as WinExe with WinForms
- [x] `MainForm` has ToolStrip, SplitContainer (TreeView + content Panel), StatusStrip
- [x] Dark theme with consistent colors across all controls
- [x] `WorkloadExplorer` discovers workloads and test definitions
- [x] TreeView populated with workload -> test hierarchy
- [x] Auto-detection of `workloads/` directory on startup
- [x] `WelcomePanel` shown when no test is selected
- [x] Regression check: all tests pass (58/58 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 10: Results Viewer + Baseline Management

### Checkpoint 10.1: Results Viewer Control
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `ResultsViewerControl` (UserControl) displays test results. Header with test name, status badge, duration, "Approve All" button. Per-checkpoint rows with stats (diff%, tolerance, SSIM), three `PictureBox` controls (baseline/candidate/diff, SizeMode=Zoom), approve/reject buttons. Events: `ApproveCheckpointRequested`, `RejectCheckpointRequested`, `ApproveAllRequested`, `ImageClicked`.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 10.2: Full-Size Image Viewer
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `ImageViewerForm` (modal Form) shows full-resolution images. Toolbar to toggle baseline/candidate/diff. Mouse wheel zoom (0.1x-10x), click-drag pan via `ScrollableControl.AutoScrollPosition`. Keyboard: Escape closes, Left/Right switch images, +/- zoom. Non-locking file load via `FileStream`.
- **Issues Found**: `AutoScrollPosition` is on `ScrollableControl`, not `Control` — cast needed for `_pictureBox.Parent`.
- **Resolution**: Cast parent to `ScrollableControl` with pattern matching.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 10.3: Approve/Reject from GUI
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `BaselineManager` (already created in Phase 8.5) provides `ApproveCheckpoint` and `RejectCheckpoint` methods. `ResultsViewerControl` exposes per-checkpoint and per-test approve/reject buttons wired to events. GUI consumers connect events to `BaselineManager` calls.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 10.4: Test Result Serialization + History
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `TestResultSerializer` saves/loads `TestResult` as JSON with `JsonStringEnumConverter` and custom `TimeSpanConverter`. `ResultsHistory` service scans `results/` directories for `result.json` files, returns sorted by timestamp descending.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 10.5: Tests
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: ApproveCheckpoint_CopiesCandidateToBaseline, RejectCheckpoint_DeletesCandidate, RoundTrip_PreservesAllFields, Scan_FindsSavedResults, Scan_EmptyDirectory_ReturnsEmpty
- **Tests Passed**: 5 new (63 total)
- **Tests Failed**: 0
- **SUPERVISOR_FLAGS**: None

### Phase 10 Gate Verification
- [x] `ResultsViewerControl` shows per-checkpoint baseline/candidate/diff images with stats
- [x] `ImageViewerForm` provides full-resolution viewing with zoom and pan
- [x] Approve/reject buttons per-checkpoint and per-test
- [x] `BaselineManager.ApproveCheckpoint` copies candidate to baseline
- [x] `BaselineManager.RejectCheckpoint` deletes candidate
- [x] `TestResultSerializer` round-trips TestResult to/from JSON
- [x] `ResultsHistory` scans results directories for saved results
- [x] Regression check: all tests pass (63/63 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 11: Test Manager — Create, Edit, Run

### Checkpoint 11.1: Test Definition Editor
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `TestEditorControl` (UserControl) with fields for name, workload, description, setup (file browser, viewport W/H/projection/display, commands list), checkpoints `DataGridView`. `ErrorProvider` validation (name required, workload required, tolerance numeric). Save serializes to JSON. `LoadDefinition` populates form from existing `TestDefinition`.
- **Issues Found**: `Validate()` hides inherited `ContainerControl.Validate()` — TreatWarningsAsErrors.
- **Resolution**: Added `new` keyword.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 11.2: Workload Configuration Editor
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `WorkloadEditorControl` (UserControl) with fields for all `WorkloadConfig` properties. Browse for executable, agent type combo (rhino/wpf/electron/custom), pipe name, startup timeout, window title. Save serializes to JSON. `LoadConfig` populates form from existing config.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 11.3: Test Runner with Live Progress
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `TestRunnerPanel` (UserControl) with status label, stop button, progress bar, and `ListBox` log. `GuiTestLogger : ITestLogger` fires events (`MessageLogged`, `StatusLogged`, `SummaryLogged`) marshalled to UI thread via `Control.BeginInvoke`. Tests run on background thread via `Task.Run`. Stop button cancels via `CancellationTokenSource`.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 11.4: Recording UI
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `RecordingPanel` (UserControl) with workload combo, window title field, start/stop buttons. Wires to `InputRecorder.StartRecording()`/`StopRecording()` from Core. `SaveFileDialog` writes `.input.json`. Uses `ViewportLocator.FindWindowByTitle` to find target.
- **Issues Found**: `InputRecorder` constructor requires `(IntPtr, string, string)`, methods are `StartRecording`/`StopRecording` (not `Start`/`Stop`/`GetRecording`), no `EventCaptured` event.
- **Resolution**: Matched actual API signatures.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 11.5: Tests
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: TestDefinition_SerializeDeserialize_RoundTrips, TestDefinition_MissingName_ThrowsJsonException, TestDefinition_MissingWorkload_ThrowsJsonException, WorkloadConfig_SerializeDeserialize_RoundTrips, GuiTestLogger_Log_FiresMessageLoggedEvent, GuiTestLogger_LogStatus_FiresStatusLoggedEvent
- **Tests Passed**: 6 new (69 total)
- **Tests Failed**: 0
- **Issues Found**: Test project needed `<UseWindowsForms>true</UseWindowsForms>` to reference `System.Windows.Forms.Form`.
- **Resolution**: Added to test csproj.
- **SUPERVISOR_FLAGS**: None

### Phase 11 Gate Verification
- [x] `TestEditorControl` creates/edits test definitions with validation
- [x] `WorkloadEditorControl` creates/edits workload configs with browse for exe
- [x] `TestRunnerPanel` runs tests with live progress, stop button, log display
- [x] `GuiTestLogger` fires events marshalled to UI thread
- [x] `RecordingPanel` wires to `InputRecorder` with start/stop and save dialog
- [x] Regression check: all tests pass (69/69 total)
- [x] `dotnet build` — 0 errors, 0 warnings

## Phase 12: Polish + Integration

### Checkpoint 12.1: Keyboard Shortcuts
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `MainForm.KeyPreview = true` with `KeyDown` handler. Ctrl+O (open folder), Ctrl+R / F5 (run tests), Ctrl+Shift+R (record), Ctrl+A (approve), Delete (delete test with confirmation dialog), Escape (close modals via ImageViewerForm).
- **SUPERVISOR_FLAGS**: None

### Checkpoint 12.2: Drag-and-Drop
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: `TreeView.AllowDrop = true`. `DragEnter` accepts `DataFormats.FileDrop`. `DragDrop` handles `.json` (import test) and `.3dm` (create test with model) files. Status bar shows feedback.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 12.3: Context Menus
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Right-click workload: Run All Tests, Edit Config, Open in Explorer. Right-click test: Run, Edit, Approve, Delete, Open in Explorer. `ContextMenuStrip` dynamically shown by node type via `NodeMouseClick`. `Process.Start(UseShellExecute: true)` for Open in Explorer.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 12.4: Update Spec Documents
- **Date**: 2026-04-04
- **Status**: PASS
- **Details**: Updated `CLAUDE.md` with new projects (Core, UI), new spec files (PHASES_UI.md, TESTS_UI.md), corrected framework targets, current phase set to 12. `spec/PHASES_UI.md` and `spec/TESTS_UI.md` already written in Phase 8.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 12.5: Final Regression
- **Date**: 2026-04-04
- **Status**: PASS
- **Tests Run**: Cli_Help_StillWorks_AfterCoreExtraction, TestRunner_UsesCore_WithITestLogger, MainForm_CanBeConstructed
- **Tests Passed**: 3 new (72 total)
- **Tests Failed**: 0
- **Details**: CLI still functional after Core extraction (--help prints all 4 commands). TestRunner accepts ITestLogger from Core. MainForm constructs without errors, title contains "Canary", min size >= 1024x768.
- **SUPERVISOR_FLAGS**: None

### Phase 12 Gate Verification
- [x] Keyboard shortcuts: Ctrl+O, Ctrl+R, F5, Ctrl+Shift+R, Ctrl+A, Delete, Escape
- [x] Drag-and-drop: .json and .3dm files accepted on tree view
- [x] Context menus: workload (Run/Edit/Open) and test (Run/Edit/Approve/Delete/Open)
- [x] All toolbar buttons wired to actions
- [x] Spec documents updated (CLAUDE.md, PHASES_UI.md, TESTS_UI.md)
- [x] CLI still works after Core extraction
- [x] GUI MainForm launches without errors
- [x] Full regression: 72 tests pass, 0 warnings
- [x] `dotnet build` — 0 errors, 0 warnings

---

## Post-Phase 12: Penumbra Bug Fix Verification (2026-04-25)

### Canary Run — 4 Suites After 9 Bug Fixes
- **Date**: 2026-04-25
- **Requested by**: Claude Code
- **Command**: `canary run --workload penumbra --suite <effects|materials|display-modes|overlays>`
- **Status**: PASS (all diffs intentional)
- **Results**:
  - `effects`: 6 pass, 2 fail (fresnel + contours — intentional shader changes)
  - `materials`: 2 pass, 3 fail (wood/zebra/damascus — intentional noise + stripe fixes)
  - `display-modes`: 8 NEW (no baselines yet)
  - `overlays`: 7 NEW (no baselines yet)
- **Notes**: 0 unexpected failures, 0 crashes. 15 NEW tests awaiting `canary approve`.

---

## Phase 13: CPig Regression Workload

### Checkpoint 13.1: New agent actions
- **Date**: 2026-04-26
- **Status**: PASS
- **Details**: Added `GrasshopperSetToggle`, `GrasshopperSetPanelText`, `GrasshopperGetPanelText` to `RhinoAgent.cs`. Each follows the existing `HandleGrasshopperSetSlider` pattern: case-insensitive nickname lookup, mutate, `ExpireSolution(true)`, marshal via `InvokeOnUi`. `GetPanelText` prefers VolatileData over UserText.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 13.2: Test runner extensions
- **Date**: 2026-04-26
- **Status**: PASS
- **Details**: Extended `TestDefinition.cs` with `TestAction` and `TestAssert` classes. `TestRunner.RunTestAsync` executes `actions[]` sequentially before checkpoint capture. `asserts[]` evaluated after each checkpoint via `EvaluateClientAssertAsync` (named pipe path) and `EvaluateAssertAsync` (in-process path). Three assert types implemented: `PanelEquals` (exact trimmed match), `PanelContains` (substring), `PanelDoesNotContain` (inverse substring). Unknown types fail with typo-hint message.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 13.3: Loader fixture
- **Date**: 2026-04-26
- **Status**: PASS
- **Details**: Built `workloads/rhino/fixtures/cpig_slop_loader.gh` (21KB) with Slop component, `JsonPath` panel, `Build` toggle, Crash Guard, Log Hub, three output panels (`SlopLog`, `SlopSuccess`, `SlopCount`). Generator template saved alongside as `cpig_slop_loader_generator.json`. Document-level viewport set to deterministic projection + display mode.
- **SUPERVISOR_FLAGS**: None

### Checkpoint 13.4: Bulk-generate test JSONs
- **Date**: 2026-04-26
- **Status**: PASS
- **Details**: `scripts/cpig-test-from-slop.ps1` implemented — reads Slop JSON paths from `CPig/research/slop_tests/`, emits matching `cpig-NN-slug.json` under `workloads/rhino/tests/`. Script is idempotent. All 17 test JSONs generated and committed: `cpig-00-smoke-ping` through `cpig-16-field-evaluate`. Each test definition includes 3 actions (SetPanelText → SetToggle → WaitForSolution) and 3 asserts (SlopSuccess=True, SlopLog !contains FATAL, SlopLog !contains CRASH).
- **SUPERVISOR_FLAGS**: None

### Checkpoint 13.5: Initial baselines + Field Modifier tests
- **Date**: 2026-04-27
- **Status**: PASS (all 22 tests run, 22 NEW — first-run baseline capture)
- **Details**: 5 new test definitions added for CPig's Field Modifiers sprint (cpig-19 through cpig-23). Suite expanded from 17 to 22 tests. All tests run via `canary run --workload rhino --suite cpig` in shared Rhino instance. All 22 tests report SlopSuccess=True, no FATAL/CRASH in logs.
- **Notes**: Baselines captured but not yet approved. cpig-10 and cpig-13 remain excluded from suite (BUG-004 scope — libfive JIT batch eval crash).
- **SUPERVISOR_FLAGS**: None

## Summary

| Phase | Description | Tests Added | Cumulative |
|-------|-------------|-------------|------------|
| 0 | Solution Scaffold + CLI Shell | 3 | 3 |
| 1 | Named Pipe IPC + Agent Protocol | 11 | 14 |
| 2 | Input Recorder + Replayer | 10 | 24 |
| 3 | Screenshot Comparison Engine | 16 | 40 |
| 4 | Test Runner + Orchestrator | 9 | 49 |
| 5 | HTML Report + Polish | 3 | 52 |
| 6 | Rhino Workload Agent | 0 | 52 |
| 7 | Future Workloads (Stub) | 0 | 52 |
| 8 | .rhp Fix + Canary.Core Extraction | 3 | 55 |
| 9 | WinForms Application Shell | 3 | 58 |
| 10 | Results Viewer + Baseline Management | 5 | 63 |
| 11 | Test Manager — Create, Edit, Run | 6 | 69 |
| 12 | Polish + Integration | 3 | 72 |
| 13 | CPig Regression Workload (13.1–13.5) | 0 (infra) | 72 + 22 test defs |

---

## 2026-04-29 — Test mode duality (Phase 8.6)

Promoted comparison mode to a runtime selector — the user picks pixel-diff (visual regression — code stability) or VLM (semantic correctness) per `canary run` invocation, without rewriting test JSONs.

- `--mode <pixel-diff|vlm|both>` flag added to `canary run` (`src/Canary.Harness/Cli/RunCommand.cs`, default `pixel-diff`).
- `ModeOverride` enum + `CheckpointMode` enum added to `Canary.Orchestration` (`src/Canary.Core/Orchestration/TestRunner.cs`).
- `TestRunner.ModeOverride` property; `RunCommand` propagates the parsed flag.
- Optional `setup.vlmDescription` field added to `TestSetup` (`src/Canary.Core/Config/TestDefinition.cs`); `ProcessVlmCheckpointAsync` falls back to it when a checkpoint omits its own `description`.
- Refactor: `ProcessCheckpointAsync` + `ProcessAgentCheckpointAsync` take an optional `forceMode: CheckpointMode?` parameter; centralized loop in `DispatchClientCheckpointAsync` / `DispatchAgentCheckpointAsync` replaces 4 inlined call sites. `--mode both` runs each checkpoint twice and emits two `CheckpointResult` rows (the VLM one suffixed with `-vlm`).
- Mode resolution: per-checkpoint `mode == "vlm"` always wins; otherwise `--mode` flag applies; otherwise pixel-diff.
- Doc updates: `docs/features/vlm-oracle.md` rewritten with duality framing + writing-good-descriptions section; `spec/PHASES.md` Phase 8.6 entry; `CLAUDE.md` Quick Reference. MultiVerse `CLAUDE.md` gains the canonical "Testing modes" section; child repos (CPig, Slop, Pigture) carry back-references.
- Build: `dotnet build Canary.sln` → 0/0.

Cross-repo coupling: CPig regenerates 19 retopo Slop+Canary test pairs that all emit `setup.vlm` + `setup.vlmDescription`; CPig adds 2 new accessor components (`CrossFieldExplode` BB019, `PatchLayoutExplode` BB01A) so VLM mode has visually distinct viewport content per stage.

---

## 2026-04-29 — Auto Log-Tap on every cpig-component output (cross-repo)

No harness change — but Canary's Slop-loader pattern now consumes test JSONs that automatically wrap every cpig-component Goo output with a Slop `Log Tap`. Per-stage taps land in `LogHub` files alongside the viewport screenshot, so when a Canary run fails, the data-flow log is the first place to look (it shows where an upstream stage went empty / wrong before the screenshot was captured).

- 19 retopo Canary test JSONs (cpig-34..52) regenerated with auto-inserted Log Tap nodes (mirrors the regenerated CPig Slop test JSONs).
- `CLAUDE.md` gains a "Logging — Slop test JSONs auto-tap every component output" section with a back-reference to MultiVerse/CLAUDE.md's canonical "Logging in Slop tests" guide.
- No behavioural change to test runs (Log Tap is a pass-through). Tap output flows into Slop's LogHub file — read it after a failing run to localize which stage went empty.

---

## 2026-05-24 — Qualia workload promoted Stub → Active (cross-repo reconcile R2)

Per `MultiVerse/audit/2026-05-24-testing-canary-audit-and-plan.md`. The
Qualia workload has been actively developed since 2026-05-08 (78 test
fixtures, 6 suites, `Canary.Agent.Qualia` CDP bridge, ~50+
`__canary*` hooks, RH-2 multi-display sweep, Wave 0.B Playground,
qualia-v4-ui), but the doc surface still reported it as a stub. This
entry catches the docs up — no code change to the harness or agent.

- `spec/QUALIA_WORKLOAD.md` — new workload specification promoted from
  `workloads/qualia/AGENT_NOTES.md`. Mirrors the structure of
  `PENUMBRA_WORKLOAD.md` / `CPIG_WORKLOAD.md` / `PIGTURE_WORKLOAD.md`.
  Documents the full hook surface (readiness / persona / landing /
  playground / qualia-v4 / RH-2 / diagnostic), the agent action
  mapping, mode selection, baseline conventions, suite roster, and
  open questions (runMode shared, diag-* test family suiting, queued
  eager-L3 + dev-test checkpoint type).
- `spec/PEERS.md` — new Qualia section between Penumbra and Pigture.
  Documents the bridge agent, workload files, Qualia-side contract
  files, hook stability rules, port co-existence with Penumbra, the
  hook-addition workflow.
- `CHANGELOG.md` — `[Unreleased]` gains a `### Added — Qualia workload
  (backfill May 2026)` block covering 2026-05-08 initial buildout
  through 2026-05-24 spec promotion. Catches up entries the prior
  monthly cadence missed.
- `README.md` — Workload table Qualia row changed from
  `Custom viewer | Built-in module | Stub` to
  `Browser (Vite + Chrome via CDP) | Canary.Agent.Qualia | Active —
  6 suites / 78 tests`.

**Why this matters.** The 2026-05-24 Qualia Phase 1 eager-L3 puppeteer
smoke surfaced a pre-existing bug (Qualia Bug 0043 —
`BehaviorExtractor` FsAdapter stale-capture) that ran end-to-end
duplicate of `Canary.Agent.Qualia`'s CDP capability. Operator-led
discovery of the existing Canary surface was blocked because the docs
said it didn't exist. This entry plus R1 (MultiVerse registers Qualia
as a child) plus R3 (Qualia CLAUDE.md surfaces Canary) closes that
gap. Subsequent moves (M1 — convert puppeteer smoke into a Canary
suite; M2 — dev-test harness as Canary checkpoint type; D — auto-
instrumentation skill) are sequenced in the audit doc.

No code touched. No baselines moved. Doc-only commit per R2 of the
reconcile.

---

## 2026-05-24 — Debug-overhaul audit Phase A

Phase A of the audit prompt `MultiVerse/prompts/canary-debug-overhaul-audit-2026-05-24.md`. Doc-only; no source changes.

- **Output:** `docs/research/2026-05-24-canary-surface-audit.md` covering A1 (UI WinForms surface — 13 controls / services), A2 (CLI — `run` / `record` / `approve` / `report` + every flag), A3 (telemetry per workload — Rhino / Penumbra / Qualia + unified gap), A4 (report artifacts + result.json shape + side channels), A5 (non-headless / UI-first state — `Canary.UI.exe` is never launched by CLI), A6 (localhost-relevant infra — two `ViteManager` copies with `netstat -ano` + `taskkill /F /T`), A7 (screenshot + diff infra — pixel-diff, SSIM, composite strips, no annotation surface).
- **Headline findings:** (1) Console + Network CDP domains are never enabled — zero JS console capture, zero network capture. (2) `--mode` CLI flag has no UI picker; GUI runs ignore `ModeOverride`. (3) Result files overwrite — no run history beyond most-recent. (4) Vite/Chrome processes bypass `ProcessManager`; can orphan on bridge-agent crash. (5) `RunCommand.RunAsync` returns void → CLI always exits 0 (regression from `spec/PHASES.md` Phase 4 spec).
- **Status:** Phase A complete; Phase B (prior-art survey) next.

## 2026-05-24 — Debug-overhaul audit Phase B

Phase B of the audit prompt. Doc-only; no source changes.

- **Output:** `docs/research/2026-05-24-test-harness-prior-art.md` covering Playwright Inspector + Trace Viewer, Cypress App + Cloud Dashboard, and Sysinternals Process Explorer. ~150 words per tool with steal / skip lists. Cross-tool synthesis table maps each borrowed convention to the §C design section that will use it.
- **Third-tool selection:** Sysinternals Process Explorer. Playwright + Cypress cover the test-runner-UI angle; Process Explorer covers the localhost-manager + process-tree provenance angle (§C7 Tier 1 + Tier 2) that neither Playwright nor Cypress addresses.
- **No live web fetches required** — all conventions cited are stable canonical surfaces (Playwright Trace Viewer file format, Cypress `open` vs `run` verb model, Process Explorer kill-tree default).
- **Status:** Phase B complete; Phase C (design proposals) next.

## 2026-05-24 — Debug-overhaul audit Phase C

Phase C of the audit prompt. Doc-only; no source changes.

- **Output:** `docs/plans/2026-05-24-canary-debug-overhaul.md` covering nine design sections (C1 universal telemetry envelope, C2 Claude-readable REPORT.md, C3 non-headless enforcement, C4 UI overhaul, C5 sketch+annotate, C6 feedback channel, C7 tiered localhost manager, C8 live+past-runs, C9 VLM/visual-regression demotion) plus Implementation Plan appendix.
- **Locked design decisions** from prompt §0.1 honored: file-inbox-canonical + MCP-wrapped feedback; full sketch+annotate (rects + freehand + text); tiered localhost manager (T1+T2+T3 all in v1).
- **Headline design picks:** WinForms additive + WPF islands via WindowsFormsHost (NOT a WPF reshell); MCP server as separate csproj; voluntary spawn registry (recommended over OS hook); UI nav re-org via INavMode interface adding Past Runs / Localhost / Feedback / Telemetry / Settings tabs.
- **First entry under new `docs/plans/`** directory in Canary (pattern borrowed from Qualia).
- **Total effort estimate:** ~9.5–11.5 weeks across 9 phases. v1 cut recommendation: Phases 1–4 (~4.5 weeks, ~70% operator-visible value).
- **Status:** Phase C complete; Phase D (hand-off) next.

## 2026-05-24 — Debug-overhaul implementation: Phase 0 + Precursor (bug 0007 CLI exit code)

Phase 0 pre-flight + precursor fix for the debug-overhaul implementation prompt `MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md`.

- **Phase 0 (pre-flight):** snapshot tag `pre-impl-debug-overhaul-2026-05-24` created at HEAD `4993c53`. Baseline: 107 Unit tests / 0 Integration tests / 0 warnings / 0 errors. Canon read: design doc, surface audit, prior-art, CLAUDE.md, SUPERVISOR.md, STANDARD.md §§ 7/14/16/19/22. Progress log: `docs/progress/2026-05-24-canary-debug-overhaul.md`.
- **Precursor (bug 0007):** `RunCommand.RunAsync` refactored to return `Task<int>`. Helper `ExitCodeFromSuiteResult` maps SuiteResult to exit code (0 = no failures, 1 = any failed or crashed; `New` baselines count as pass). Every early-error path inside `RunAsync` now returns `1`. Handler closure sets `ctx.ExitCode`. Bug doc: `docs/bugs/0007-cli-exit-code-regression.md`. 8 new unit tests in `tests/Canary.Tests/Cli/RunCommandExitCodeTests.cs`. Tests now: 115 Unit (was 107), all green; 0/0 warnings/errors.

## 2026-05-24 — Debug-overhaul Phase 1 (C3 non-headless enforcement)

First design-phase commit of the debug-overhaul. Implements `STANDARD.md` §16 locked rule 8: CLI launches UI by default.

- **Added flag:** `canary run --headless` opts out of UI launch (CI / scripted use). `--quiet` implies `--headless`. Help text mentions both behaviors.
- **UI handoff:** when not `--headless` and `UiLocator.TryFindUiExe` succeeds, `RunCommand.TryLaunchUi` spawns `Canary.UI.exe` with the auto-run args via `Process.Start(UseShellExecute=true)` and CLI exits 0. Search order: same directory as canary.exe → sibling `Canary.UI/bin/{Release|Debug}/net8.0-windows/Canary.UI.exe` (walks up to find the `src/` parent in dev tree) → `Canary UI.lnk` shortcut (resolved via WScript.Shell COM).
- **Single-instance UI:** `Program.cs` acquires `Global\Canary.UI.SingleInstance` mutex. Second invocation sends its AutoRunArgs JSON through `canary-ui-singleinstance-pipe` and exits; first instance receives the payload via `SingleInstancePipeServer.AutoRunRequested` (raised on the pipe-loop Task), marshals to the UI thread via `form.BeginInvoke`, and calls `MainForm.AutoRunAsync(args)`.
- **AutoRun:** `MainForm.AutoRunAsync` waits up to 10s for the workloads tree to populate (the constructor fires `AutoDetectWorkloadsDir` async), then selects the matching workload/test/suite node and triggers `OnRunTests`. The mode override (`--mode pixel-diff|vlm|both`) is stashed in `_autoRunModeOverride` and consumed when `OnRunTests` creates the `TestRunnerPanel`; the panel passes it to `TestRunner.ModeOverride`. `TestRunnerPanel.RunAsync` gained a `modeOverride` parameter (default PixelDiff). The corresponding UI mode picker arrives in Phase 7.
- **Files added:** `src/Canary.Core/Cli/AutoRunArgs.cs`, `src/Canary.Harness/UiLocator.cs`, `src/Canary.UI/SingleInstancePipeServer.cs` (also defines `SingleInstancePipeClient`).
- **Files modified:** `src/Canary.Harness/Cli/RunCommand.cs` (--headless option, TryLaunchUi), `src/Canary.UI/Program.cs` (mutex + pipe server + initial auto-run), `src/Canary.UI/MainForm.cs` (AutoRunAsync + FindAutoRunNode + ParseAutoRunMode + `_autoRunModeOverride` field, `using Canary.Cli`), `src/Canary.UI/Controls/TestRunnerPanel.cs` (modeOverride parameter + ModeOverride passthrough), `tests/Canary.Tests.Integration/Canary.Tests.Integration.csproj` (added Canary.UI project reference).
- **Tests:** 13 new Unit tests (`tests/Canary.Tests/Cli/AutoRunArgsTests.cs` × 10, `tests/Canary.Tests/Cli/HeadlessFlagTests.cs` × 3). 2 new Integration tests (`tests/Canary.Tests.Integration/SingleInstancePipeTests.cs`).
- **Counts:** Unit 128 Passed (was 115); Integration 2 Passed (was 0); build 0/0.
- **Smoke:** `canary run --headless` (with no workload) prints the error and exits 1; `canary run --help` advertises `--headless`. Operator-side non-headless smoke (`canary run --workload qualia --test main-pencil` → UI auto-launches and runs) deferred to operator verification.

## 2026-05-24 — Debug-overhaul Phase 2 (C1 universal telemetry envelope)

L-effort phase per design Implementation Plan. The data-producer side of
debug-overhaul: every workload agent now writes a uniform `TelemetryRecord`
stream to a per-suite NDJSON file. Phase 3 will ingest this into
REPORT.md; Phase 7 will surface it in a Telemetry tab.

- **New namespace `Canary.Telemetry` (`src/Canary.Core/Telemetry/`):**
  - `TelemetryKind` enum (Console / Network / Input / AgentState / AgentAction / Log / Screenshot).
  - `TelemetryRecord` POCO — JSON envelope with `t / runId / testName / checkpointName / kind / level / source / data`; serializer options shared (camelCase, null-omitted, non-indented for NDJSON).
  - `ITelemetrySink` interface + `NullTelemetrySink` (no-op default) + `CompositeTelemetrySink` (fan-out with per-sink try/catch).
  - `NdjsonFileSink` — thread-safe one-record-per-line writer with 500 KB per-line truncation marker on overflow; opens with `FileShare.Read` so tailers (the Phase 7 Telemetry tab) can read while the run is in flight.
  - `EventStreamSink` — in-memory fan-out via `Action<TelemetryRecord>` event (subscriber called on writer thread; consumers marshal).
  - `ITelemetryAware` — implemented by agents that accept a sink (registered before `InitializeAsync`).
- **CDP extension (`Canary.Cdp.CdpClient`):** `Subscribe(method, Action<JsonNode>)` returning `IDisposable`. ReadLoopAsync now fans event payloads to both the historic `_eventWaiters` (one-shot `TryRemove`) AND the new `_subscribers` (continuous, multi-handler). Subscribers run on the read-loop thread; per-handler try/catch so one bad subscriber does not break the loop.
- **Shared CDP telemetry helper (`Canary.Cdp.CdpTelemetryStream`):** `EnableAndSubscribeAsync(cdp, sink, source, ct)` enables Runtime + Console + Log + Network domains and registers subscribers for `Runtime.consoleAPICalled` / `Log.entryAdded` / `Network.requestWillBeSent` / `Network.responseReceived` / `Network.loadingFailed`. Network records carry a `durationMs` computed from per-request `Stopwatch.GetTimestamp` deltas (cleaned up on response/failure). Console payload shape: `{text, type, sourceUrl, lineNumber, category}`. Network payload shape: `{method, url, status, durationMs, errorText}`.
- **Penumbra agent (`src/Canary.Agent.Penumbra/PenumbraBridgeAgent.cs`):** implements `ITelemetryAware`. Both `InitializeAsync` (fresh launch) and `InitializeFromExistingAsync` (attach) call `CdpTelemetryStream.EnableAndSubscribeAsync` after the existing Page+Runtime enables. Subscription handle stored in `_telemetrySubscriptions`, disposed on agent `Dispose`.
- **Qualia agent (`src/Canary.Agent.Qualia/QualiaBridgeAgent.cs`):** same wiring as Penumbra; source discriminator `qualia`.
- **TestRunner integration:** new `TelemetrySink` property (default `NullTelemetrySink.Instance`). `RunCommand.RunAsync` instantiates a per-suite `NdjsonFileSink` at `workloads/<w>/results/[<suite>/]telemetry.ndjson` before kicking off the suite; `RunQualiaSuiteAsync` and `RunPenumbraSuiteAsync` register the sink on the agent (cast to `ITelemetryAware`) before calling `InitializeAsync`. Phase 3 will move the path into `runs/<timestamp>/`.
- **ITestProgressEvents extension:** `OnTelemetry(TelemetryRecord)` default-method (C# 8+ feature) — no-op default so existing implementers (NullTestProgressEvents + ProgressFeedPanel) need no change. Phase 7 Telemetry tab will override.
- **Files added:** `src/Canary.Core/Telemetry/{TelemetryKind,TelemetryRecord,ITelemetrySink,NdjsonFileSink}.cs`, `src/Canary.Core/Cdp/CdpTelemetryStream.cs`, `tests/Canary.Tests/Telemetry/{TelemetryRecordSerializationTests,NdjsonFileSinkTests}.cs`.
- **Files modified:** `src/Canary.Core/Cdp/CdpClient.cs` (Subscribe API + ReadLoop fan-out), `src/Canary.Core/ITestProgressEvents.cs` (OnTelemetry default method + using Canary.Telemetry), `src/Canary.Core/Orchestration/TestRunner.cs` (TelemetrySink property + using Canary.Telemetry), `src/Canary.Agent.Penumbra/PenumbraBridgeAgent.cs` (ITelemetryAware + sink + subs wiring + Dispose), `src/Canary.Agent.Qualia/QualiaBridgeAgent.cs` (same), `src/Canary.Harness/Cli/RunCommand.cs` (NdjsonFileSink instantiation + register-on-agent calls in both suite paths).
- **Verification:** `dotnet build Canary.sln` = 0/0. `dotnet test --filter "Category=Unit"` = 140 Passed (was 128; +12 new). `dotnet test --filter "Category=Integration"` = 2 Passed (unchanged — the live CDP integration tests are deferred to operator-side). Smoke: `canary run --headless --workload nonexistent` exits 1 (no regression from precursor).
- **Deferred to follow-up (documented per impl §4):**
  - **Rhino-side console interception** — RhinoCommon 8 does not expose a clean `RhinoApp.WriteLine` event or `TextWriter` swap hook in scope; deferred to a v2 follow-up. Penumbra + Qualia coverage ships as planned.
  - **`InputReplayer` event records** — `InputReplayer.InjectMouseMove` etc. have no sink reference; passing one through requires refactoring its construction site (TestRunner constructs it per-test). Cross-cuts Phase 7 UI work; deferred to that phase.
  - **`ProcessManager.Track` agent-action records** — emit point is the right surface, but the records would need a SpawnRegistry consumer (Phase 6 / §C7 Tier 2). Deferred to Phase 6.
  - **Live CDP integration tests** — require Chrome + Vite for the workload under test; operator runs them via `canary run --workload {penumbra|qualia} --test <known-test>` and inspects `workloads/<w>/results/telemetry.ndjson`.
- **Snapshot tag `pre-impl-phase2-2026-05-24`** preserved during the work; deleted at commit.

## 2026-05-24 — Debug-overhaul Phase 3 (C2 REPORT.md + per-run dir layout)

M-effort phase per design Implementation Plan. Per-run dir layout for
result.json + REPORT.md, with Markdown report generator + ResultsHistory
dual-shape scan + retention helper.

- **MarkdownReportGenerator (`src/Canary.Core/Reporting/MarkdownReportGenerator.cs`):** generates `REPORT.md` per the §C2 spec template — header (run id + workload + agent type + mode + duration + started/finished UTC) → Verdict summary → Checkpoints table → Errors (conditional) → VLM evaluations (conditional) → Files. Cross-link convention: `[baseline](../baselines/<name>.png)` etc. (REPORT.md at `<test>/runs/<timestamp>/REPORT.md`; images at `<test>/<dir>/`). Telemetry footer link points at `../../../telemetry.ndjson` (per-suite location from Phase 2).
- **Per-run dir wiring in `TestRunner`:** new helper `SavePerRunArtifactsAsync(result, workload, testDir, startedUtc, finishedUtc)` creates `<testDir>/runs/<yyyyMMdd-HHmmss-xxxx>/` and writes `result.json` + `REPORT.md` into it. Called at the end of both `RunTestAsync` (HarnessClient path — Rhino/Pigture workloads) and `RunAgentTestAsync` (CDP bridge path — Penumbra/Qualia workloads). Failures swallowed + logged so a broken report write does not flip the test verdict. Run-id timestamp includes 4 hex chars to avoid same-second collisions in suite mode.
- **Baselines + candidates + diffs + composite stay flat under `<test>/`** for Phase 3 (overwriting per run, same as today). MarkdownReportGenerator links account for this via `../<dir>/`. Future phase can move them under per-run dirs if past-runs image preservation becomes required; Phase 3 ships the mental model + REPORT.md without the deep refactor.
- **`ResultsHistory.ScanAsync` dual-shape:** single recursive `result.json` walk now picks up both `<test>/result.json` (legacy flat) AND `<test>/runs/<timestamp>/result.json` (new per-run). Both kinds surface as entries; downstream UI sorts and picks. Legacy stays read-only history.
- **`ResultRetention.PurgeOlderThan` (new `Canary.Maintenance` namespace):** walks every `results/.../runs/<timestamp>/` and deletes those older than the threshold. Default 14 days (matches `STANDARD.md` §16 candidates/diffs convention). Legacy flat-layout artifacts intentionally untouched. Returns PurgeReport with dirs scanned + dirs purged + bytes freed + per-dir error list. NOT wired into TestRunner this phase — Phase 4 or later can invoke at run start; for now it is an exposed helper for ops scripts.
- **`TestRunnerPanel.RunAsync` cleanup:** removed the per-test `result.json` save loop — `TestRunner` now owns it (CLI parity goal). Inline comment points to Phase 3 / §C2.
- **Files added:** `src/Canary.Core/Reporting/MarkdownReportGenerator.cs`, `src/Canary.Core/Maintenance/ResultRetention.cs`, `tests/Canary.Tests/Reporting/MarkdownReportGeneratorTests.cs`, `tests/Canary.Tests/UI/ResultsHistoryDualShapeTests.cs`, `tests/Canary.Tests/Maintenance/ResultRetentionTests.cs`.
- **Files modified:** `src/Canary.Core/Orchestration/TestRunner.cs` (using Canary.Reporting, SavePerRunArtifactsAsync helper, GenerateRunId, calls in both run paths), `src/Canary.UI/Services/ResultsHistory.cs` (dual-shape doc comment; existing recursive walk already picks up both layouts so the behavior change is implicit + documented), `src/Canary.UI/Controls/TestRunnerPanel.cs` (remove per-test save loop, comment-out pointing to Phase 3).
- **Verification:** `dotnet build Canary.sln` = 0/0. `dotnet test --filter "Category=Unit"` = 155 Passed (was 140; +15 new). `dotnet test --filter "Category=Integration"` = 2 Passed (unchanged). CLI smoke unchanged.
- **Deferred (per Phase 3 scope discipline):**
  - **Moving candidates/diffs/composite under runs/<timestamp>/** — kept flat for Phase 3; substantial refactor across ProcessCheckpointAsync + BuildCompositeAsync + every candidate/diff path. PastRuns image history can land in a follow-up.
  - **Per-test telemetry slicing** — telemetry.ndjson stays at the per-suite level (Phase 2 location). REPORT.md links via `../../../telemetry.ndjson` (relative). Per-test slicing in shared-suite mode is ambiguous (no crisp boundaries on console message arrival); revisit if needed.
  - **Wiring `ResultRetention.PurgeOlderThan` into TestRunner startup** — helper is available; auto-invocation deferred until operator decides cadence (run-start, daily, manual?).
- **Snapshot tag `pre-impl-phase3-2026-05-24`** preserved during the work; deleted at commit.

## 2026-05-24 — Debug-overhaul Phase 4 (C7 Tier 1 localhost manager)

M-effort phase. Tier 1 of design §C7 — passive port enumeration via
netstat + ProcessManager-equivalent enrichment. Deduplicates the
ViteManager.KillStaleListenerAsync helpers across Penumbra + Qualia.
Interim UI surface ships as a toolbar-launched popup form; Phase 7
migrates to a proper Localhost nav tab.

- **New namespace `Canary.Localhost` (`src/Canary.Core/Localhost/`):**
  - `PortEntry` — sealed record holding Port + Pid + ProcessName +
    CommandLine + WorkingDirectory + StartTime + Provenance.
  - `PortProvenance` — enum (Unknown / DevServerHeuristic / CanarySpawn /
    CanaryHarness). Phase 4 populates Unknown + CanaryHarness; Phase 6
    fills CanarySpawn via SpawnRegistry; Phase 8 fills DevServerHeuristic.
  - `LocalhostManager` — `EnumeratePorts(filter)` / `EnumeratePortsAsync`
    (synchronous netstat + IPGlobalProperties for Canary self-listeners)
    + `KillByPortAsync(port, ct)` (the dedup target for ViteManager).
    Internal `ParseNetstat(string)` exposed for unit-testing through
    InternalsVisibleTo. `DefaultPorts` array per §0.3.
- **ViteManager dedupe:** both Penumbra + Qualia `ViteManager.KillStaleListenerAsync`
  collapsed to a one-liner delegate to `LocalhostManager.KillByPortAsync`.
  ~100 lines of duplicated netstat-parse + taskkill code removed per
  workload. `FindListenerPid` deleted in both.
- **Interim UI surface (`src/Canary.UI/Controls/LocalhostPanel.cs`):**
  UserControl with toolbar (Refresh + Kill selected), 6-column ListView,
  StatusStrip footer. Polling timer at 2s when visible; flips to 30s via
  the `SetSlowPolling()` API the host can call on form-deactivate. Kill
  confirmation modal is louder for `Provenance == CanaryHarness` rows.
  Auto-refresh on selection + after each kill.
- **MainForm wiring:** new "Localhost" toolbar button opens the panel in
  a 1100×500 popup form (`OnShowLocalhost`). Per impl §6 + design
  Implementation Plan deviation #2 — interim placement; Phase 7 nav-tab
  refactor migrates.
- **Files added:** `src/Canary.Core/Localhost/{PortEntry,LocalhostManager}.cs`,
  `src/Canary.UI/Controls/LocalhostPanel.cs`,
  `tests/Canary.Tests/Localhost/LocalhostManagerTests.cs`.
- **Files modified:** `src/Canary.Agent.Penumbra/ViteManager.cs` (dedupe,
  removed FindListenerPid + inline kill body), `src/Canary.Agent.Qualia/ViteManager.cs`
  (same), `src/Canary.UI/MainForm.cs` (Localhost toolbar button +
  OnShowLocalhost popup handler).
- **Verification:** `dotnet build Canary.sln` = 0/0. `dotnet test --filter
  "Category=Unit"` = 164 Passed (was 155; +9 new). `dotnet test --filter
  "Category=Integration"` = 2 Passed (unchanged). The `EnumeratePorts`
  real-machine smoke test runs as a unit test — it shells out to netstat
  for real but asserts only shape, not specific ports.
- **Deferred to subsequent phases:**
  - Tier 2 (SpawnRegistry — Canary-spawn provenance) → Phase 6 with the
    MCP server.
  - Tier 3 (name-heuristic listing) → Phase 8.
  - WMI command-line enrichment — current LocalhostManager uses
    `Process.MainModule?.FileName` which captures the exe path but not
    the full command line. WMI Win32_Process is slower (cache needed) and
    not blocking the Tier 1 deliverable; can land in Phase 8 polish.
  - Restart action on CanarySpawn rows (no provenance available until
    Tier 2).
- **Snapshot tag `pre-impl-phase4-2026-05-24`** preserved during the work;
  deleted at commit.

## 2026-05-24 — Debug-overhaul Phase 5 (C5 sketch UI + C6 file-inbox half)

M-L effort phase. WPF island for annotation surface (per operator
decision Q4) + file-based feedback inbox (canonical layer of design
§C6; MCP server wrapper ships in Phase 6).

- **Snapshot tag:** `pre-impl-phase5-2026-05-24` created; deleted on success.
- **Canary.UI WPF wiring:** `<UseWPF>true</UseWPF>` added to
  Canary.UI.csproj alongside the existing `<UseWindowsForms>true</UseWindowsForms>`.
  The .NET SDK quirk: adding UseWPF drops System.IO from the default
  implicit usings; restored via explicit `<Using Include="System.IO" />`
  so the existing files keep working without per-file changes.
- **AnnotationCanvas (WPF UserControl):** custom Canvas with Pointer /
  Rectangle / Freehand / Text tool modes; red/yellow/green color picker;
  source-image background at native pixel size. Mouse handlers translate
  to WPF Rectangle / Polyline / TextBlock+backing-Rectangle shapes.
  RenderAnnotatedPng forces a layout pass at source dims then renders via
  RenderTargetBitmap + PngBitmapEncoder. SerializeAnnotationsJson emits
  the §C5 schema (rect = {x,y,w,h,stroke,strokeWidth}; freehand =
  {points[], stroke, strokeWidth}; text = {x,y,text,color,fontSize}).
  Type aliases scoped to file disambiguate WPF vs WinForms collisions
  (UserControl, Image, Brush, Brushes, Point, Size, Color, MouseEventArgs,
  Rectangle, FontFamily, HorizontalAlignment, VerticalAlignment).
- **AnnotatedImageForm (WinForm + ElementHost):** dark-themed shell with
  ToolStrip (tool buttons + color picker + Clear), TextBox for title +
  multiline body, ElementHost embedding AnnotationCanvas, FlowLayoutPanel
  with Save + Cancel + status label. Save reads the source PNG bytes,
  renders annotated PNG bytes, serializes JSON, generates a slug via
  FeedbackSlugGenerator, and atomic-writes the triad via
  FeedbackInboxWriter. WpfBrush/WpfBrushes/WpfColor/WpfSolidColorBrush
  aliases for the few WPF brush usages in the WinForms scope.
- **Canary.Feedback namespace (`src/Canary.Core/Feedback/`):**
  - `FeedbackItem` — POCO with Slug + Date + Status + Project + RunRef +
    CheckpointRef + ImageRef + Urgency + Tags + Title + Body.
  - `FeedbackSlugGenerator.Generate(date, title, existingSlugs)` —
    produces `YYYY-MM-DD-NNN-<slugified-title>`. NNN auto-increments past
    the highest existing NNN for the same date; title slugifies to
    lowercase hyphenated alpha-numeric, capped at 5 words.
  - `FeedbackInboxWriter` — atomic per-file writes (write to .tmp +
    rename) for the markdown body + sidecar dir with source.png +
    annotated.png + annotations.json. ExistingSlugs() enumerates the .md
    filenames for slug-collision avoidance.
- **docs/feedback/ tree:** inbox/ + triaged/ + resolved/ created with
  .gitkeep markers + README.md documenting the layout, slug format,
  lifecycle (open → triaged → resolved), and item shape.
- **CLAUDE.md update:** new "Feedback inbox" section + entry in the
  Documentation Structure tree.
- **ImageViewerForm Annotate button:** interim launch surface per
  impl §7. Opens current image in AnnotatedImageForm with inbox root
  discovered by walking up from AppContext.BaseDirectory.
- **Files added:** `src/Canary.Core/Feedback/{FeedbackItem,FeedbackSlugGenerator,FeedbackInboxWriter}.cs`,
  `src/Canary.UI/Annotation/{AnnotationCanvas,AnnotatedImageForm}.cs`,
  `docs/feedback/{README.md, inbox/.gitkeep, triaged/.gitkeep, resolved/.gitkeep}`,
  `tests/Canary.Tests/Feedback/{FeedbackSlugGeneratorTests,FeedbackInboxWriterTests}.cs`.
- **Files modified:** `src/Canary.UI/Canary.UI.csproj` (UseWPF + System.IO
  using), `src/Canary.UI/Controls/ImageViewerForm.cs` (Annotate button +
  OpenAnnotate + FindInboxRoot helpers), `CLAUDE.md` (Feedback inbox
  section).
- **Verification:** `dotnet build Canary.sln` = 0/0. `dotnet test --filter
  "Category=Unit"` = 176 Passed (was 164; +12 new). `dotnet test --filter
  "Category=Integration"` = 2 Passed (unchanged). CLI smoke unchanged.
  WPF island visual verification deferred to operator (requires UI launch
  + manual annotate flow).
- **Deferred / not in scope:**
  - AnnotationOverlayRenderingTests (WPF RenderTargetBitmap test) — needs
    a headless WPF render harness or a STA test thread; the
    end-to-end behavior is exercised by the operator's first annotate
    save. Schedule a follow-up unit test if a STA-friendly fixture
    materializes.
  - InkCanvas alternative — explicitly chose custom WPF Canvas per §C5
    open question; ships as such.
- **Snapshot tag `pre-impl-phase5-2026-05-24`** preserved during the work;
  deleted at commit.

## 2026-05-24 — Debug-overhaul Phase 6 (C6 MCP server + C7 Tier 2 spawn registry)

M-effort phase. New csproj exposing 8 MCP tools over stdio JSON-RPC +
spawn registry so the Localhost panel / MCP server / future PastRuns
panel can attribute Canary-spawned processes.

- **Snapshot tag:** `pre-impl-phase6-2026-05-24` created; deleted on success.
- **New csproj (`src/Canary.McpServer/`):** net8.0-windows (matches
  Canary.Core), OutputType Exe, single `ProjectReference Canary.Core`.
  No external NuGet dependencies — protocol handler rolled in-house for
  visibility + zero version churn. Added to Canary.sln.
- **MCP protocol handler (`McpProtocol.cs`):** ~120-line stdio JSON-RPC
  2.0 loop supporting `initialize`, `tools/list`, `tools/call`,
  `notifications/*`. Internal `McpTool` abstract base with `Name`,
  `Description`, `InputSchemaJson`, `InvokeAsync`. Per-tool exceptions
  wrap into JSON-RPC content with `isError: true` so Claude sees the
  error message instead of a transport failure.
- **8 tool implementations (`src/Canary.McpServer/Tools/`):**
  - `FeedbackTools.cs` — `list_feedback`, `get_feedback`,
    `mark_feedback_triaged`. Walks up from `AppContext.BaseDirectory`
    to discover `docs/feedback/`. Frontmatter parsed inline (title +
    project + urgency for the list view).
  - `RunsTools.cs` — `list_recent_runs`, `get_run_report`. Walks
    `workloads/*/results/**/REPORT.md` from Phase 3. Parses verdict
    from the first markdown header line. Optional workload + verdict
    + limit filters.
  - `LocalhostTools.cs` — `list_localhost_ports`, `list_running_apps`,
    `kill_localhost_port`. Thin wrappers over LocalhostManager +
    SpawnRegistry.
- **SpawnRegistry (`src/Canary.Core/Telemetry/SpawnRegistry.cs`):**
  Voluntary per operator decision Q3. Per-process JSON file at
  `%LocalAppData%\Canary\claude-spawns\<sessionId>.json`. Static
  `Default` singleton lazily creates a session file on first access
  (sessionId = `canary-<pid>-<utcStamp>`). Public API: `Register`,
  `Unregister`, `Snapshot`, `LoadAllSessions` (cross-session merge),
  `PurgeOldSessions`. Atomic flush per change (write-to-.tmp + move).
- **Tier 2 union in LocalhostManager:** Pass-1 netstat enrichment now
  consults `SpawnRegistry.LoadAllSessions()` as a PID-indexed dict.
  Matching PIDs get `Provenance = CanarySpawn` + the intent string
  promoted into the CommandLine field (e.g. "Qualia Vite dev server
  (port 5173, projectDir=C:\Repos\Qualia)").
- **Producer wiring:**
  - `Penumbra.ViteManager` — `Register(pid, "node.exe", ..., port,
    "Penumbra Vite dev server (port N, projectDir=X)")` after
    Process.Start; `Unregister(pid)` in `StopInternal`.
  - `Qualia.ViteManager` — same shape with Qualia intent string.
  - `ChromeLauncher` — `Register(pid, Path.GetFileName(chromePath),
    ..., cdpPort, "Chrome for CDP bridge (port N)")` after
    Process.Start; `Unregister(pid)` in `ChromeLaunchResult.Dispose`.
- **docs/mcp-server.md:** operator-facing setup guide — tool table,
  `.mcp.json` snippet, discovery-root behavior, spawn registry storage
  path, stdio smoke command (printf | exe), wire-protocol note
  explaining the self-contained choice.
- **Files added:**
  `src/Canary.McpServer/{Canary.McpServer.csproj, Program.cs, McpProtocol.cs}`,
  `src/Canary.McpServer/Tools/{FeedbackTools, RunsTools, LocalhostTools}.cs`,
  `src/Canary.Core/Telemetry/SpawnRegistry.cs`,
  `tests/Canary.Tests/Telemetry/SpawnRegistryTests.cs`,
  `tests/Canary.Tests/Mcp/McpServerToolDispatchTests.cs`,
  `docs/mcp-server.md`.
- **Files modified:**
  `Canary.sln` (added McpServer project),
  `src/Canary.Core/Localhost/LocalhostManager.cs` (Tier 2 overlay),
  `src/Canary.Agent.Penumbra/ViteManager.cs` (Register + Unregister),
  `src/Canary.Agent.Qualia/ViteManager.cs` (same),
  `src/Canary.Core/Cdp/ChromeLauncher.cs` (Register on launch +
    Unregister on Dispose),
  `tests/Canary.Tests/Canary.Tests.csproj` (added McpServer reference).
- **Verification:**
  - `dotnet build Canary.sln` = 0/0.
  - `dotnet test --filter "Category=Unit"` = 191 Passed (was 176; +15 new).
  - `dotnet test --filter "Category=Integration"` = 2 Passed (unchanged).
  - Stdio smoke: `printf '<init>\n<tools/list>\n' | Canary.McpServer.exe`
    returns valid JSON-RPC responses listing all 8 tools.
- **Deferred:**
  - **McpServerStdioIntegrationTests** — would spawn `Canary.McpServer.exe`
    as a child process from xUnit. The protocol-level tests in
    `McpServerToolDispatchTests.McpProtocol_*` already exercise the full
    stdio loop via `StringReader`/`StringWriter`; spawning the real
    process adds little signal and pollutes the test runner with stdio
    plumbing. Operator runs the printf-piped smoke for end-to-end
    confirmation.
  - **OS-level spawn hook** — explicitly out per operator decision Q3
    (voluntary registration only). Non-Canary spawns fall back to
    Tier 1 / Tier 3 (Phase 8).
- **Snapshot tag `pre-impl-phase6-2026-05-24`** preserved during the
  work; deleted at commit.

## 2026-05-24 — Debug-overhaul Phase 7 (C4 UI overhaul)

L-effort phase. Top-level nav tabs surface the surfaces Phases 2-6
shipped without UI exposure: Past Runs (Phase 3 per-run REPORTs),
Telemetry (Phase 2 NDJSON live tail), Feedback (Phase 5 inbox),
Localhost (Phase 4 + 6 with Tier 2 provenance), Settings (Phase 9
toggle stub). Plus the long-standing mode picker GUI gap (§A1).

- **Snapshot tag:** `pre-impl-phase7-2026-05-24` created; deleted on success.
- **INavMode interface (`src/Canary.UI/Navigation/INavMode.cs`):** Name +
  Description + `Control CreateContent()`. Implementations lazy-create
  + cache content.
- **5 nav-mode classes (`src/Canary.UI/Navigation/NavModes.cs`):**
  PastRunsNavMode, LocalhostNavMode, FeedbackNavMode, TelemetryNavMode,
  SettingsNavMode. PastRuns + Telemetry expose SetWorkloadsDir for
  MainForm to push the discovered workloads dir.
- **MainForm wraps the existing SplitContainer inside a TabControl:**
  Tests tab contains the historic tree + content panel (zero behavior
  change for existing flows); 5 new tabs lazy-instantiate their nav
  modes on first activation. The lazy-create handler also calls
  `PropagateWorkloadsDirToMode` so panels catch up if the operator
  opens them after the workloads dir loads.
- **Scope choice vs §C4 ASCII:** the design shows the tab strip nested
  below the TreeView on the LEFT pane (each mode swaps left + right
  panes). Phase 7 ships a simpler top-level TabControl wrapping the
  whole SplitContainer. Trade-off: less invasive refactor, ships
  in-session; doesn't quite match the design's left-pane-mode mental
  model. INavMode contract is unchanged so a future polish pass can
  re-arrange placement without touching panel internals.
- **PastRunsPanel:** SplitContainer with run list (cols: When/Workload/
  Test/Verdict) + REPORT.md preview. Filter textbox does substring
  match on all four columns. Refresh button re-walks
  `workloads/*/results/.../runs/<timestamp>/REPORT.md`. Verdict parser
  shared (made internal for tests).
- **FeedbackPanel:** TreeView of inbox/triaged/resolved buckets, each
  expanded by default showing .md filenames as children. Markdown
  preview on selection. Open-inbox-folder button shells out to
  Explorer. DiscoverFeedbackRoot walks up from
  AppContext.BaseDirectory.
- **TelemetryPanel:** 2s polling timer (only when Visible) finds the
  newest `telemetry.ndjson` under `workloads/`, re-reads only on
  LastWriteTimeUtc change. ListView with time/kind/source/level/data
  (color-coded by level). Source-filter dropdown. Uses
  `FileShare.ReadWrite` to coexist with an actively-writing CLI run.
- **SettingsPanel:** Stabilization (default) / Maturation radio per
  §C9. Placeholder labels for Tier 3 toggle + retention slider +
  persistence (all explicitly noted as Phase 8 work).
- **Mode picker on toolbar:** ToolStripComboBox with pixel-diff /
  vlm / both. `MainForm.OnRunTests` reads it (after the one-shot
  `_autoRunModeOverride` from CLI handoff) and passes through to
  `TestRunnerPanel.RunAsync.modeOverride`. Resolves §A1 gap (GUI runs
  used to ignore --mode).
- **Localhost toolbar button:** Phase 4 opened a popup form; Phase 7
  switches the click handler to select the Localhost nav tab instead.
- **InternalsVisibleTo Canary.Tests added to Canary.UI.csproj** so
  panel-internal helpers (PastRunsPanel.ParseVerdict,
  FeedbackPanel.DiscoverFeedbackRoot) are unit-testable.
- **Files added:** `src/Canary.UI/Navigation/{INavMode,NavModes}.cs`,
  `src/Canary.UI/Panels/{PastRunsPanel,FeedbackPanel,TelemetryPanel,SettingsPanel}.cs`,
  `tests/Canary.Tests/Navigation/NavModeTests.cs`,
  `tests/Canary.Tests/UI/PastRunsIndexTests.cs`.
- **Files modified:** `src/Canary.UI/MainForm.cs` (nav TabControl,
  AddNavTab + PropagateWorkloadsDirToMode helpers, OnShowLocalhost
  now selects tab, mode picker + ReadToolbarMode, _modePicker field,
  using Canary.UI.Navigation, SetWorkloadsDir calls in
  LoadWorkloadsDirAsync), `src/Canary.UI/Canary.UI.csproj`
  (InternalsVisibleTo), `src/Canary.UI/Panels/TelemetryPanel.cs`
  (renamed `Refresh` → `RefreshTelemetry` to avoid shadowing
  Control.Refresh).
- **Verification:** `dotnet build Canary.sln` = 0/0. `dotnet test
  --filter "Category=Unit"` = 212 Passed (was 191; +21 new). `dotnet
  test --filter "Category=Integration"` = 2 Passed (unchanged). CLI
  smoke unchanged. UI manual smoke deferred to operator (visual sanity
  of new tabs requires a desktop session).
- **Deferred to Phase 8:**
  - Settings persistence + actual UI-mode flip behavior (Stabilization
    vs Maturation).
  - Tier 3 toggle wiring.
  - Retention slider.
  - PastRuns search across REPORT.md body content + tag-based
    filtering.
  - PastRuns ↔ AnnotatedImageForm hand-off per §C8 (existing
    ImageViewerForm Annotate button still works).
  - UIOverhaulSmokeTests integration test (requires a desktop session +
    forms-message-pump-friendly fixture; the NavModeTests cover the
    contract; operator smokes the visual end).
- **Snapshot tag `pre-impl-phase7-2026-05-24`** preserved during the
  work; deleted at commit.

## 2026-05-24 — Debug-overhaul Phase 8 (C7 Tier 3 + C8 polish + C9 settings)

S-M effort phase. Tier 3 heuristic process listing + per-user settings
persistence + PastRuns quick-date filters. No snapshot tag — additive
polish.

- **HeuristicProcessLister (`src/Canary.Core/Localhost/`)**: Tier 3 of
  §C7. Filters Process.GetProcesses() by name against a default list
  (node, deno, bun, npm/npx/yarn/pnpm, python, dotnet, cargo, tauri,
  ruby/rails, go). Returns Pid + Name + StartTime + MainWindowTitle.
  WMI command-line filtering deliberately deferred — name-only is the
  pragmatic Tier 3 ship; can land in a polish follow-up if signal too
  noisy.
- **LocalhostPanel Tier 3 toggle:** inline CheckBox reads initial state
  from CanarySettings; toggling re-runs RefreshAsync which appends
  heuristic-only PIDs (those not already in Tier 1/2 enumeration) with
  Provenance = DevServerHeuristic + dimmer row color + caveat label
  in the CommandLine column. Status footer shows the count split
  ("5 listening + 12 heuristic").
- **CanarySettings (`src/Canary.Core/Settings/`)**: JSON file at
  %LocalAppData%\Canary\settings.json. Fields: UiMode (stabilization /
  maturation), ShowTier3Processes (bool), RetentionDays (int, default
  14, range 1–365). Load() returns defaults on missing file or parse
  failure. Save() does atomic write-to-.tmp + rename. JsonOptions
  exposed for serializer reuse.
- **SettingsPanel persistence wiring:** Phase 7's placeholder gets
  hooked up — radios / checkbox / numeric input changes fire
  PersistAndNotify which calls Save + raises SettingsChanged event +
  updates status label. Initial state hydrated from CanarySettings.Load.
  Stabilization radio label clarifies "Maturation panels NOT in v1 per
  §C9 — toggle only" per the design's explicit scope statement.
- **PastRunsPanel quick filters:** All / Last 7d / Last 30d buttons in a
  FlowLayoutPanel; active button highlighted blue. ApplyFilter combines
  the date range with the substring filter (date is the outer
  constraint, substring narrows further). TableLayoutPanel ColumnCount
  bumped 3 → 4.
- **Files added:** `src/Canary.Core/Localhost/HeuristicProcessLister.cs`,
  `src/Canary.Core/Settings/CanarySettings.cs`,
  `tests/Canary.Tests/Localhost/Tier3HeuristicTests.cs`,
  `tests/Canary.Tests/Settings/CanarySettingsTests.cs`.
- **Files modified:** `src/Canary.UI/Panels/SettingsPanel.cs` (load +
  save wiring, Tier 3 + retention controls, SettingsChanged event),
  `src/Canary.UI/Panels/PastRunsPanel.cs` (quick-date filter buttons,
  ApplyFilter combines date + substring), `src/Canary.UI/Controls/LocalhostPanel.cs`
  (Tier 3 inline toggle + heuristic-row append in RefreshAsync,
  CanarySettings hydration).
- **Verification:** `dotnet build Canary.sln` = 0/0. `dotnet test
  --filter "Category=Unit"` = 220 Passed (was 212; +8 new). `dotnet test
  --filter "Category=Integration"` = 2 Passed (unchanged).
- **Deferred:**
  - **WMI Win32_Process command-line filtering for Tier 3** — name-only
    is the ship; can land if false-positive rate is too high.
  - **Maturation-mode panels** — explicitly NOT in v1 per §C9; the
    Settings toggle persists the choice but no behavior flips today.
  - **Retention helper auto-wiring** — `ResultRetention.PurgeOlderThan`
    is callable, but no automatic invocation at CLI / UI startup yet.
    Operator can call it from scripts using the persisted
    `CanarySettings.RetentionDays`.
  - **PastRuns body search across REPORT.md content** — current filter
    is metadata-only (workload/test/verdict/runId). Body search would
    need lazy-load + index; future polish.

## 2026-05-24 — Debug-overhaul Phase 9 (cross-repo doc pass)

S-effort phase. Final phase of the debug-overhaul implementation.

- **Canary/CLAUDE.md Quick Reference** rewritten to point operators at
  the new debug-overhaul surfaces (toolbar mode picker, 6 nav tabs,
  per-run dir layout, telemetry NDJSON path, MCP server, feedback
  inbox). The §16 rule 8 line updated to reflect Phase 1 shipped (no
  longer "queued").
- **Canary/docs/features/FEATURE_STATUS.md** gains a Debug-overhaul
  section table mapping each shipped feature → phase number + the
  consolidated deferred follow-ups list (10 items).
- **Canary/docs/plans/2026-05-24-canary-debug-overhaul.md** frontmatter
  flipped `in-progress → shipped`; retrospective section appended (what
  shipped exactly as designed, scope deviations table, deferred items,
  counts, operator-visible deltas).
- **C:/Repos/Penumbra/CLAUDE.md** gains a "Canary integration
  (debug-overhaul shipped 2026-05-24)" section after Quick Reference,
  documenting telemetry capture + per-run REPORT.md + MCP server +
  spawn registry + feedback inbox. Notes that no Penumbra-side code
  changes were needed.
- **C:/Repos/Qualia/CLAUDE.md** gains the same section (Qualia-relevant
  highlights focus on toolbar mode picker for VLM oracle tests +
  spawn-registry attribution).
- **C:/Repos/MultiVerse/BUILD_LOG.md** gains one consolidated cross-repo
  entry summarising the full debug-overhaul outcome + deferral list.
- **MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md**
  frontmatter flipped `status: READY → EXECUTED` + `executed: 2026-05-24`
  + banner mirroring the design doc retrospective (10 phases landed,
  ~30 commits, build 0/0, Unit 107 → 220, Integration 0 → 2; headline
  outcomes; pragmatic deviations; deferred follow-ups).
- **No Rhino-side CLAUDE.md update** — Rhino-side telemetry interception
  was deferred in Phase 2 (no clean RhinoCommon 8 hook); the repo
  layout has no separate Rhino/ CLAUDE.md (RhinoIfc is unrelated).
- **Files modified (this session, this phase):**
  - `CLAUDE.md`, `docs/features/FEATURE_STATUS.md`,
    `docs/plans/2026-05-24-canary-debug-overhaul.md` (Canary).
  - `C:/Repos/Penumbra/CLAUDE.md`,
    `C:/Repos/Qualia/CLAUDE.md`,
    `C:/Repos/MultiVerse/BUILD_LOG.md`,
    `C:/Repos/MultiVerse/prompts/canary-debug-overhaul-implement-2026-05-24.md`
    (cross-repo).
- **Per §0.3 item 12 + 13 of the implementation prompt: status flip +
  prompt frontmatter EXECUTED — both done.**
- **No verification step (docs-only).** Existing build + test green
  state from Phase 8 carries forward (220 Unit + 2 Integration; 0/0).

## 2026-05-24 — Debug-overhaul implementation COMPLETE

All 9 design phases (C1-C9) + Phase 0 pre-flight + precursor shipped
across one agentic session. ~30 commits on master past the
`pre-impl-debug-overhaul-2026-05-24` snapshot tag. Build 0/0 throughout.
Unit tests 107 → 220 (+113); integration tests 0 → 2.

Snapshot tag preserved as the rollback anchor; per the implementation
prompt §11 "Delete the master snapshot tag (only if you confirm
everything is pushed)", the tag is kept for the operator's review and
deletion. Final operator-facing summary appended to this log per the
prompt's §11 final-summary step.

**Final summary:**
- Total commits: ~30 past `pre-impl-debug-overhaul-2026-05-24`.
- Per-phase commit counts (approx): Phase 0 = 1; Precursor = 2;
  Phase 1 = 3; Phase 2 = 5; Phase 3 = 5; Phase 4 = 4; Phase 5 = 5;
  Phase 6 = 5; Phase 7 = 4; Phase 8 = 4; Phase 9 = 1.
- Tests added: +113 Unit, +2 Integration.
- Cross-repo updates: Penumbra CLAUDE.md, Qualia CLAUDE.md, MultiVerse
  BUILD_LOG.md, MultiVerse implementation prompt frontmatter.
- Deviations from design: 5 documented in the retrospective (UI
  layout, mode picker placement, candidates/diffs flatness, MCP
  transport, Tier 3 filter scope).
- Follow-up bugs filed: bug 0007 (CLI exit code) shipped its own fix
  inside Phase Precursor; no new bugs filed by the implementation.
- Cross-repo work that surfaced needing a separate prompt: none.
  Penumbra + Qualia integrations Just Worked through the existing
  hooks contract; no contract changes required.
- VLM + pixel-diff functional check: deferred to operator-side smoke
  on hardware-bearing machine. The Phase 8 build + test run completes
  green; no code path was modified in ways that would break the
  existing VLM/pixel-diff verdict generation.

## 2026-05-24 — Debug-overhaul post-Phase-9 polish (operator screenshot review)

Operator ran the UI after Phase 9 closeout, screenshotted the result,
and called out four toolbar/nav-tab UX issues. All four addressed in
one commit (`0946954`):

1. **Mode picker truncation.** Width 110 → 140 px so "pixel-diff" +
   chevron fit. The Phase 7 default ate ~20px on the chevron leaving
   ~90px for text — short by ~10px.
2. **Nav tabs visually weak.** Upgraded to `TabAppearance.FlatButtons`
   + fixed `ItemSize(140, 32)` + Segoe UI 10.5pt (was default 9pt) +
   `Padding(12, 6)`. Now visibly the primary nav surface.
3. **Toolbar clutter on non-Tests tabs.** Tests-only items (Run Tests
   / Mode / Record / Approve / View Report / Deploy Agent / Close
   Workload / Expand All + their grouping separators) collected into a
   `_testsOnlyToolbarItems` array; `_navTabControl.SelectedIndexChanged`
   now calls `UpdateToolbarVisibilityForActiveTab` which toggles
   `Visible` on each. Open Folder stays visible everywhere. Implements
   the §C4 polish that was explicitly deferred from Phase 7.
4. **Localhost toolbar button** dropped entirely. Phase 4 added it as
   a popup launcher; Phase 7 made it a tab-switch shortcut redundant
   with the Localhost nav tab. `OnShowLocalhost` handler deleted (zero
   remaining callers).

Verification: `dotnet build src/Canary.UI/Canary.UI.csproj
--configuration Release` = 0/0. `dotnet test --filter "Category=Unit"` =
220 Passed (unchanged). Operator relaunched, confirmed "looks good".

## 2026-05-24 — Debug-overhaul session closeout

End-of-session bookkeeping:

- **Commits this session past `pre-impl-debug-overhaul-2026-05-24`:** 40
  (39 implementation + 1 post-Phase-9 polish).
- **Cross-repo pushes done:** Penumbra `5c5672f..d372694` → origin/main;
  Qualia `0cc90ca..04c5e29` → origin/master; MultiVerse `dc574ce..75a9c66`
  → origin/main.
- **Canary push state:** master at `0946954`, fully synced with
  origin/master.
- **Snapshot tag `pre-impl-debug-overhaul-2026-05-24`:** preserved as
  the rollback anchor per the implementation prompt §11 ("Delete the
  master snapshot tag (only if you confirm everything is pushed)").
  Operator's call when to delete; meanwhile it's a cheap safety
  net pointing at HEAD ~40 commits ago.
- **`.claude/settings.local.json`:** harness auto-appended a
  `Bash(dotnet *)` allowlist entry during the session — committed as a
  tracked chore so the working tree is clean (file is tracked, not in
  .gitignore).
- **Working tree state at session end:** clean. Master branch only.
- **Hardware-bearing follow-ups for operator on a separate machine** (per
  the prompt's hard rule 8 + the deferred integration tests):
  - Run Penumbra workload end-to-end to confirm Phase 2 CDP telemetry
    capture produces a populated `telemetry.ndjson` and Phase 3
    `REPORT.md` reflects accurate verdicts (already smoked on Qualia
    `display-modes` suite during this session — 10 NEW baselines).
  - Run Rhino-workload tests (need Rhino 8 installed) to confirm the
    non-bridge `RunTestAsync` path also writes per-run `runs/<timestamp>/`
    dirs correctly.
  - Register `Canary.McpServer.exe` in a Claude Code `.mcp.json` +
    exercise `list_recent_runs` / `get_run_report` /
    `list_localhost_ports` from a Claude session.
  - VLM smoke (`canary run --workload qualia --suite landing-screen
    --mode vlm`) to confirm the toolbar mode picker + Phase 2
    telemetry coexist with VLM oracle evaluation.

## 2026-06-11 — Operator-feedback UI fixes (suite tree + Run History pane)

- **Scope:** the two deferred 2026-06-10 feedback items, green-lit today.
  Canary.UI (Avalonia) only — no harness/agent/comparison changes.
- **Step 0:** clean tree at `3f2ac4c`; build 0/0; unit tests 304/305 — the
  one failure was a stale enum-shape guard
  (`CheckpointMode_Enum_HasTwoValues`) left behind by the 2026-06-09
  Capture-mode work. Fixed to assert three values (`4d42ae3`).
- **Item 2 — collapsible suite tree (`77500e1`):** suite nodes in the Tests
  tree now nest their member tests (collapsed by default, suite-JSON order,
  full TestDefinition payload so Run/Edit/Approve/details work; red
  "(missing)" leaf for dangling suite entries). 1 new unit test.
  Operator eyeballed the 15-test kbridge suite in the launched UI: approved.
- **Item 1 — Run History docked pane:** hard-gate question answered by the
  operator: **docked companion pane** (not a Past Runs enhancement).
  New `RunHistoryScanner` (walks `results/<test>/runs/<stamp>/` and
  suite-nested `results/<suite>/<test>/runs/<stamp>/`; excludes legacy flat
  results + archived snapshots), `RunHistoryViewModel`,
  `RunHistoryPaneView` docked below the NavigationView in MainWindow.
  Auto-refresh after in-UI runs via new `TestRunnerViewModel.RunCompleted`
  event. Double-click opens REPORT.md. 3 new unit tests.
  Operator eyeballed the pane in the launched UI: approved.
- **Build/tests at wrap:** 0 errors / 0 warnings; 309/309 unit tests.
- **Feedback lifecycle:** both items inbox → triaged → resolved.
- **Cross-repo impact:** none (UI-only).

## 2026-06-13 — Penumbra-in-Rhino live-preview harness (Phase 0, gate ▣0-ready)
Added `WaitForPenumbraFrame` agent action to Canary.Agent.Rhino (reflection into Penumbra.Bridge.PenumbraBridge.GetFrameState — peer-agnostic, no hard ref; polls RealRevision, RhinoApp.Wait pump). Authored workloads/rhino/tests/penumbra-rhino-00-smoke.json (RunCommand PenumbraShow → WaitForPenumbraFrame → capture the box-sphere demo, mode=capture) + suites/penumbra.json. Build: Canary.Agent.Rhino Release 0 warn/0 err (after fixing 2 nullable CS8600). Schema-validated against TestRunner (JsonExtensionData→AsParameters; checkpoints after blocking actions; setup.File optional). Gate ▣0 (operator: heads-up Canary, --suite penumbra, approve baseline) pending — needs Penumbra .rhp auto-load registered + PENUMBRA_HOST_DEV set. Penumbra side (frame-ready hook) done in the Penumbra repo same day.

## 2026-06-13 — ▣0 first run: stale Rhino agent (rebuild gotcha)
First `--suite penumbra` run CRASHED: "Unknown action: WaitForPenumbraFrame" — from RhinoAgent's dispatch default, i.e. the harness relayed it fine but Rhino auto-loaded a STALE Canary.Agent.Rhino.rhp (registered to the bin/Debug/net48 build, Jun 2) lacking the new action. My earlier build was Release-only. Fix: rebuilt the agent in BOTH Debug + Release (both .rhp now carry WaitForPenumbraFrame). GOTCHA for future RhinoAgent changes: Rhino auto-loads the registered .rhp by config — rebuild the SAME config Rhino is registered to (Debug here), and ensure Rhino is fully closed so the new .rhp loads (a reused shared-suite Rhino keeps the old plugin in memory). Canary.UI does NOT need rebuilding for a new agent action — it only relays the action string over the pipe. Re-run pending.

## 2026-06-13 — ▣0 GREEN: Penumbra-in-Rhino overlay captured via Canary
penumbra-rhino-00-smoke PASSED with a visible orange sphere in active.png — full chain proven (plug-in → host → render → conduit → frame dump → file-source capture). Closed out: stale Debug agent, host-dir fallback, System.Web.Extensions→MiniJson (bug 0052), un-capturable overlay→file-source+literal filePath, host concurrency, resolution-specific cache, scene-scale-vs-camera-distance (demo→centered sphere r150). WaitForPenumbraFrame action + TestCheckpoint.FilePath shipped. Next: conduit view auto-fit (C4) so arbitrary-scale CPig fields frame, then ▣1 gyroid.

## 2026-06-13 — Documentation consolidation + ▣1 gyroid Slop test
Consolidated the Rhino-viewer integration lessons into Penumbra/hosts/rhino/LESSONS-rhino-viewer-integration.md (8 hard-won lessons), updated the progress doc §4 (hypotheses→confirmed) + CLAUDE.md pointer. New Slop test CPig/research/slop_tests/53_penumbra_preview.json (Field TPMS gyroid ∩ sphere-r150 → Penumbra Preview …BA200, Solid variant, period 30) + Canary test penumbra-rhino-01-gyroid.json (Slop loader → JsonPath → PenumbraShow → Build → WaitForPenumbraFrame → file-source capture active.png) + added to penumbra suite. JSON validated. Operator-run (heads-up Canary) pending = ▣1.

## 2026-06-15 — Penumbra Studio debug loop (new tests/suite) + drove a Penumbra fix
Added standalone Penumbra-Studio debug tests workloads/rhino/tests/penumbra-rhino-studio-{sphere,gyroid}.json + suites/penumbra-studio.json (RunCommand _PenumbraSphere/_PenumbraGyroid → WaitForPenumbraFrame(real) → file-source active.png; no GH/Slop). Used the loop headless (agent e2e, --headless) to reproduce + root-cause a Penumbra blank-gyroid bug: sphere framed, gyroid blank, same harness → instrumented telemetry showed auto-fit zoomed the doc active viewport but the conduit renders a different viewport (multi-view) → fixed Penumbra-side (fit follows rendered viewport); gyroid candidate 988B→206KB lattice. NO Canary code change — data-only (test/suite JSON). Documented the debug loop + footguns in CLAUDE.md ("Penumbra Studio debug loop"): run ISOLATED not --suite (cumulative RealRevision), run from C:\Repos\Canary (CWD-relative workloads), delete active.png before each run (stale-capture), capture-mode never FAILs (eval PNG+telemetry). Queued Tier-2: a telemetryPath field to tail Penumbra's preview/telemetry.ndjson into REPORT.md.
