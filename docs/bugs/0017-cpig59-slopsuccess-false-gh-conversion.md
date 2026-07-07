---
date: 2026-07-07
tags: [bug, canary, slop, cpig, field-point-cloud, type-conversion, gh-error-balloon]
status: open
project: cpig
severity: medium
component: "CPig Slop test 59_field_point_cloud_r6.json"
related: "Canary bug 0016 (resolved — timeout infra now configurable)"
---

# 0017 — cpig-59-field-point-cloud-r6 SlopSuccess=False (GH type-conversion / mysterious "breakpoint")

## Symptom

`cpig-59-field-point-cloud-r6` returns `SlopSuccess=False` when run through Canary (GUI-open, ~24s). The test does NOT crash or hang anymore (Canary bug 0016 timeout fixes resolved that). The Slop loader builds the graph but a component fails during GH solution, causing the `SlopSuccess` panel to read "False".

**CRITICAL GAP:** The actual error text has not been captured. The operator sees a GH error balloon ("breakpoint"-like — a Grasshopper-specific UI that needs visual inspection to understand). The `SlopLog` panel content has not been read. All debugging so far has been guessing at the cause instead of reading the actual error.

## What the operator has told us

1. "theres more info surfaced by the breakpoint than what i see here" — there is a Grasshopper error balloon showing real diagnostic info that we are NOT surfacing through Canary.
2. "the breakpoint is a weird grasshopper thing, that you do actually need to kind of see to understand" — this is a GH-specific error display behavior, not a Debugger.Break or a native crash.
3. Earlier hint: "the supplied data could not be converted: parameter type L GH_Point Supplied type: Pointcloud" — this was the error on a PREVIOUS version of the test (before Custom Preview was removed). The current error may be different.
4. "i think potentially you don't understand slop that well, and slop does need to be improved" — part of R6.5 Phase C/D is improving Slop's component grounding so agents understand what fields/components actually do and how to wire them.

## Investigation so far

### What was tried (and why it was wrong)
- **Bug 0016 timeout fixes** (6 commits): made all three hard-coded timeouts configurable (`ExecuteTimeoutMs` in `WorkloadConfig`, `TestRunner.cs:152/524`, `RhinoAgent.InvokeOnUi`). This was correct work (the timeouts WERE too short and hard-coded in three places), but it didn't fix the test — it just made the hang not happen. The real issue is the GH solution fails, not that it's slow.
- **Crash capture infrastructure** (3 commits): `VectoredExceptionHandler`, `AppDomain.UnhandledException`, `FirstChanceException`, `SEM_NOGPFAULTERRORBOX`, crash log at `%TEMP%/Canary/canary-crash.log`. **No crash log was ever written** — the test doesn't crash, it fails gracefully. This infrastructure is still valuable for future native faults but was NOT the fix here.
- **Slop test fixes** (4 commits): removed Custom Preview (PointCloud→GH_Point conversion), lowered octree depth 7→5, wired Mesh Sphere base plane, removed duplicate `p_rep` node. The Custom Preview removal was correct (the conversion error was real), but the test STILL fails after all of these.

### What we still need
- **READ THE SLOPLOG.** The `SlopLog` panel in `cpig_slop_loader.gh` contains the actual error. The Canary test asserts it doesn't contain "FATAL"/"!!!" but never dumps its content. The operator sees a GH error balloon that we haven't captured.
- **See the GH error balloon.** The operator says this is a "weird Grasshopper thing" that needs visual inspection. The next session MUST have vision capabilities to see the balloon.

## Next steps for the new session

1. **Get vision working first** (before touching anything else). The operator will reset with a vision-capable auxiliary model.
2. **Read the SlopLog panel** — either by capturing a screenshot of the GH canvas after the test runs, or by modifying the Canary test to dump the `SlopLog` panel content to a file and reading it.
3. **Look at the GH error balloon** — screenshot the Rhino/GH window after `Build` fires. The balloon shows the actual component-level error.
4. **Fix the root cause** — whatever the SlopLog/balloon says. May be a wire issue, a missing input, a component GUID mismatch, or a genuine CPig component bug in `FieldPointCloudExtractor`.
5. **Consider reverting** if the crash-capture / timeout commits added complexity without value. The operator said "reverting if needed." Evaluate whether the 6+ Canary commits are worth keeping or should be reverted to a simpler state.

## Relevant files

- `C:\Repos\CPig\research\slop_tests\59_field_point_cloud_r6.json` — the Slop test definition (current version)
- `C:\Repos\Canary\workloads\rhino\tests\cpig-59-field-point-cloud-r6.json` — the Canary wrapper
- `C:\Repos\Canary\workloads\rhino\fixtures\cpig_slop_loader.gh` — the loader fixture (has SlopLog + SlopSuccess panels)
- `C:\Repos\CPig\CPig.Grasshopper\Components\Implicit\CPig_FieldPointCloud.cs` — the thinned GH component (shim)
- `C:\Repos\CPig\CPig.Interop\FieldPointCloudExtractor.cs` — the extracted octree algorithm (R6 Phase B)
- `C:\Repos\Canary\docs\bugs\0016-headless-agent-session-pipe-timeout.md` — resolved (timeout infra)

## Lessons (for the next session — read these before debugging)

1. **Read the error first.** Don't build infrastructure (crash capture, timeout fixes) before reading what the actual error says. The SlopLog panel and GH error balloon have the answer.
2. **Don't do round trips without checking in.** The operator flagged that I went several tool-call rounds without reporting back. Pause and summarize after each meaningful step.
3. **You don't understand Slop deeply yet.** R6.5 Phase C/D will improve this. For now, lean on the operator's knowledge and the existing passing tests (cpig-58, etc.) as patterns.
4. **The "breakpoint" is a GH error balloon**, not a Debugger.Break. It's a Grasshopper-specific UI artifact. Visual inspection is required to understand it.
5. **Verify deploy chain before running.** Kill all `Canary.UI.exe` + `Rhino.exe`, rebuild with `--no-incremental`, verify `.rhp` + `.dll` timestamps, THEN run. The `DeployToRhino` MSBuild target auto-deploys to `%APPDATA%/McNeel/Rhinoceros/8.0/Plug-ins/Canary (B4E7C920-...)/`.