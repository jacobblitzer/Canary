---
date: 2026-06-10
tags: [feature, canary, ui]
status: open
project: canary
component: "ui-test-runner"
source: operator (verbal, via kbridge cowork session)
---

# Feature desire — a window showing previous test runs as a log

Operator request (2026-06-10, logged verbatim-intent during KinematicBridge cowork):

> "I want there to be a window that has previous test runs as a log."

A persistent, log-style view of prior runs — chronological, scannable, presumably
per-test and/or global — visible while working, rather than navigating away. Note for
the implementing session: a **Past Runs** nav tab already exists (debug-overhaul Phase
3); the operator's ask reads as wanting a *log-style window* of run history (e.g. a
dockable/always-visible pane listing recent runs with verdicts/timestamps), so clarify
with the operator whether this is an enhancement of the Past Runs tab or a separate
companion panel before building. Run data already exists on disk per run at
`workloads/<w>/results/[<suite>/]<test>/runs/<stamp>/result.json` — this is a UI
surfacing ask, not a data-model one.

Not scheduled — operator explicitly deferred ("i dont want to open a bunch of canary
work at the moment"). Log only.
