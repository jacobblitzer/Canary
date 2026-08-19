---
title: Qualia workload — what is out of date, 2026-08-18
status: current
kind: audit
---

# Qualia workload — what is out of date

Audited 2026-08-18 at the operator's request ("the qualia tests may be old… may be worth it
to clean out some tests rather than try and fix these"). Every claim here was checked against
Qualia's live source, not inferred from test names.

## The headline: the corpus is not stale. It is unapproved.

- **Hook coverage is 109/109 clean.** 69 distinct `window.__canary*` hooks are called; 68
  exist in Qualia today. The one apparent exception, `__canaryRetriggerEagerSweep`, appears
  only inside a *description* paragraph and is never called — and was never *implemented*,
  so it was never "removed" either.
- **Every display profile referenced by a test still exists.** 12 of Qualia's 17 are
  referenced; `mobile`, `laser-rat`, `circuit`, `x-ray` and `bioluminescent` are referenced
  by nothing.
- **102 of 109 tests have no approved baseline** (91 of the 98 that parse; 49 of 56
  pixel-diff checkpoints). They render `New`, which is excluded from the exit code. That is
  the real problem: most of this workload passes while comparing nothing.

So age is not the defect. Two narrower things are.

## 1. Dead module ids — 4 tests (CORRECTED 2026-08-18)

> **This section was wrong when first written, and the error disarmed ten working tests.**
> It claimed Qualia's registry held **14** ids and that **8** were dead. The registry
> (`packages/core/src/modules/builtin.ts`) holds **51**, and only **3** are dead.
>
> Cause: the live-id list was built with the regex `id:\s*'[a-z]+\.[a-zA-Z.]+'`. That
> character class cannot match a **hyphen**, so every hyphenated id — `fx.pencil-toon`,
> `fx.laser-rat`, `compute.rag.eager-l3`, `fx.debug-layer-colors`,
> `render.penumbra-backdrop` — failed to match and fell silently out of the "live" set. The
> 14 found were exactly the 14 hyphen-free ids. Absence was then read off that filtered view.
>
> Ten tests were consequently marked stale and forced to `mode: "capture"` — save-only,
> never failing — while their toggles were live. That is a fresh instance of the
> silent-green defect this campaign exists to remove. All ten have been restored from
> `d950c02^`. Found by adversarial review, not by the author.

Three module ids are genuinely gone, removed 2026-07-19 and now referenced only in comments:
**`render.nodes`**, **`render.edges`**, **`render.labels`**.

Four tests call them via `__canarySetPersonaEnabled`, where the call is a **silent no-op** —
the test runs, captures a frame, and the toggle it is named for does nothing, so the image is
indistinguishable from the un-toggled one:

| Test | Dead id(s) |
|---|---|
| `diag-pencil-no-nodes` | `render.nodes` |
| `diag-pencil-no-edges` | `render.edges` |
| `diag-pencil-no-labels` | `render.labels` |
| `diag-pencil-only-background` | all three |

These four carry a `[STALE 2026-08-18]` note naming the dead id and are set to
`mode: "capture"`. A known no-op must not sit in a suite asserting what it cannot test.

**Action still needed (operator):** map each to a live module id, or retire the arm.

