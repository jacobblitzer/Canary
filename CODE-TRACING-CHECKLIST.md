# CODE-TRACING-CHECKLIST — Canary

> Per `MultiVerse/SUPERVISOR.md` Discipline 6: before any non-trivial code change touching one of the areas below, READ the listed trace files end-to-end. After the session, UPDATE this file with any newly discovered load-bearing path.

## Format

```
### <area>
- **Touching:** <files you'd be editing>
- **Trace:** <other files that consume / produce / interact>
- **Why:** <one line>
- **Last bit:** <date>
```

---

## Workload editor persistence (WorkloadConfig round-trip)

- **Touching:** `src/Canary.Core/Config/WorkloadConfig.cs`, the editor VMs in
  `src/Canary.UI.Avalonia/ViewModels/Editors/`, or `MainWindow.PersistAndRefreshAsync`
- **Trace:** the UI workload editor persists by SERIALIZING the mutated
  `WorkloadConfig` back over `workload.json` (`WorkloadEditorViewModel.ToJson`
  → `PersistAndRefreshAsync`). `WorkloadConfig` models only the shared launch
  fields — the per-agent blocks (`qualiaConfig`, `penumbraConfig`) are
  deserialized SEPARATELY from the same file by the agent factories/CLI, so
  they are invisible to this type. `[JsonExtensionData] ExtensionData` (bug
  0018) is what round-trips them through the editor: REMOVE it and any UI
  Save on a browser workload silently deletes the whole agent block
  (qualia-web: viteScript/vitePort/cdpPort/projectDir; qualia-desktop:
  `desktop:true` + appExePath). A new typed property on WorkloadConfig simply
  moves that key out of ExtensionData — fine; a new SIBLING agent block needs
  NO change here to survive. Regression tests:
  `WorkloadEditorViewModelTests.RoundTrip_PreservesUnknownAgentConfigBlocks`
  + `RealQualiaWebWorkload_SurvivesEditorSave`.
- **Why:** the editor path is lossy by construction for anything the POCO
  doesn't model; the catch-all is the only thing standing between a UI Save
  and destroyed workload config.
- **Last bit:** 2026-07-24 (bug 0018)

## Test checkpoint modes (capture / pixel-diff / vlm)

- **Touching:** any new test JSON in `workloads/rhino/tests/*.json` or `workloads/penumbra/`, or changes to `src/Canary.Core/Orchestration/TestRunner.cs`
- **Trace:** `mode: capture` always passes (no comparison, saves screenshot), `pixel-diff` needs a baseline (first run = `New` status, not pass), `vlm` needs Ollama. The `--mode` CLI flag in `src/Canary.Harness/Cli/RunCommand.cs` interacts with `ModeOverride` enum in TestRunner — capture mode wins over any override. First-run pixel-diff with no baseline → status `New`, NOT a real pass.
- **Why:** misunderstanding capture-mode semantics has led to "tests pass" claims when they're vacuous.
- **Last bit:** 2026-06-24 (codified at template creation)

## Rhino macros in test `setup.commands`

- **Touching:** any `setup.commands` string in a rhino test JSON
- **Trace:** strings run via `RhinoApp.RunScript`. If the macro doesn't FULLY exit the command → Rhino at a sub-prompt → next agent request hangs → "Pipe disconnected" crash (looks like Canary bug; it's the macro). Use `_EnterEnd` for deep commands (Options / DocumentProperties) — NOT a counted `_Enter` chain (breaks on conditional prompts like "scale by 0.1?"). Must include every sub-option keyword (skipping `_UnitSystem` for example feeds the unit name to the wrong menu → hang).
- **Why:** macro failures look like Canary harness failures. They're not — they're prompt-state-leak failures.
- **Last bit:** 2026-06-24

## Penumbra frame-state reflection contract (WaitForPenumbraFrame + GetPenumbraFrameState)

- **Touching:** `Canary.Agent.Rhino` `WaitForPenumbraFrame` or `GetPenumbraFrameState` actions
- **Trace:** `RhinoAgent.ResolveGetFrameState` + `ReadFrameState` are THE single reflection seam
  into `PenumbraBridge.GetFrameState()` (factored 2026-07-02, flight-recorder Phase A; do NOT
  duplicate the assembly scan or field reads — audit-c pins one seam). Fields actually read:
  `RealRevision`, `PresentedRevision`, `EvalMode`, `Status`, `DisabledByError` (the earlier
  `BakeLevel` claim was stale — no such read exists in RhinoAgent.cs), plus `BakesOutstanding`
  as the ONLY null-tolerant read (additive 2026-07-03, Penumbra bug 0058/R1.2 — `GetField` may
  return null on older plugins; null = "unknown", never coerce to 0 for gating). `requireSteady`
  now gates on Status containing " steady" AND `BakesOutstanding` null-or-0 (bake-complete
  capture gate). Two consumers: `WaitForPenumbraFrame` (blocking wait, quietMs/requireSteady
  gates) and `GetPenumbraFrameState` (one-shot, feeds session capture markers +
  active-view/view-list; emits `bakesOutstanding` = number or "n/a").
  RENAMING any field on Penumbra's side silently breaks Canary at next test run — no compile
  error. Cross-repo contract listed in `Penumbra/spec/PEERS.md` (and Penumbra-perf equivalent).
