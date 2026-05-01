---
title: "canary-penumbra staging area"
date: 2026-04-24
tags:
  - penumbra
  - integration
---

# canary-penumbra/ — Penumbra Integration Staging

This directory was used to develop the Penumbra CDP bridge integration. Some contents have been consolidated into canonical locations.

## Canonical Locations

| Content | Canonical Location | Notes |
|---------|-------------------|-------|
| Test definitions | `workloads/penumbra/tests/` | **Use this**, not `canary-penumbra/workloads/` |
| workload.json | `workloads/penumbra/workload.json` | **Use this** |
| AGENT_NOTES.md | `workloads/penumbra/AGENT_NOTES.md` | **Use this** |
| CLAUDE_CODE_RUNNER.md | `workloads/penumbra/CLAUDE_CODE_RUNNER.md` | **Use this** |
| ADR for CDP decision | `docs/decisions/0002-cdp-bridge-for-penumbra.md` | Summary of architecture decision |

## Still Active Here

| Content | Notes |
|---------|-------|
| `PENUMBRA_CANARY_SPEC.md` | Full integration spec (referenced by ADR 0002) |
| `INTEGRATION_GUIDE.md` | Step-by-step integration guide |
| `penumbra-hooks/canary-hooks.ts` | Penumbra-side hooks for test/main.ts |
| `src/Canary.Agent.Penumbra/` | **Newer** version with `InitializeFromExistingAsync`, `LoadSceneByName` |
| `src/Canary.Core/Cdp/` | CDP client code (may diverge from `src/Canary.Core/Cdp/`) |

## Code Divergence Warning

`canary-penumbra/src/` contains a **newer** version of `PenumbraBridgeAgent.cs` than `src/Canary.Agent.Penumbra/`. Key additions:
- `InitializeFromExistingAsync()` — connect to already-running Penumbra
- `LoadSceneByName` action
- `autostart=true` query param on navigation URL

These should be merged into `src/Canary.Agent.Penumbra/` when ready.
