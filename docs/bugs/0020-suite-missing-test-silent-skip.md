---
title: "A suite-listed test whose JSON file is missing is warn-skipped — the suite still reports green"
date: 2026-07-24
tags: [bug, harness, test-discovery, false-green, coverage]
status: open
project: canary
component: TestDiscovery.DiscoverTestsForSuiteAsync
severity: medium
fix-commit: ""
found-during: "P4 fresh-session review (2026-07-24) — documented + spawned as a follow-up chip; filed as a durable bug post-campaign on operator instruction"
---

# Missing suite-listed test files silently shrink coverage

## Mechanism (grounded)

`TestDiscovery.DiscoverTestsForSuiteAsync`
(`src/Canary.Core/Orchestration/TestDiscovery.cs:~73-90`) resolves each
suite-listed test name to `workloads/<name>/tests/<name>.json`; a
missing file produces a console WARN and the test is skipped. The run
then executes the remaining tests and prints `Results: N passed, 0
failed` — no failure, no skipped-count in the results line, exit code 0.

## Failure scenario

A test file is renamed (or a suite entry typo'd) without updating the
suite JSON: the suite silently drops from, say, 9 tests to 8, every
battery line still reads green, and the dropped test's coverage —
possibly a frozen invariant or a parity tripwire — is gone until a
human notices the count. The platform-foundation batteries guard this
only by convention (humans pinning "9/9" / "7/7" in progress logs);
nothing mechanical fails.

## Fix shape (when picked up)

Treat a missing suite-listed test file as a suite FAILURE (or at
minimum: nonzero exit + `skipped: M` in the Results line so count-
pinning docs and the drift-watch skill can gate on it). Applies to all
workloads, not just qualia-family.