- **Why:** silent breakage. Renames need a coordinated cross-repo commit.
- **Last bit:** 2026-07-03 (BakesOutstanding null-tolerant read + requireSteady bake gate, R1.2)

## AppLauncher env auto-resolve

- **Touching:** `src/Canary.Core/Orchestration/AppLauncher.cs` or any env-var Canary spawns child processes with
- **Trace:** `AppLauncher.Launch` ENUMERATES every `PENUMBRA_*` env var present in the User-scope registry OR the current process env, then forwards/overrides each into the spawned process env. **2026-06-24 — was a hardcoded 3-element list; recurring bug across 5+ sessions because every new `PENUMBRA_*` var (HOST_FSM_TS, ALLOW_VERSION_SKEW, etc.) silently failed to forward.** Now enumeration-based: adding a new Penumbra env var requires ZERO changes here. Opt-out: `CANARY_USE_INHERITED_PENUMBRA_ENV=1`. Console line `[canary-env] auto-resolve scanning N PENUMBRA_* var(s)` confirms the scan happened.
- **Why:** without auto-resolve, Canary-spawned Rhino runs with whatever env Canary.UI started with — typically stale. The hardcoded-list trap meant new Penumbra features silently didn't activate in Canary tests.
- **TRAP (2026-07-02):** the auto-resolve loop actively STRIPS any `PENUMBRA_*` var present only
  in the process env but not the User registry ("clear to match user state"). A per-spawn var
  (e.g. `PENUMBRA_SESSION_REF`) set via ordinary process env therefore NEVER reaches the child —
  silently. The ONLY sanctioned route is `AppLauncher.LaunchWithEnv(config, extraEnv)`: entries
  are applied AFTER the loop and exempt from the strip. `Launch(config)` remains the plain
  equivalent. `LaunchResult.AppliedEnv` records every decision for the session manifest.
- **Last bit:** 2026-07-02 (LaunchWithEnv/extraEnv bypass added; strip trap documented)

## OrphanNodeCleaner

- **Touching:** `src/Canary.Agent.Common/OrphanNodeCleaner.cs` or its invocation points
- **Trace:** runs at every Canary session/test boundary — pre-launch sweep + pre-kill children + post-kill orphans. Kills parentless `node.exe`. Opt-out: `CANARY_DISABLE_ORPHAN_KILL=1`. Without it, prior session's leaked node hosts pile up + sometimes hold pipes that conflict with new spawns.
- **Why:** orphan accumulation eventually causes weird failures (pipe collisions, port reuse).
- **Last bit:** 2026-06-24

## Telemetry capture (CDP + NDJSON)

- **Touching:** `Canary.Agent.Penumbra` (browser) or `Canary.Agent.Rhino` telemetry surfaces
- **Trace:** browser tests capture `Runtime.consoleAPICalled` + `Log.entryAdded` + `Network.*` into `workloads/penumbra/results/[<suite>/]telemetry.ndjson`. Rhino tests use `PenumbraPreviewTelemetryTail` reading `%LocalAppData%\Penumbra\preview\telemetry.ndjson`. REPORT.md is generated alongside `result.json` per run.
- **Why:** Penumbra-side NDJSON event renames (e.g. `gl.cascade.bake-done`) break any Canary asserts that read specific event types.
- **Last bit:** 2026-06-24

## Slop test authoring

- **Touching:** any new test under `workloads/rhino/tests/cpig-*` or related
- **Trace:** generator is `CPig/scripts/gen_retopo_slop_tests.py` (auto-inserts Log Tap). `Slop/SLOP_STYLE.md` has the layout rules. CPig inputs are `item`/`list`, never `tree` — shape upstream of CPig nodes. Component lookup: `Slop/fodder/tools/lookup_component.py "<name>"`. Pin components by GUID, not by library label.
- **Why:** wrong data shape feeds CPig wrong inputs silently — test "passes" but wasn't testing what you thought.
- **Last bit:** 2026-06-24

