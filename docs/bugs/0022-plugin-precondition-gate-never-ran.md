---
id: 0022
title: The plug-in precondition gate never ran on Rhino
date: 2026-08-18
status: fixed
component: Canary.Agent.Rhino, Canary.Core/Orchestration
---

# The plug-in precondition gate never ran on Rhino

## Symptom

None. That is the whole problem.

`canary run` reported no precondition failures on any Rhino workload, ever, including on a
machine where a declared plug-in was demonstrably not loaded. The only trace was one line
buried in a long log:

```
Warning: the host could not report what it has loaded yet (hostReady=absent); plug-in preconditions NOT verified (continuing).
```

## Root cause

Two spellings of one field, in two assemblies, neither of which could see the other.

`Canary.Agent.Rhino/RhinoAgent.cs` emitted the readiness flag as a bare literal:

```csharp
data["grasshopperReady"] = ready ? "true" : "false";
```

`Canary.Core/Orchestration/TestRunner.cs` gated on the shared constant:

```csharp
resp.Data.TryGetValue(HostStateFields.HostReady, out var hostReady);   // "hostReady"
```

So on Rhino the field was **always absent**. `EnsureHostPreconditionsAsync` treats a
non-`"true"` readiness as "the host cannot see its own plug-in table yet" — a real and
expected state — and returns early with a warning. It therefore returned early on *every
Rhino run since the gate was written*, and `HostPreconditions.Diff` was never once reached.

The gate could not fail. It had no way to fail.

This is the same defect as the `ghLibraries`/`loaded` mismatch fixed earlier in the campaign.
That fix introduced `HostStateFields` so an agent and its reader would disagree at compile
time instead of at runtime — but it only converted the fields it was looking at. The
readiness field kept its literal, and the class of bug the constants existed to prevent
survived in the half nobody re-read.

### Why it stayed invisible

Grounded at the artifact rather than the code: `workloads/rhino/results/environment.json`,
captured the same day, contains `grasshopperReady: "true"` and no `hostReady` key at all.
Nothing on the harness side reads `grasshopperReady` — it was write-only. The host was ready,
had 96 libraries loaded, and the gate still excused itself.

An earlier claim in this campaign that the gate had been proven live (`exit 3 in 22s`) was
wrong about *which* gate: that was the offline file/service check in `RunCommand`, which never
consults readiness. The plug-in half had never fired.

## Fix

1. **`RhinoAgent.cs`** — emit `data[HostStateFields.HostReady]`. `data["framework"]` also went
   through the constant: it happened to match, which is one typo from repeating this.
2. **`TestRunner.cs`** — **absent is no longer treated as false.**
   - `hostReady == "false"` → the host answered honestly that it cannot tell yet. Warn and
     continue, unchanged.
   - `hostReady` **absent** → the agent does not implement the contract. Throw
     `PreconditionFailedException`. A guard that disables itself and passes every machine is
     strictly worse than one that fails loudly, and it is the exact silent-green shape this
     campaign exists to remove.
3. **`HostPreconditions.cs`** — a message-only `PreconditionFailedException` ctor, and
   `Format` prints `ex.Message` when `Misses` is empty. Without that branch a
   check-could-not-run failure rendered as a bare "aborted" line with no reason.

Blast radius of the new throw is bounded by the existing `if (plugins.Count == 0) return;`
above it — only workloads that actually declare `plugin` requirements can hit it. All three
`GetHostState` agents (Rhino, Penumbra, Qualia) now assign the constant, verified by test.

## Guard

`tests/Canary.Tests/Orchestration/HostReadyContractTests.cs`:

- `EveryGetHostStateAgent_ReportsReadiness_ViaTheSharedConstant` — a source-corpus scan for the
  **assignment** form `[HostStateFields.HostReady] =`. It has to be a source scan: the failure
  was two assemblies holding different opinions about a string, which no single-assembly test
  can observe, and the Rhino agent cannot be instantiated without Rhino.
- `NoAgent_AssignsAContractFieldUnderAHandWrittenKey` — rejects `["field"] =` for every
  contract field, including `"hostReady"` itself. The original bug was a *misspelled* field,
  not a missing one, so a presence check alone would not have caught it.
- The corpus builder asserts it found **at least 3** agents. A scan that matches nothing passes
  every assertion above it — the same silent-shrink failure in miniature.

### Mutation-proved

Reverting `RhinoAgent.cs` to `data["grasshopperReady"]` turns both guards red for the right
reasons, with the other 17 tests in the file staying green. Restored, all pass.

The first version of the presence guard was a substring check for `HostStateFields.HostReady`,
and it **stayed green under mutation** — defeated by the fix's own explanatory comment naming
the constant. That is why it now matches the assignment form. A guard nobody has watched go red
is not yet a guard, and this one had to be watched twice.
