---
date: 2026-07-24
tags: [bug, canary, ui, workload-editor, config, data-loss]
status: resolved
fix-commit: pending operator commit (found + fixed in the P4-review session)
project: canary
severity: high
component: "WorkloadConfig / WorkloadEditorViewModel / MainWindow.PersistAndRefreshAsync"
related: "found during the Qualia platform-foundation P4 deployed-web review (pre-existing; NOT part of the P4 diff)"
---

# 0018 — UI workload editor silently deletes qualiaConfig/penumbraConfig on Save

## Symptom

Open any browser workload (`workloads/qualia`, `workloads/qualia-web`,
`workloads/qualia-desktop`, `workloads/penumbra`) in the UI workload editor and
click Save: the entire `qualiaConfig`/`penumbraConfig` block vanishes from
`workload.json`. For qualia-web that loses `viteScript: "preview"`,
`vitePort: 4173`, `cdpPort: 9225`, `projectDir`; for qualia-desktop it loses
`desktop: true` and `appExePath`. The next run of that leg fails loudly, but
the config is destroyed and must be restored from git.

## Root cause

`src/Canary.Core/Config/WorkloadConfig.cs` models only the shared launch
fields and had no `[JsonExtensionData]` catch-all. The per-agent blocks are
deserialized SEPARATELY from the same file (`SessionAgentFactory` /
`RunCommand` / `TestRunnerViewModel` read `QualiaWorkloadConfig` /
`PenumbraWorkloadConfig` themselves), so the editor's POCO never saw them.
`WorkloadEditorViewModel.ToJson()` (:71) serializes the truncated POCO and
`MainWindow.PersistAndRefreshAsync` (WorkloadEditorViewModel branch, ~:182)
writes it straight over `workload.json` — a lossy read-modify-write.

## Fix

`[JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData`
on `WorkloadConfig`: every unmodeled top-level member (agent blocks + future
knobs) now round-trips Parse → editor → ToJson unchanged (entries append after
the known properties; `EditorJson.Options` is just `WriteIndented`, and a null
dictionary emits nothing, so freshly-created workloads serialize as before).

## Verification

- New unit tests (`[Trait("Category","Unit")]`) in
  `tests/Canary.Tests/UI.Avalonia/Editors/WorkloadEditorViewModelTests.cs`:
  - `RoundTrip_PreservesUnknownAgentConfigBlocks` — hermetic qualia-web
    replica incl. `viteScript`, `desktop`, `appExePath`, a `penumbraConfig`
    sibling, and an unknown scalar; all survive a real editor mutation.
  - `RealQualiaWebWorkload_SurvivesEditorSave` — loads the actual
    `workloads/qualia-web/workload.json` (repo-root discovered via
    `Canary.sln` walk-up) and asserts `qualiaConfig.viteScript == "preview"`,
    `vitePort 4173`, `cdpPort 9225`, `projectDir` survive `ToJson()`.
- Existing editor tests (idempotent round-trip, field population, empty-row
  filtering, save validation) stay green.
- `dotnet build Canary.sln` 0 errors 0 warnings; Unit category green.

## Trace note

CODE-TRACING-CHECKLIST gained a "Workload editor persistence" section: the
editor path is lossy by construction for anything the POCO doesn't model —
the catch-all is the only thing standing between a UI Save and destroyed
workload config.
