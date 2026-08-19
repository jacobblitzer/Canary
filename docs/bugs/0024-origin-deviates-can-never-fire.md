---
title: The origin-deviates clash class can never fire — nothing declares an origin pin
status: open
severity: medium
found: 2026-08-18
area: preconditions / environment report
---

# 0024 — `origin-deviates` is a guard that cannot fire

## The defect

`EnvironmentReport.Analyse` detects six clash classes. One of them, `origin-deviates`, is
reachable only when a requirement declares an `origin` pin:

```csharp
if (expectedOrigins != null && expectedOrigins.Count > 0)
```

`RequirementChecker.ExpectedOrigins` builds that map from `plugin` requirements carrying an
`origin` value, omitting unpinned ones and `any`.

**Across the whole corpus there are 676 declared requirements — 207 `file`, 469 `plugin`,
0 `service` — and not one carries an `origin` pin.** So `ExpectedOrigins` returns an empty
map for every workload, the guard short-circuits, and the class has never fired once.

## Why it matters more than it looks

Origin-shadowing is *the* failure this campaign was built around: a Grasshopper library
loaded from a developer folder wins over a deployed install, so a yak install reports success
while the old code keeps running. `canary env --diff` deliberately ranks origin differences
*above* version differences for exactly this reason.

The detector for it exists, is tested, and is switched off by the content.

This is the same shape as bug 0022 — a guard that reports nothing wrong because it never
runs — and it was introduced in the same week, by the same person, while fixing 0022.

## Why it is not simply a bug to fix

Pinning `origin: "deployed"` on the QC-relevant requirements would make it fire. But on the
DEV machine **seven** libraries legitimately load from developer folders (the operator's own
repos and the Drive ship folder), so a blanket pin would produce seven warnings per run on
the machine where that is the normal condition. That is what the 2026-08-18 ruling already
rejected once.

So this needs a decision about *which* requirements should pin an origin, and whether the
pin should differ by tier — which is a content decision, not a code fix.

## What would close it

1. Decide which requirements pin `origin`, and to what.
2. A guard so the class cannot silently switch itself off again: assert somewhere that either
   at least one requirement pins an origin, or that the corpus deliberately pins none. Absent
   is not the same as false — an empty `ExpectedOrigins` should be a stated choice, not an
   accident nobody notices for months.

## Not yet verified

Whether a pin actually fires correctly end to end on a real capture. The unit tests cover
`Analyse` with a supplied expectation map; nothing has exercised the path from a pinned
requirement in content through to a warning in a live report.