**Live ids — all 51**, for anyone tempted to re-derive this with a pattern:
`compute.metrics`, `compute.rag.eager-l3`, `compute.simulation`, `debug.api-tick-log`,
`debug.fps-hud`, `debug.junction-markers`, `debug.playground`, `fx.audio-reactive`,
`fx.chromatic-aberration`, `fx.color-grade`, `fx.connection-sweep`, `fx.constellations`,
`fx.crystal-material`, `fx.cursor-trail`, `fx.debug-layer-colors`, `fx.echo-trails`,
`fx.edge-flow`, `fx.film-grain`, `fx.force-field`, `fx.group-atmosphere`, `fx.heat-map`,
`fx.hover-lift`, `fx.label-bloom`, `fx.laser-rat`, `fx.magnetic-snap`, `fx.node-pulse`,
`fx.outline`, `fx.pencil-toon`, `fx.post-process`, `fx.selection-halo`, `fx.sounds`,
`fx.time-lapse`, `fx.vignette`, `render.context-jewel-hud`, `render.context-jewel-scene`,
`render.context-navigator`, `render.cross-context.portal`, `render.cross-context.qnode`,
`render.curl-noise-field`, `render.graph-scene`, `render.grid`, `render.gumball`,
`render.junction.bubble`, `render.junction.center`, `render.junction.pull-back`,
`render.junction.surface`, `render.junction.voronoi`, `render.paper`,
`render.penumbra-backdrop`, `render.penumbra-fallback`, `render.qverse-graph-nav`.

## 2. `display-modes` and `pencil-diff` — VLM, but only where the criteria are verified

> **CORRECTED 2026-08-18.** Nine `main-*` criteria were authored from each profile's one-line
> `description:` string. An audit against the actual rendering found **only `main-shaded` and
> `main-pencil` sound**. Seven asserted things the profile provably does not do and would
> have failed on a *correct* image — `main-blueprint` claims a deep-blue background where the
> measured modal pixel is literally `(0,0,0)`; `main-artistic` claims curved edges where
> `CatmullRomCurve3` over two waypoints is a straight line; `main-wireframe` claims
> point-cloud markers where the profile sets `nodeOpacity 0.0` on ordinary meshes;
> `main-standard` claims a gumball that is absent with nothing selected. Those seven are
> **reverted to no criteria**. Wrong criteria are worse than none: they teach the operator to
> ignore red.

Operator ruling: *"they should first pass vlm rather than visual regression. I think its fine
if they arent approved yet."*

Both suites had every checkpoint on the default `pixel-diff` with **no baseline and an empty
description** — asserting nothing while looking armed. Twelve checkpoints now carry
`mode: "vlm"` and authored criteria, worded from Qualia's own profile descriptions in
`packages/core/src/modules/profiles.ts` so the criteria describe what the profile is
specified to do:

- `display-modes` (9 of 10): standard, wireframe, shaded, pencil, blueprint, artistic,
  rendered, neon, aurora. **The tenth (`main-cinematic-curl-noise`) was wrongly excluded by
  the §1 error — `render.penumbra-backdrop` is live.** Restored; still needs a criterion.
- `pencil-diff` (3 of 11): `baseline`, `mount-trace`, `no-grid`. **Four more arms
  (`debug-colors`, `no-pencil-toon`, `only-debug-colors`, `standard-debug-colors`) were
  wrongly excluded here by the §1 error — their toggles are live.** They have been restored
  to their original mode and still need VLM criteria; those were deliberately NOT authored
  in the same pass, because the method that produced them is itself under review (§6).

## 3. Never-run: the 11 `rh2-*` tests