## Viewport capture geometry (SetViewport / floating / pixel-diff baselines)

- **Touching:** `RhinoAgent.HandleSetViewport`, `TestRunner.SendSetupCommandsAsync` / `BuildViewportParams`, any test's `viewport` block, or approving a baseline
- **Trace:** ORDER inside HandleSetViewport is load-bearing: activate named view → float+Size (or MaxViewport — they are rivals, never both) → camera refit/zoom. `view.Size` is silently IGNORED on a docked/maximized view, so only `floating: true` produces deterministic capture dimensions; the refit zooms `doc.Views.ActiveView`, so float FIRST or the framed frustum is carried into a resized window and content clips at the edges. In `shared` runMode only the FIRST test's setup runs — per-test capture context belongs on the CHECKPOINT (`checkpoint.viewport`), which applies in suite AND solo runs. Diag log: `C:\Repos\CPig\logs\agent_viewport_diag.log` (hardcoded path — debt).
- **Why:** 2026-08-07 — a "viewport size mismatch" unpicked into five stacked defects; the first fix attempt clipped a 12-point grid to 6 points and was one `canary approve` away from enshrining a broken baseline. **Never approve a baseline you have not eyeballed against the test's vlmDescription.**
- **Last bit:** 2026-08-07

## Timeouts assert warmth, not correctness

- **Touching:** any `timeoutMs` on a wait action, or the agent's GH-canvas startup budget
- **Trace:** GH cold-init on this machine exceeds 30s (plugins load from a cloud-synced drive) — the agent budget is 90s for that reason. `bristle-02/03` waiting 20s where `bristle-05` waits 90s produced false CRASHes twice in one day from documented cache invalidations. A failed fixture open now ABORTS setup loudly (`TestRunner.SendSetupCommandsAsync` checks the agent response); before 2026-08-07 it logged "Setup commands complete" and every action died with 'No active Grasshopper document', which cost a full bisection of two innocent repos.
- **Why:** a tight timeout on a cold path fails on cache/boot state, not on the thing the test claims to verify — and a swallowed setup failure points the blame everywhere except the cause.
- **Last bit:** 2026-08-07

## Seeded prereqs rot; keep-open failures masquerade as hangs

- **Touching:** any test asserting on engine STATE the test did not create (BR_Watch pickups, job-list contents), or running keep-open tests headless from an agent session
- **Trace:** bristle-17/20 assert `WatchLog contains "picked up"`, which needs an app COMMIT surviving in the engine's pruned job list (`keep_last`). The commit was whatever the last app session left behind — after ~50 jobs of churn none remained, Watch idled at "watching for app commits...", the wait timed out, and `keepOpenOnFailure` held a HEADLESS Rhino open forever. Two 35-minute "hangs" on 2026-08-14 were exactly this, invisible because `| Select-Object -Last` buffers canary stdout until exit. Fix pattern: make the prereq explicit — `scripts/bristle-gate-seed.ps1` seeds one commit then runs both gates (the bristle-22-seed precedent). Agent-side rule: stream canary output to a file and watch for `Results:`; never trust silence from a keep-open test.
- **Why:** a test that depends on leftover state passes for days and then fails on churn — the failure points at the day's diff, not at the missing fixture.
- **Last bit:** 2026-08-14

## A green suite that compares nothing: the baseline must be REACHED, not merely present

- **Touching:** anything that derives a result/baseline directory, `runMode`/suite dispatch, `GetTestDirectory`, `ResultPaths`, `canary approve` scoping, or a checkpoint's `mode`
- **Trace:** three separately-reasonable behaviours compose into a pass over nothing. A missing baseline yields `New`; `New` is excluded from the exit code (`RunCommand.cs:163-164`, `(Failed + Crashed) == 0 ? 0 : 1`); and the derivation is SPLIT — `RunSharedSuiteAsync` has no `suiteName` parameter at all (`TestRunner.cs:546`), so it reads `results/<test>/`, while `BaselineManager.GetTestDirectory` writes `results/<suite>/<test>/` whenever approval was given a suite. Six suites are in that state right now — penumbra `effects`/`environment`/`materials`/`d1-lipschitz`/`d3-tricubic` and qualia `playground`: 32 tests, 59 approved PNGs, `reachable = 0`, all reporting green. Second trap, same class: the `mode == Capture` early-return at `:1256-1263`/`:1545-1552` returns `Passed` BEFORE `baselinePath` is computed at `:1270`/`:1560`, so a guard written inside `if (!File.Exists(baselinePath))` is unreachable and a one-word JSON edit disarms a comparison with the file still on disk (rhino: 265 capture-only vs 136 armed).
- **Why:** "the PNG is on disk" and "this run compared it" are different claims, and only the second one is a test. Presence proves nothing about reachability, `New` hides the gap, and `doctor` cannot see it either — `DoctorCommand.cs` skips any path containing `/results/` by design. Check the ledger (`canary baselines verify`), and when you change a derivation, confirm the row count BEFORE and AFTER: a short ledger verifies green.
- **Resolved 2026-08-17 (Phase 2b C3):** there is now ONE derivation (`ResultPaths`), `suiteName` is gone from every evidence path, and the arming gate sits in the two dispatch funnels ABOVE the mode loop so a `capture` flip cannot slip under it. `canary baselines verify` is the check; `SingleResultDerivationTests` stops a second derivation appearing. What to re-check when touching this: the row COUNT before and after, because a short ledger verifies green.
- **Last bit:** 2026-08-17

