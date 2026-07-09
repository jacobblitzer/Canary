---
id: 0001
peer: rhino
status: open
requested: 2026-05-24
severity: normal
tags: [docs, spec, rollout]
---

# Create Rhino/spec/CANARY.md mirroring CPig's pattern

## Context

The `spec/CANARY.md` convention was rolled out on 2026-05-24 to Penumbra and Qualia, mirroring the existing CPig pattern at `CPig/spec/CANARY.md`. Rhino is the fourth peer of Canary's `rhino` workload (alongside CPig and Pigture; Slop is the infrastructure for the workload itself, not a tested consumer).

The MultiVerse coordination-pattern rollout (`MultiVerse/prompts/multiverse-canary-coordination-pattern-2026-05-24.md`) was unable to land Rhino's `spec/CANARY.md` because the Rhino repo isn't checked out locally on the operator's machine — it may be a separate-org repo, hosted elsewhere, or just not synced here. This ask captures the work for the next session that has Rhino-side access.

## Shape of the answer

A new file at `Rhino/spec/CANARY.md` mirroring CPig's structure. The 7 sections:

1. **Purpose** — what Canary tests for Rhino; cross-link to `Canary/spec/PEERS.md` (if it has a Rhino section) or to `Canary/spec/CPIG_WORKLOAD.md` / `Canary/spec/PIGTURE_WORKLOAD.md` (the two workloads that exercise Rhino in practice).
2. **Architecture** — ASCII diagram: Canary harness → JSON-RPC over named pipe → Canary.Agent.Rhino plugin + Grasshopper fixture loader (`cpig_slop_loader.gh`, `pigture_slop_loader.gh`). Note that there isn't a Rhino-specific test suite per se — `rhino` is the workload that hosts `cpig-*` and `pigture-*` suites.
3. **Test discovery** — `rhino-*` suites layer on top (cpig-*, pigture-*, future pigment-*); see `CPig/spec/CANARY.md` for cpig conventions and `Pigture/spec/CANARY.md` for pigture conventions (if/when it exists).
4. **Running tests** — bash blocks for `canary run --workload rhino --test cpig-NN-slug`, `canary run --workload rhino --filter "pigture-*"`, `canary baseline approve <test-id>`.
5. **Workflow when fixing a Rhino bug** — reproduce locally in Rhino + Grasshopper; land fix in the relevant plugin (`CPig.Grasshopper.gha` or Pigture); `canary baseline approve` after deliberate visual change. Include the agent actions `GrasshopperSetPanelText`, `GrasshopperSetToggle`, `GrasshopperGetPanelText` defined in `C:\Repos\Canary\src\Canary.Agent.Rhino\RhinoAgent.cs`.
6. **Adding a new test** — depends on whether the test belongs to a cpig-* or pigture-* suite; cross-reference the relevant peer's CANARY.md.
7. **Cross-references** — to `CPig/spec/CANARY.md`, `Pigture/spec/CANARY.md` (if exists), `Canary/spec/PEERS.md`, `Canary/spec/CPIG_WORKLOAD.md`, `Canary/spec/PIGTURE_WORKLOAD.md`, `Slop/SLOP_STYLE.md` (test JSON authoring conventions).

**Reference template:** `CPig/spec/CANARY.md` (the gold-standard structure all peer `spec/CANARY.md` files mirror). The Penumbra + Qualia files landed in the 2026-05-24 rollout are also good adaptation examples.

After creating the file, also:
- Add a cross-link from `Rhino/AGENTS.md` to the new `spec/CANARY.md` (e.g. in the spec files list or a "See also" line wherever the spec/ index lives).
- Update this ask file (`Canary/docs/asks/rhino/0001-create-canary-md.md`) to `status: landed` with `landed: <date>` and append a `### Resolution` section pointing at the commits.
- Append a `MultiVerse/BUILD_LOG.md` cross-repo entry per STANDARD.md §7.

## What Canary will do once the peer lands it

No Canary-side change required — the file is a Rhino-side doc that mirrors the spec/CANARY.md pattern already present in CPig + Penumbra + Qualia. The MultiVerse coordination-pattern rollout will be complete once this lands; from then on, asks filed at `Canary/docs/asks/rhino/<NNNN>-slug.md` have a documented peer-side workflow to land them via.

## Open questions

- Does `C:\Repos\Rhino\` exist on a different machine but not the operator's current host? Operator: confirm whether this needs syncing or if the work should be executed on the machine that has Rhino checked out.
- Is Rhino's `spec/` structure compatible with the CPig template (sections, naming)? If Rhino has its own spec/ conventions, the CANARY.md should adapt to those rather than rigidly mirroring CPig.
- Are there Rhino-only tests that don't belong to `cpig-*` / `pigture-*`? If yes, the test-discovery section should describe their naming convention too.
