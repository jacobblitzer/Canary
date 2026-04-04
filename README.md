# Canary — Cross-Application Visual Regression Testing

Canary is a visual regression testing harness for desktop applications. It records user interactions, replays them against live applications, captures screenshots at checkpoints, and compares them against verified baselines to catch visual regressions across builds.

**Like a canary in a coal mine — it warns you early when something looks wrong.**

## Architecture

Canary uses a **two-process model**:

- **The Harness** (`canary.exe`) — an external orchestrator that manages test lifecycle, input replay, screenshot comparison, and reporting. It runs outside the application being tested.
- **Workload Agents** — thin plugins that live *inside* each target application (Rhino, Qualia, Penumbra, etc.). They receive commands from the harness over named pipes and execute them in the host app's context.

```
canary.exe (harness)  ←—— Named Pipe IPC ——→  Agent inside Rhino
                      ←—— Named Pipe IPC ——→  Agent inside Qualia
                      ←—— Named Pipe IPC ——→  Agent inside Penumbra
```

## Quick Start

```bash
# Record a new test (records mouse/keyboard input relative to the app viewport)
canary record --app rhino --name sculpt-undo-test

# Run all tests for a workload
canary run --workload pigment

# Run a specific test
canary run --test sculpt-undo-test

# Approve new baselines after intentional visual changes
canary approve --test sculpt-undo-test

# Generate HTML report from last run
canary report
```

## Abort

**Press `Ctrl+C` at any time to kill the test run.** The harness will terminate all child application processes and exit cleanly. This is always available and always safe.

## Workloads

| Workload | Host Application | Agent Type |
|----------|-----------------|------------|
| Pigment  | Rhino 8         | RhinoCommon plugin (.rhp) |
| Qualia   | Custom viewer    | Built-in module |
| Penumbra | Custom app       | Built-in module |
| Grasshopper | Rhino 8 + GH | RhinoCommon plugin (.rhp) |

## Build

```bash
dotnet build Canary.sln
```

## Test

```bash
dotnet test Canary.Tests/Canary.Tests.csproj --filter "Category=Unit"
```

## Requirements

- Windows 10/11
- .NET 8.0+
- Target application installed (Rhino 8 for Rhino workloads)
- A real display (not RDP) — GPU rendering required for screenshot capture

## License

MIT
