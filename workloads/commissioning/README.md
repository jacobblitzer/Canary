# The commissioning workload

Ruling 7A. This content answers one question — **can this machine test at all?** — and it is
the only workload a USER-tier machine gets.

Run it: `canary commission [--workload <w>]`

## Why it gates rather than precedes

The campaign needs a three-way distinction, because collapsing it wastes days:

| Signal | Meaning |
|---|---|
| commissioning red | the harness is broken — **any plug-in result is unreadable** |
| `doctor` red | the install is incomplete — **not** a defect in the plug-in |
| commissioning green + doctor green + smoke red | **the only combination that is a real finding** |

So `commission` has its own exit code (**4**), distinct from doctor's 1 and the run path's 3.
A single non-zero would hide which of those three happened.

## The three layers

**1 — comparer** (fatal, **no app required**). Compares images shipped in `references/`
whose answer is known exactly: an image against itself must find 0; the pair must find
exactly 256 of 4096; and a copy shifted 2 per channel must find 0 at the default threshold of
3. That third assertion is not padding — a comparer that ignored its threshold would report
every anti-aliasing difference on earth as a regression.

This is the layer that runs where nothing else does, and the entire value of the USER tier.

**2 — repeatable** (fatal, needs an app). Two captures of an unchanged scene, back-to-back in
one session, must be **identical** — zero tolerance. If a machine cannot reproduce its own
frame seconds apart, no baseline from anywhere will ever match on it and every pixel
comparison is noise. Ruling 7A calls this the quiet star: it needs no shipped baseline and it
is the only check that says whether baselines could travel at all.

**3 — reference** (**not fatal**, needs an app). Compares a capture made on another machine
against one made here. A failure does not mean the harness is broken — it means pixel
baselines do not travel to this machine, which is useful evidence and nothing more. Such a
machine can still test: approve baselines locally, or use VLM mode.

## NotRun is not a pass

A layer nobody attempted has answered nothing. `commission` with no `--workload` leaves
layers 2 and 3 `NotRun` and reports the harness as **unusable**, because capture
repeatability on that machine is genuinely unknown. Same rule that makes an absent
`hostReady` a failure rather than a pass.

## Regenerating references/

The three comparer images are synthetic and deterministic — 64x64, a 16x16 patch, and a
2-per-channel nudge. Regenerate with `scripts/make-commissioning-references.py`.

`rhino-reference.png` is different: it is a real capture from a real machine
(`BOOK-4IBO7G77D6`, 552x310), shipped so layer 3 has something made *elsewhere* to compare
against. Replacing it with a capture from the machine under test would make layer 3
tautological.

> **Known, and expected on machine 2:** that capture came back 552x310 although 800x600 was
> requested — the viewport ignores a declared size unless it is floating. Layer 2 is
> unaffected, because both of its captures agree. But a machine whose viewport differs will
> report "baselines cannot travel between these two machines at all" on layer 3. That is the
> honest finding, not a fault in commissioning.
