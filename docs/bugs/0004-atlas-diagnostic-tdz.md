---
title: "atlas-diagnostic.ts temporal dead zone error"
date: 2026-04-24
tags:
  - bug
  - penumbra
  - diagnostics
status: resolved
project: penumbra
component: diagnostics
severity: medium
fix-commit: ""
---

# atlas-diagnostic.ts temporal dead zone error

## Summary
`atlas-diagnostic.ts` threw a ReferenceError at runtime due to variables being referenced before their declaration.

## Environment
- OS: Windows 11
- Penumbra: main branch

## Root Cause
`center` and `extent` were declared with `const` at line 208 but referenced earlier at line 158. JavaScript's temporal dead zone (TDZ) means `const`/`let` variables exist in scope from the start of the block but cannot be accessed before their declaration.

## Steps to Reproduce
1. Call `runAtlasDiagnostic()` from console or via CDP
2. Observe: `ReferenceError: Cannot access 'center' before initialization`

## Fix
Moved the `center` and `extent` declarations above their first use (before line 158).

## Verification
- `runAtlasDiagnostic()` runs to completion without errors
- Diagnostic output correctly reports CPU/GPU SDF comparison

## Related
- File changed: `atlas-diagnostic.ts` (Penumbra repo)
