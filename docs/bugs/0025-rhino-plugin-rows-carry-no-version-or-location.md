---
title: rhino: plug-in rows carry no version and no location, so origin is always Unknown
status: open
severity: low
found: 2026-08-19
area: agent / environment capture
---

# 0025 — `rhino:` rows have no version and no location

## The defect

`RhinoAgent` emits Grasshopper libraries and Rhino plug-ins into the same `loaded` list, but
with different amounts of information:

- Grasshopper: `gh:<Name>=<version>@<location>`
- Rhino: `rhino:<Name>=loaded`

`EnvironmentReport.ParseLoaded` splits on `=` then `@`. With no `@`, a Rhino row parses as
`Version = "loaded"`, `Location = ""`, and `PluginOrigins.Classify("")` returns
`PluginOrigin.Unknown`.

## What it costs

Three things, all quiet:

- **The Pretest tab shows `loaded` in a Version column** for `rhino:CPig.Rhino` — the
  requirement added to 59 tests on 2026-08-18.
- **`canary env --diff` cannot compare Rhino plug-ins meaningfully.** Version skew between
  two machines is invisible, because both sides read `loaded`.
- **Origin-shadowing is undetectable for Rhino plug-ins.** A `.rhp` loaded from a build
  output and one from a package look identical, so the check that matters most for QC — where
  did this actually come from — cannot answer for that half of the surface.

The last one is the real cost, and it interacts with bug 0024: even once requirements start
pinning an origin, a `rhino:` pin could never be satisfied because the actual origin is
always `Unknown`.

## Why it has not bitten yet

Only one `rhino:` requirement exists in the corpus (`rhino:CPig.Rhino`), added recently, and
`PluginOrigins.Satisfies` returns true for an unrecognised or absent pin — so nothing fails.
It would begin to matter the moment anyone pins an origin on a Rhino plug-in.

## What would close it

Emit the same shape for both: `rhino:<Name>=<version>@<location>`. `PlugIn` exposes both —
the plug-in's assembly location and its version — so this is an agent-side change plus a
re-capture. `ParseLoaded` already handles the fuller form, so nothing downstream changes.

Worth doing together with 0024, since neither is much use alone.
