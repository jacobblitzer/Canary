---
title: "One result-directory contract"
status: accepted
date: 2026-08-17
tags:
  - decision
  - results
  - baselines
---

# One result-directory contract

## Context and Problem Statement

Canary's result-directory derivation was split in two, and the split was invisible.

`RunSharedSuiteAsync` had **no `suiteName` parameter at all**, so it read
`results/<test>/`. `BaselineManager` wrote `results/<suite>/<test>/` whenever approval had
been given a suite. Approval and execution could therefore disagree about where a test's
approved pixels live.

That disagreement was silent, because a missing baseline yields `New` and `New` is excluded
from the exit code (`(Failed + Crashed) == 0 ? 0 : 1`). Nothing in that chain is a bug on
its own. Composed, they let the harness **report a pass while comparing nothing**.

Measured on 2026-08-17, before any change: **six suites** — penumbra `effects`,
`environment`, `materials`, `d1-lipschitz`, `d3-tricubic`, and qualia `playground` — held
**32 tests and 59 approved images with `reachable = 0`**. Every one of those suites ran
green. `runMode: shared` is the default for every test, so the artifact-less, unscoped path
was the common case, not an edge.

## Decision Drivers

* A green harness that asserts nothing is worse than no harness: it retires the question
  without answering it.
* Whatever is chosen must be **provable before it is applied** — a migration that silently
  orphans baselines reproduces the very defect being fixed.
* `.gitignore` excludes `results/`, so **no baseline has git recovery**. Nothing may be
  destroyed to get there.

## Considered Options

1. **Flat** — the evidence directory is a pure function of (workload, test).
2. **Suite-scoped everywhere** — every test's evidence lives under its suite.
3. **Keep both, with a documented fallback chain.**

## Decision Outcome

**Chosen: option 1, flat.** A test's evidence directory is a pure function of (workload,
test); it never contains a suite segment; a suite owns only its rollups (`report.html`,
`junit.xml`, `telemetry.ndjson`). `ResultPaths` is the sole derivation.

### Why not suite-scoped

The first analysis in this campaign argued *for* suite-scoping, on the evidence that five
tests carried differing baselines in both layouts — inferring **intent** from
**difference**. That inference was wrong, and the correction is the useful part:

* **No mechanism for that intent exists.** `SuiteDefinition` has four fields — `name`,
  `description`, `tests`, `keepOpen`. No capture geometry, no tolerance, no mode. A suite
  cannot make the same test render differently; capture geometry is per-*test*.
* **Content tracks the blessing date, not the suite.** `smoke` is uniformly 2026-04-25,
  `buyout-canonical` 2026-05-05, flat newest; same date ⇒ byte-identical, different date ⇒
  different bytes, at identical 960×540 with identical checkpoint name sets. If the scope
  encoded intent, content would track the scope.
* **`--test` and `--suite` are mutually exclusive** (`RunCommand.cs:282`), so a suite-scoped
  contract has no home at all for a solo run and would need an invented sentinel — two
  spellings under one name, which is the defect rather than the fix.
* **Cost:** 59 images move to flat; 227 would have been orphaned going the other way.

### Why not a fallback chain

`ApproveCommand` already had one, and it was actively dangerous: it blessed at whichever
layout it happened to find and returned success, which converts a half-applied migration
into a silent pass. It is deleted.

### Consequences

* **Good:** one derivation, enforced — `SingleResultDerivationTests` fails on any new
  `"results"` composition outside `ResultPaths.cs`. There were **14 such lines across 10
  files**; a unification that edited only the two helpers would have left the UI reading the
  old shape.
* **Good:** the shared path now writes per-run artifacts, so provenance has somewhere to go.
* **Bad, and accepted:** six suites that were silently green now report real failures. Their
  baselines are up to four months stale — the capture surface itself changed (the 2026-04-25
  penumbra baselines include the app's HUD and button bar; today's captures are
  geometry-only). Un-blinding a suite means seeing the drift that accumulated while nobody
  was looking. **Nothing was re-approved**: blind approval would launder that drift into the
  reference, which is the failure mode this whole change exists to prevent.
* **Bad, and accepted:** `LedgerLayout.Dual` survives as a legacy-read mode. It is what let
  the ledger be locked green under the pre-cutover code, which is the only reason the
  migration could be proven safe *before* it ran. Nothing on the run path calls it.

## How it was made safe

The order mattered more than the change:

1. **Snapshot** (`scripts/snapshot-baselines.ps1`) — 96 dirs, 322 PNGs, 198.23 MB, twice,
   with a SHA256 manifest verified against source. Mandatory: there is no git recovery, and
   the only other way back is re-approving whatever the code currently renders.
2. **Ledger** (`workloads/<w>/baselines.lock.json`) — git-tracked, keyed on **identity**
   rather than path, and locked **green under the old code**. That is what makes it the one
   guard independent of the thing being changed; every other candidate reads something
   inside the directory whose location is the variable.
3. **Migrate** (`scripts/migrate-result-layout.ps1`) — copy, never move. `verify --layout
   flat` reaching **174/174 while still running the pre-cutover code** was the proof the
   cutover could not orphan anything.
4. **Cut over** — one commit.

## Guards, and the mutation that proved each

* **Ledgered-but-absent ⇒ `Failed`, not `New`.** `Failed` is reused rather than adding a
  status, so the exit code, JUnit mapping and report badges need no new vocabulary.
* **The gate sits in the two dispatch funnels, above the mode loop — the placement is the
  guard.** The obvious home, inside `if (!File.Exists(baselinePath))`, is unreachable for
  the case that matters: the `mode == Capture` early-return sets `Passed` and returns
  *before* `baselinePath` is computed. A one-word JSON edit would otherwise disarm a
  ledgered comparison with the approved image still on disk.
* **A content disarm fails; a `--mode` flag does not.** Content changes are permanent and
  silent; a flag is one operator asking for one run.
* **Fail closed: an absent ledger is not an empty ledger.** A workload arming nothing
  carries a committed `"rows": []`. The counter-mutation must stay green, or the guard
  becomes too strict to live with and gets switched off.
* **`--expect-rows` is mandatory when locking.** Locking with the post-cutover resolver
  yields 40 penumbra rows instead of 93, and `verify` is *green* on that truncated ledger
  because every row it contains resolves. The count is the only tell.

Mutation-proving repeatedly earned its keep. Reverting the completeness gate fired only
**one of three** guards — one test was passing for an unrelated reason and would have passed
with the gate deleted. Breaking `LoadRequired` exposed that **nothing covered doctor's
fail-closed path**.

## Known deviation from STANDARD.md §16

§16 requires baselines tracked in git, next to the test definition, named `<test-id>.png`.
Canary violates this for all 322 images, and `spec/PEERS.md` told four peer repos to look in
`workloads/<workload>/baselines/` — a directory that does not exist and never has. That is
corrected there.

Full compliance is **deferred and needs a MultiVerse ruling first**: §16's naming rule is one
image per test, which does not fit a per-checkpoint model. The ledger is the
§16-compatible half available now — it puts the *contract* in git at ~30 KB with no image
movement.
