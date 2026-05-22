---
date: 2026-05-03
status: shipped
source: extracted from Canary/CLAUDE.md per STANDARD.md §3 + §19
---

# Penumbra display-preset workload (Phase 5 shipped 2026-05-03)


`TestSetup.DisplayPreset` (string, optional) wires named Penumbra
`DisplayState` presets into the test harness. When set, `TestRunner`
dispatches `LoadDisplayPreset` on the agent before the first checkpoint;
`PenumbraBridgeAgent` evaluates `pass.loadDisplayPreset(name)` (or
`renderer.loadDisplayPreset(name)` in Studio mode) via CDP and records
the resulting `displayMode` / `atomMode` / `vizMode` in the run log for
repro. Unknown preset names log a warning + no-op rather than crash the
run.

8 preset-driven tests under `workloads/penumbra/tests/preset-*.json`,
two suites: `display-smoke` (3-test fail-fast subset for CI) and
`display-matrix` (full 8-preset sweep). Both modes (`--mode pixel-diff`
and `--mode vlm`) supported; per-preset `vlmDescription` baked into
every test.

Run:
```
canary run --workload penumbra --suite display-matrix
canary run --workload penumbra --suite display-smoke --mode vlm
```

See `spec/PENUMBRA_WORKLOAD.md` for the contract and Penumbra ADR 0011
for the parent design.
