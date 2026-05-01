# Integration Guide — Penumbra CDP Bridge Agent

## Files to Add to the Canary Repo

### New Project: src/Canary.Agent.Penumbra/
```
src/Canary.Agent.Penumbra/
├── Canary.Agent.Penumbra.csproj    ← net8.0-windows, refs Canary.Core + Canary.Agent
├── PenumbraBridgeAgent.cs          ← ICanaryAgent implementation (the core bridge)
├── PenumbraConfig.cs               ← Config model for workload.json penumbraConfig section
├── ViteManager.cs                  ← Start/stop Vite dev server
└── CdpInputReplayer.cs             ← Mouse replay via CDP (Path B, for interactive tests)
```

### CDP Client Library: src/Canary.Core/Cdp/
```
src/Canary.Core/Cdp/
├── CdpClient.cs                    ← WebSocket-based CDP protocol client
└── ChromeLauncher.cs               ← Find + launch Chrome/Edge with remote debugging
```

### Updated Workload: workloads/penumbra/
```
workloads/penumbra/
├── workload.json                   ← REPLACE existing (adds penumbraConfig section)
├── AGENT_NOTES.md                  ← REPLACE existing (updated with CDP architecture)
├── CLAUDE_CODE_RUNNER.md           ← NEW — automation prompts for Claude Code
└── tests/
    ├── tape-csg-orbit.json         ← Scene 0, 4 camera angles
    ├── atlas-blob-orbit.json       ← Scene 1, 4 camera angles
    ├── multi-field-orbit.json      ← Scene 2, 4 camera angles
    └── stress-test-orbit.json      ← Scene 3, 4 camera angles + wide shot
```

### Penumbra-Side Hooks: penumbra-hooks/
```
penumbra-hooks/
└── canary-hooks.ts                 ← Code to add to Penumbra's test/main.ts
```

---

## Step 1: Add the Project to the Solution

```bash
cd C:\Repos\Canary
dotnet sln Canary.sln add src/Canary.Agent.Penumbra/Canary.Agent.Penumbra.csproj --solution-folder src
```

This also needs the Canary.Harness to reference it (so the test runner can instantiate the bridge agent):

```bash
cd src/Canary.Harness
dotnet add reference ../Canary.Agent.Penumbra/Canary.Agent.Penumbra.csproj
```

## Step 2: Verify Build

```bash
dotnet build Canary.sln
# Expected: 0 errors, 0 warnings
```

## Step 3: Wire Up the TestRunner

The existing `TestRunner.cs` needs to know about the new agent type. In the agent creation logic
(wherever the harness instantiates an `ICanaryAgent` based on `workload.agentType`), add:

```csharp
case "penumbra-cdp":
    var penumbraConfig = PenumbraWorkloadConfig.LoadAsync(workloadJsonPath).Result;
    var bridgeAgent = new PenumbraBridgeAgent(penumbraConfig.PenumbraConfig);
    await bridgeAgent.InitializeAsync(ct);
    return bridgeAgent;
```

The TestRunner also needs to handle the new checkpoint format (camera positions instead of
atTimeMs). When a checkpoint has a `camera` field:

```csharp
// Instead of replaying input and pausing at atTimeMs,
// set the camera and capture immediately:
if (checkpoint.Camera != null)
{
    await agent.ExecuteAsync("SetCamera", new Dictionary<string, string>
    {
        ["azimuth"] = checkpoint.Camera.Azimuth.ToString(),
        ["elevation"] = checkpoint.Camera.Elevation.ToString(),
        ["distance"] = checkpoint.Camera.Distance.ToString(),
        ["stabilizeMs"] = (checkpoint.StabilizeMs ?? 500).ToString()
    });
}
```

## Step 4: Add Penumbra Hooks

In the Penumbra repo (`C:\Repos\Penumbra`), apply the changes from `penumbra-hooks/canary-hooks.ts`
to `test/main.ts`:

1. Add the `window.__canary*` API functions after renderer/camera initialization
2. Add the `__canaryLockSize` guard around canvas resize in the render loop
3. Add the `__canaryPauseAnimation` guard around the torus orbit animation
4. Add `setSpherical()` / `getSpherical()` to the orbit camera class

## Step 5: Establish Baselines

```bash
# First run — captures screenshots, all show NEW
canary run --workload penumbra

# Verify the captures look correct in the report
canary report --workload penumbra

# Approve as baselines
canary approve --workload penumbra
```

## Step 6: Verify Regression Detection

Make a trivial shader change in Penumbra (e.g., change a color), then:

```bash
canary run --workload penumbra
# Expected: FAIL for affected scenes, PASS for unaffected
```

Revert the change and run again:

```bash
canary run --workload penumbra
# Expected: all PASS
```

---

## TestRunner Changes Summary

The main code change needed in the existing Canary codebase (beyond adding the new project)
is extending `TestDefinition` and `TestRunner` to support:

1. **`camera` field on checkpoints** — `{ azimuth, elevation, distance }` instead of `atTimeMs`
2. **`scene` field in setup** — `{ index }` to load a specific Penumbra scene
3. **`canvas` field in setup** — `{ width, height }` to set deterministic canvas size
4. **`penumbra-cdp` agent type** — instantiate PenumbraBridgeAgent instead of pipe-based agent

The bridge agent handles its own lifecycle (Vite + Chrome) inside `InitializeAsync()`,
so the harness doesn't need to know about Vite or CDP — it just talks to `ICanaryAgent`.
