---
date: YYYY-MM-DD
id: YYYY-MM-DD-NNN-<3-to-5-word-slug>   # save the file under this name too - the
                                        # importer files by file name, so a name that
                                        # does not match the id makes a collision
                                        # report meaningless
status: open           # open | triaged | resolved
project: canary        # canary | qualia | penumbra | rhino | cross-repo
urgency: normal        # low | normal | high
tags: [feedback, qc]
# --- provenance: which machine produced this ---------------------------------
# A report that cannot say which machine, which Canary and which tier produced it
# is not evidence. Copy every one of them out of the bundle, never from memory:
#   machine / tier / canaryVersion  <- commissioning-report.json, the machine block
#                                      (machineName, tier, canaryBuild)
#   commissionExit                  <- qc-summary.json, commissionExit
#   doctorExit + workload           <- qc-summary.json, the workloads entry
# Leaving one blank because it "was obviously that box" is how a stale finding gets
# trusted a month later on a machine it was never true of.
machine: ""            # e.g. BOOK-4IBO7G77D6
tier: ""               # DEV | QC | USER - derived from observable facts, never hand-set
canaryVersion: ""      # e.g. 0.9.0 (42122b0)
commissionExit: null   # 0 = harness proven on that machine; 4 = commissioning failed
doctorExit: null       # 0 = install complete for the workload named below
workload: ""           # which workload doctorExit belongs to
# --- optional: only when this learning points at a specific run --------------
runRef: ""
checkpointRef: ""
imageRef: ""
---

# [One line, in the terms the QC machine saw it - not the diagnosis]

## Signal

Which of the three fired, and which stayed green. Fill this from exit codes, not from
impressions: the codes are separate precisely so that one failure cannot be mistaken for
another, and prose written at a machine under stress is where that distinction dies.

| Signal | This bundle | Meaning if red |
|---|---|---|
| commissioning | [exit N] | the harness is broken - **every result below is unreadable** |
| `doctor` | [exit N] | the install is incomplete - **not** a defect in the plug-in |
| the suite | [pass / fail / NotRun] | a real finding **only** when both rows above are green |

[Say plainly which of the three combinations this is. If a layer or a suite was never
attempted, write NotRun and say why it was not attempted - NotRun is never a pass.]

## Observed

Exit code first, then the lines themselves, quoted verbatim. Never summarised: a paraphrase
of an error is a second-hand report of the one thing that was first-hand.

```
[paste from the bundle: commissioning.txt, <workload>.doctor.txt, <workload>.env.txt]
```

## What this means for the dev machine

[Is this a property of that machine, or of the content everyone shares? A viewport size that
differs is the first; a token left literal in a shipped workload is the second, and only the
second is fixed here. If it is a machine property, say what the dev machine would have to
change to reproduce it at all.]

## Proposed fix

[Concrete, and named at a file. If the honest answer is that the fix cannot be chosen without
something the QC machine could not measure, say that here and put the missing measurement in
Undetermined rather than guessing.]

## Undetermined

[What could not be established there, and WHY - no repo, no SDK, the app never launched, the
plug-in was not installed. This section is the one that stops a later session re-deriving a
question that was already asked and already found unanswerable at that machine.]

## Related

- Bundle: `qc-<COMPUTERNAME>-<yyyyMMdd>` under `G:\My Drive\claude-share\`
- Imported by: `powershell -File scripts/import-qc-bundle.ps1 <bundlePath>`
- Environment diff: `canary env --workload <w> --diff <bundle>/<w>.environment.json`
