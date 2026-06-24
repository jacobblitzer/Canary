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

## WaitForPenumbraFrame (reflection contract)

- **Touching:** `Canary.Agent.Rhino` `WaitForPenumbraFrame` action
- **Trace:** reads `PenumbraBridge.GetFrameState()` via REFLECTION on public fields: `RealRevision`, `PresentedRevision`, `EvalMode`, `Status`, `DisabledByError`, `BakeLevel` (Phase 5 v1.1 addition). RENAMING any field on Penumbra's side silently breaks Canary at next test run — no compile error. Cross-repo contract listed in `Penumbra/spec/PEERS.md` (and Penumbra-perf equivalent).
- **Why:** silent breakage. Renames need a coordinated cross-repo commit.
- **Last bit:** 2026-06-24

## AppLauncher env auto-resolve

- **Touching:** `src/Canary.Agent.Common/AppLauncher.cs` or any env-var Canary spawns child processes with
- **Trace:** `AppLauncher.Launch` auto-resolves `PENUMBRA_HOST_DEV` / `PENUMBRA_USE_NATIVE_DLL` / `PENUMBRA_PIPELINE_CACHE_DIR` from User-scope registry into the spawned process env. This bypasses Canary.UI's inherited env (which may be stale from when Canary.UI started). Opt-out: `CANARY_USE_INHERITED_PENUMBRA_ENV=1`. Bug 0057's lesson — env-var inheritance in Windows.
- **Why:** without auto-resolve, Canary tests run with whatever env Canary.UI started with — typically stale.
- **Last bit:** 2026-06-24

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