Not stale — **stillborn**. Commit `870cad9` (2026-05-14, "RH-2 multi-display sweep suite +
11 tests") already contained the malformed JSON (`__canaryApplyPerfSnapshot({ "theme":
"dark" })` with unescaped inner quotes). They have never parsed, never run, and have zero
result directories. Everything they target is alive today.

They hid because `canary doctor`'s suite-completeness check *was* gated behind `--suite`, so
`doctor --workload qualia` checked no suite at all. **Fixed the same day in `47db29d`** — it
now sweeps every suite and parses every test file, and `doctor --workload qualia` exits 1 as
a result. Before that fix, only naming the suite explicitly worked:
`doctor --workload qualia --suite multi-display` reports **"0 of 11 tests loadable"** and
errors on each — the guard is correct and was simply never asked. Sweeping every suite in all
five workloads, `multi-display` is the only incomplete one.

**Open decision:** repair (11 quote escapes, plus a fixture-determinism problem —
`minimal/.qualia` stores no coordinates and `LayoutEngine` seeds them with `Math.random()`,
so pixel baselines would be flaky) or retire (orphans two live Qualia hooks and trips the
cross-repo protocol; `Qualia/spec/CANARY.md` names RH-2).

## 4. Regenerable scaffolding, safe to delete — 13 tests + 5 suites

All machine-generated by `workloads/qualia/sweeps/generate-sweep.mjs`, all reproducible from
the 8 retained specs. **DELETED 2026-08-18** (commit `bdda672`, pushed) at the operator's
instruction. Each retained spec now carries a `_retired` field, because regenerating one
would faithfully re-create the deleted tests — deleting output while leaving the thing that
reproduces it is a delay, not a deletion.

| Tests | Why dead |
|---|---|
| `sweep-w1-personas-*` (3) | 39 of 51 states duplicated by `w2-atlas`; the other 12 are personas Qualia deleted 2026-07-19 |
| `sweep-w1-smoke-*` (1) | the generator's own mutation-kind demo |
| `sweep-w3-pairs-*` (2), `sweep-w3-planar-*` (5) | one-shot campaign; findings frozen into `Qualia/spec/DISPLAY-BEHAVIOR.md` |
| `sweep-w4-fix-verify-*` (2) | verified bugs 0054–0058, all closed |

No live consumer runs them: the display-sweep skill invokes only `w2-atlas`, `desktop-mini`,
`display-invariants`, `platform-parity`.

`sweep-w2-atlas` is **not** redundant and must not be pruned — measured over 1,022 states,
139 of 146 state ids produce a different effect-path set per family, and an exhaustive search
over all 2⁷−1 = 127 subsets of the other specs found none reproducing the **376** distinct
(stateId, effect-path-set) pairs that `w2-atlas-r6` yields.

> The 376 figure was challenged as fabricated, then **verified and reinstated**. It is
> computed the way `sweeps/derive.mjs:112` defines a signature — a set of effect *paths*.
> The competing figures (396/294/294/483/458) count `(path, before, after)` triples, which
> is a different question that `derive.mjs` never asks.

## 5. Other drift worth knowing

- **Sweep driver drift.** After the §4 deletions, **10** `sweep-*` tests embed a frozen copy
  of `sweep-driver.js` — now a **single vintage** at 19,586 chars against a live driver of
  20,528. Still stale, but a narrower problem than the four vintages that existed before the
  deletion. The drift-watch skill regenerates before every run, so this is hygiene rather than
  an active break.
- **`qualia-v4-refresh-toolbar`.** Checkpoint `refresh-disabled-initially` asserts a disabled
  Refresh button, but `Toolbar.tsx:215` disables it only at `pointerCount === 0` and the boot
  workspace ships 79 pointers. A guaranteed VLM failure.
- **`main-*` under-covers.** A one-test-per-profile catalog pinned at **11** of 17 profiles;
  missing `bioluminescent`, `circuit`, `x-ray`, `laser-rat`, `mobile` **and `minimal`**.
- **`landing-*` prose is stale.** Narrates a 5-pill / "21 of 37" screen that is now 18 pills /
  51 modules. VLM criteria only — no structural break.
- **`eager-l3-no-provider-noop`.** Its description mentions `__canaryRetriggerEagerSweep`,
  which was **never implemented** (not removed — the other unblock route was taken instead,
  Qualia `9b01603`). Prose only.

## What was checked and found clean

`resolver-*` (13), `viewport-*` (5), `display-inv-*` (3), `playground-*` (7), `landing-*` (5),
`qualia-v4-*` (11), `diag-md-editor`, and the entire `qualia-desktop` (14) and `qualia-web`
(10) workloads. Hashing all 111 parseable tests on canvas + commands + actions + checkpoint
params produced **zero** duplicate pairs. (98 parseable tests, not 111 — the earlier figure
predated the §4 deletions.)

> One caveat on provenance: the duplication lens's verification agent died on a transport
> error (Cloudflare 521), so the "zero duplicates" result carries one less layer of
> adversarial checking than the rest of this document.
