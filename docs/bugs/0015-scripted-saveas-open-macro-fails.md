---
date: 2026-07-03
tags: [bug, canary, rhino, test-authoring, agent]
status: open
project: canary
severity: medium
component: "Canary.Agent.Rhino RunCommand / rhino macros"
---

# 0015 — scripted `-_SaveAs`/`-_Open <path> _Enter` macro fails ("Cancel"), crashing the save/reopen tests

## Symptom

`cpig-bool-refactor-13-save-reopen-composite-frommesh` crashes with
`Action 'RunCommand' failed: Command failed: -_SaveAs C:/…/canary_cpig_bug0021_test13.3dm _Enter`.
Telemetry shows the command resolving as `SaveAs result=Cancel durationMs≈2`. Same failure on
`-_Open <path> _Enter`.

## Root cause: the macro form, NOT persistence (proven by control experiment)

Discovered during R2.4 (CPig.Core recipe move) verification and initially suspected as an R2.4
persistence-contract regression (`CPigFieldData.Write` throwing over the relocated
`CPigRecipeNode` during `.3dm` save). **Ruled out with a control:** a probe test running
`-_SaveAs <temppath> _Enter` on an **empty document** (no CPig field, no UserData serialization)
fails identically. The restore/serialize code is never reached — the `-_SaveAs`/`-_Open` scripted
macro form itself resolves to `Cancel` in the headless Canary agent, regardless of document
content, and regardless of whether the target file pre-exists (temp cleared → still fails).

So R2.4 is cleared: the recipe move's persistence path is separately verified inert (relocate A/B
per-part hashes identical 5/5; the `CPig.Core.Tests` VdbBytes round-trip exercises
`RestoreFieldFromVdbBytes` headlessly; `CPigRecipeNode` shape + chunk v3.0 are byte-verbatim).

## Impact

- `cpig-bool-refactor-13` (the within-session save/reopen composite guard) cannot pass headless.
- Blocks authoring a cross-generation pre-split `.3dm` reopen gate (attempted in R2.4 as
  `cpig-ab-r2-presplit-reopen`, removed because `-_Open` hits the same wall).
- Any future test needing scripted file save/open is affected.

## Fix direction (needs investigation — not attempted in R2)

Determine why `RhinoApp.RunScript("-_SaveAs <path> _Enter", …)` returns Cancel headlessly:
likely the scripted `-_SaveAs` expects the path as a quoted string token, or the dash-form needs
a different token sequence (a version/overwrite/units sub-prompt the trailing `_Enter` doesn't
answer). Candidates: quote the path (`-_SaveAs "C:\…\f.3dm" _Enter`), use backslashes, or add the
missing sub-prompt answer. Verify by driving one committed save/reopen test to green. Whether
test 13 ever passed **headless** (vs attended UI) needs git-archaeology — it may have only ever
been validated attended.
