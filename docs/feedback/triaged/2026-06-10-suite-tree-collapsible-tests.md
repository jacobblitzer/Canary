---
date: 2026-06-10
tags: [feature, canary, ui]
status: open
project: canary
component: "ui-test-tree"
source: operator (verbal, via kbridge cowork session)
---

# Feature desire — suites show their nested tests collapsed/expandable underneath

Operator request (2026-06-10, logged verbatim-intent during KinematicBridge cowork):

> "For test suites, the nested tests that are in that suite [should] be collapsed,
> expandable underneath."

In the Tests tree, a suite node should list its member tests as a collapsed,
expandable child group (chevron/twisty), instead of whatever flat/duplicated
presentation exists today. Suite membership is already declared in
`workloads/<w>/suites/<suite>.json` (`"tests": [...]`) — this is a tree-view UI ask.
Motivating context: the `kbridge` suite has grown to 10 tests and scanning it flat is
getting unwieldy.

Not scheduled — operator explicitly deferred ("i dont want to open a bunch of canary
work at the moment"). Log only.
