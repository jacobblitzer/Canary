---
date: 2026-06-09
tags: [feature, canary, rhino]
status: accepted
project: canary
component: rhino-workload
---

# Rhino command macros in test `setup.commands`

Reference for authoring the `setup.commands` strings in a Rhino-workload test JSON
(`workloads/rhino/tests/*.json`). Hard-won — a malformed macro hangs the whole test with a
confusing crash that looks like a Canary or Grasshopper bug but is actually the Rhino command
line stuck waiting for input.

## How they run

Each string in `setup.commands` is dispatched as the agent action `RunCommand`, which calls
`RhinoApp.RunScript(command, echo:false)` inside Rhino (`Canary.Agent.Rhino/RhinoAgent.cs`,
`TestRunner` setup loop). It runs **after** the fixture opens and **before** the test's
`actions`. So whatever doc state you set here is in effect when the Slop graph builds.

## The failure mode (read this first)

If the macro does **not** fully exit its command, Rhino is left sitting at a sub-prompt
waiting for input. The harness's next pipe request is never serviced, and after a timeout you
get:

```
Status: Crashed — "Pipe disconnected while sending request."
```

No checkpoints, no Slop log, no crash dump — because nothing past setup ever ran. **This is
almost always a bad macro, not a Canary or Grasshopper fault.** Symptom on screen: the Rhino
command line is parked mid-command and the run won't advance.

## Enter handling (the part everyone gets wrong)

- A **space** between tokens acts as **Enter**.
- **`_Enter`** is an explicit single Enter.
- **`_EnterEnd`** backs **all the way out** of a deep nested command (Options /
  DocumentProperties) to a blank `Command:` prompt — *without* having to count how many levels
  deep you are. **Prefer `_EnterEnd`.** Hand-counting `_Enter`s breaks the moment a
  **conditional** prompt appears or doesn't (e.g. "scale model by 0.1?", which shows only when
  the unit actually changes) — one off-by-one and the command hangs.

Other rules:
- **Include every sub-option keyword.** Skipping one feeds your value to the wrong menu and
  hangs. (The classic bug: writing `… _ModelUnits _Centimeters …` and omitting `_UnitSystem`,
  so `_Centimeters` is handed to the "model units and tolerances" menu, which has no such
  option.)
- Use the `_` prefix on keywords (`_Units`, `_Centimeters`) so the macro is locale-independent.
- Lead with `-` (e.g. `-_DocumentProperties`) to force the command-line version instead of the
  modal dialog. A modal dialog blocks the pipe → same hang.
- Quote paths/names that contain spaces; otherwise a space is read as Enter.

## How to verify a macro fast

Paste the macro **straight into Rhino's command line** and press Enter once. Spaces are
treated as Enters exactly as `RunScript` does, so this reproduces Canary's behavior. It's
correct iff it returns to a **blank `Command:` prompt** (not parked at a sub-prompt) and the
intended state changed.

## Known-good recipes

Set model units to **centimeters** (used by the `kbridge-*` tests; tested 2026-06-09):

```
-_DocumentProperties _Units _ModelUnits _UnitSystem _Centimeters _EnterEnd
```

`_EnterEnd` exits through the model-units menu, "Units to edit", the conditional
"scale by 0.1?" Yes/No (accepts default), and the document-property menu in one token.

Pattern for any DocumentProperties tweak: `-_DocumentProperties <section> <…sub-options…> _EnterEnd`.
(McNeel's own example: `-_DocumentProperties _Mesh _Custom _MaxEdgeSrf .01 _EnterEnd`.)

## Why units matter for the Inventor↔CPig overlay

KinematicImporter is **cm-native** (it scales transforms but not its imported STEP geometry,
so it only renders correctly at doc = cm). CPig's `Preview Diagram` is now **unit-aware**
(CPig ADR 0005), so it matches at cm too. Hence the kbridge tests force **doc = cm** via the
recipe above.

## Sources

- [Rhino 8 scripting / command macros](http://docs.mcneel.com/rhino/8/help/en-us/information/rhinoscripting.htm)
- [Creating Macros wiki](https://wiki.mcneel.com/rhino/basicmacros)
- [developer.rhino3d — Creating Command Macros](https://developer.rhino3d.com/guides/general/creating-command-macros/)
