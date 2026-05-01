---
title: "Register overflow in 64-sphere stress test"
date: 2026-04-24
tags:
  - bug
  - penumbra
  - tape-compiler
status: resolved
project: penumbra
component: core/compiler
severity: critical
fix-commit: ""
---

# Register overflow in 64-sphere stress test

## Summary
The 64-sphere smooth-union stress test produced incorrect SDF values on GPU. CPU/GPU delta was consistent across all sampled cells, pointing to a systematic error rather than floating-point drift.

## Environment
- OS: Windows 11
- Penumbra: main branch, WebGPU backend
- Scene: 64-sphere stress test (scene index 3)

## Root Cause
`TapeCompiler.compileBoolean()` used a bump allocator that **never recycled registers**. The 64-sphere smooth-union needed registers 3 through 129, but the GPU shader's register file was declared as `array<f32, 32>`. Writes to registers beyond index 31 wrapped or were silently dropped.

## Steps to Reproduce
1. Load 64-sphere stress test scene
2. Run atlas diagnostic readback (`runAtlasDiagnostic()`)
3. Compare CPU vs GPU SDF values — observe consistent delta

## Fix
Two changes:
1. **Register recycling in boolean left-fold** -- reuse `accumReg` as destination register, reclaim child registers via `freeRegister()` after each iteration
2. **Increased MAX_REGISTERS from 32 to 64** in WGSL tape evaluator (`tape-eval.wgsl`) as a safety margin

## Verification
- Atlas diagnostic readback shows CPU/GPU delta < 0.001
- Canary stress-test-orbit visual regression test passes

## Related
- Files changed: `packages/core/src/compiler.ts`, `packages/shaders/src/wgsl/tape-eval.wgsl` (Penumbra repo)
- Diagnostic: `runAtlasDiagnostic()` readback tool
