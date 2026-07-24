---
title: "WaitForReady timeouts are swallowed — tests proceed against a non-ready app"
date: 2026-07-24
tags: [bug, qualia-workload, harness, false-green, readiness]
status: open
project: canary
component: QualiaBridgeAgent readiness path x qualia setup.commands convention
severity: medium
fix-commit: ""
found-during: "P4 fresh-session review (2026-07-24) — documented + spawned as a follow-up chip; filed as a durable bug post-campaign on operator instruction"
---

# WaitForReady failures don't fail anything

## Mechanism (grounded)

`window.__canaryWaitForReady(timeoutMs)` RESOLVES with an envelope —
`{ok: true}` when ready, `{ok: false, reason: 'Timed out …'}` on
timeout (`Qualia/packages/ui/src/canary-hooks.ts:212-219`). It never
throws. Two consumers treat resolution as success:

1. **Every qualia-family test prelude** — `setup.commands[0]` is the
   bare string `"window.__canaryWaitForReady(30000)"`. Setup commands
   fail a test only on a THROWN error (the structural-assertion
   mechanism); a resolved `{ok:false}` envelope sails through, and the
   test body runs against a half-booted app.
2. **`QualiaBridgeAgent.WaitForReadyInternalAsync`** evaluates
   `window.__canaryWaitForReady(remainingMs)` and awaits the result
   without inspecting the envelope — only the outer hooks-not-installed
   polling loop can throw `TimeoutException`. Once `__canaryHooksReady`
   is true, a not-ready app passes initialization.

## Failure scenario

Slow boot (cold WebView2 profile, model download, big vault): hooks
install but `isReady()` (demo data loaded) lags. Initialization and the
prelude both "pass"; the first structural assertion then fails on empty
state (flake attributed to the test) — or worse, a negative-only
assertion passes vacuously (the false-green class the P3/P4 reviews
fought).

## Fix shape (when picked up)

- Prelude convention: wrap the wait —
  `(async () => { const r = await window.__canaryWaitForReady(30000); if (!r || r.ok !== true) throw new Error('not ready: ' + (r && r.reason)); })()`
  — ideally updated once in the generators/templates and rolled across
  test JSONs mechanically.
- `WaitForReadyInternalAsync`: parse the envelope; `ok !== true` →
  throw with the reason.
- Both are behavior-tightening only; no hook change (hook-stability
  unaffected — the envelope contract is correct, the CONSUMERS are lax).
