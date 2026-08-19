---
title: Open items — decisions, debt and known gaps
status: current
kind: register
updated: 2026-08-19
---

# Open items

Things that are known, deliberate, and not yet resolved. Filed here because a commit message
is not a place anyone looks, and "I mentioned it at the time" is not a record.

Genuine defects get a numbered doc in [`docs/bugs/`](bugs/) and are linked from here.
Everything else is a decision waiting on the operator, or debt with a named owner.

---

## Waiting on an operator decision

### 1. The Penumbra dependency — 62 tests depend on something never seen loaded
**62 rhino tests** hard-depend on the Penumbra Rhino plug-in via the `WaitForPenumbraFrame`
action. This machine's capture contains **zero** occurrences of "penumbra" in its loaded
list — no `rhino:` id, no `gh:` id. A `penumbra-1.0.0` yak exists, so it is packaged and
installable; it is simply not installed here.

Deliberately **not declared**: writing `rhino:Penumbra.Rhino` would false-red the dev machine
on 62 tests immediately. `workloads/plugin-packages.json` carries it as the only
`"grounded": "inferred"` entry, with the reasoning inline.

Either those tests do not currently pass, or the plug-in reaches Rhino by a route
`PlugIn.GetInstalledPlugIns()` does not report. **Machine 2 may answer this for free** — the
prompt asks it to report the exact id string if a Penumbra plug-in appears.

### 2. `gh:KinematicImporter` — declared, and nothing provides it
A test declares it; no yak package publishes it. `machine-setup.ps1` reports it as
`NO PACKAGE MAPPED` rather than papering over it. Needs either a package or a decision that
this test is DEV-only.

### 3. Four dead qualia module ids — remap or retire
`render.nodes`, `render.edges`, `render.labels` were removed from Qualia on 2026-07-19.
Four tests still toggle them, so the toggle is a silent no-op; they are marked
`[STALE 2026-08-18]` and set to `mode: "capture"`. See
[`qualia-stale-2026-08-18.md`](qualia-stale-2026-08-18.md) §1.

### 4. Seven reverted VLM criteria
Seven `main-*` checkpoints had criteria authored from each profile's one-line `description:`
field. An audit against the actual rendering found only `main-shaded` and `main-pencil`
sound — the rest asserted things the profile provably does not do and would have failed on a
*correct* image. They were reverted to no criteria rather than rewritten, because the method
is what failed. Re-authoring needs measurement against real renders.

### 5. Pinning `origin` on requirements
See [bug 0024](bugs/0024-origin-deviates-can-never-fire.md). The shadowing detector cannot
fire until some requirement pins an origin, and a blanket pin would produce seven warnings
per run on the DEV machine where developer-origin is normal. Needs a decision about which
requirements pin, and whether the pin differs by tier.

---

## Debt with a named owner

### 6. `InstallReadiness` is duplicated in PowerShell
`scripts/machine-setup.ps1` computes the declared-vs-loaded join itself; `InstallReadiness`
in Core now computes the same join for the UI. **Two implementations of one rule is the shape
that produced bug 0022.** The script already requires `canary.exe` for its re-capture step,
so it can call Core rather than keep a second copy. Until it does, a change to the join has
to be made in both places.

### 7. `Requirement` and `RequirementChecker` have no direct tests
The precondition layer went from 0 to ~41 tests, but these two types are still exercised only
through the view model and through `doctor`. This is the one place the layer does not meet
the bar the campaign holds everything else to.

### 8. UI-first runs do not work from an agent session
`canary run --workload qualia --suite multi-display` (no `--headless`) launched `Canary.UI`
with auto-run args and then produced nothing for six minutes: no run directory, no file
written, no browser, no dev server. `--headless` worked immediately. `AGENTS.md` already
documents that the UI launch flakes from agent sessions; this is a reproduction, and it means
the documented UI-first convention is currently unusable for agent verification.

### 9. Dead settings surface
Found while surveying the UI: `SettingsViewModel.SettingsChanged` is raised and has **no
subscribers anywhere in the solution**; `RetentionDays` is persisted and displayed but has no
consumer in the UI project; and `LocalhostViewModel` keeps its **own independent**
`CanarySettings.Load()` copy, so the same setting is owned by two view models with
last-writer-wins semantics — changing Tier 3 in Settings does not update the Localhost tab.

---

## Known and accepted, not defects

### The viewport ignores a declared capture size
Commissioning requested 800x600 and got 552x310 — the viewport honours a declared size only
when floating. Layer 2 is unaffected because both of its captures agree. But the shipped
`rhino-reference.png` is 552x310, so a machine whose viewport differs will report on layer 3
that "baselines cannot travel between these two machines at all". **That is the honest
finding, not a fault in commissioning** — and it may be the most interesting thing the first
QC run tells us.

### Layer 3 failing is not a broken harness
By design. It asks whether a pixel baseline made elsewhere matches here. A machine that fails
it tests perfectly well by approving locally or using VLM mode.