## Non-zero defaults make "omitted" indistinguishable from "explicit"

- **Touching:** any config field whose checker reads `if (x > 0)` / `if (x != null)`, and any per-checkpoint override that REPLACES rather than merges a parent block
- **Trace:** `ViewportSetup.Width`/`Height` default to **800/600**, so `BuildViewportParams`' `if (viewport.Width > 0)` guard cannot distinguish a size the author wrote from one they omitted. Every per-checkpoint `viewport` override therefore requested 800x600 and discarded whatever the test declared — the 13 cpig-kin tests asked for 800x600, 1200x600, 1000x700 and 900x900, and all of them captured at the pane size, with baselines uniformly 960x600. Those declared sizes were never honoured, from the day the tests were written. Same shape one layer up: the old `GetTestDirectory` keyed off `suiteName != null`, and because `Path.Combine` drops empty segments, `""` read as "a suite was supplied" and silently produced the UNSCOPED path — so normalising "no suite" to `""` would have looked applied and behaved as before.
- **Why:** a sentinel that is also a legal value erases the distinction the guard exists to make. Default to 0/null so the guard means what it says, and when a checkpoint-level block overrides a test-level one, decide explicitly whether it MERGES or REPLACES — silent replacement throws away the parent's declaration.
- **Last bit:** 2026-08-17

## Two halves of one contract, written at two times

- **Touching:** adding a field to an agent response and a reader for it in the harness; anything where a producer and consumer are edited in separate passes
- **Trace:** 2026-08-18. `GetHostState` emitted a rich `ghLibraries` JSON blob; the harness parsed a field called `loaded` in `id=detail` form. Both halves were correct in isolation, and the agent was written before the design that named the field. The harness saw an empty map and reported `gh:Slop` and `gh:CPig Kinematics` **absent on a machine where both were loaded** — a FALSE RED that would have blocked a healthy install. This is the same defect as the dual `GetTestDirectory`: two things that must agree, with nothing forcing them to.
- **Why:** blocking a good machine is a different mistake from passing a bad one, and no more acceptable. When a producer and a consumer are added in separate passes, run the pair end-to-end before believing either — and prefer ONE namespace shared by both sides (requirement ids and reported ids in the same vocabulary) so a mismatch is a compile-or-diff away rather than an empty-dictionary away. Also: distinguish "absent" from "could not see yet" (`grasshopperReady`), because absence of evidence is not evidence of absence.
- **Last bit:** 2026-08-18

## Rhino agent: the UI thread you are on is the pump the SDK needs

- **Touching:** any new `RhinoAgent` handler that loads a plug-in, opens a document, or waits on app initialisation
- **Trace:** every handler runs inside `InvokeOnUi`. `PlugIn.LoadPlugIn(grasshopperGuid)` needs the message pump to finish bringing GH up — the pump the handler is already occupying — so it **deadlocked to the 300s RPC deadline**. A probe written to prevent a five-minute silence caused one (measured 344s). `RhinoApp.RunScript("_-Grasshopper _W _T ENTER")` works, because it returns to the pump; `Application.DoEvents()` + sleep is what lets a readiness poll progress. Also, `Canary.Agent.Rhino` **shadows the root `Rhino` namespace** — `Rhino.PlugIns` resolves to `Canary.Agent.Rhino.PlugIns` and needs `global::`.
- **Why:** a synchronous invoke cannot wait on anything that needs the thread it is holding. And any probe on the launch path must carry its OWN short timeout rather than inheriting the execute deadline — a precondition check that can cost minutes adds delay to the very failure it exists to shorten.
- **Last bit:** 2026-08-18
