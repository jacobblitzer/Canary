# Canary — Cross-Application Visual Regression Testing

Canary is a visual regression testing harness for desktop and browser applications. It records user interactions, replays them against live applications, captures screenshots at checkpoints, and compares them against verified baselines to catch visual regressions across builds.

**Like a canary in a coal mine — it warns you early when something looks wrong.**

## Architecture

Canary uses a **two-process model**:

- **The Harness** (`canary.exe`) — an external orchestrator that manages test lifecycle, input replay, screenshot comparison, and reporting.
- **Workload Agents** — thin plugins that live *inside* each target application and execute commands received over named pipes.

```
canary.exe (harness)  <-- Named Pipe IPC -->  Agent inside Rhino (.rhp plugin)
                      <-- Named Pipe IPC -->  PenumbraBridgeAgent (.NET) -- CDP --> Chrome
```

## Features

- **CLI + GUI**: Command-line harness and WinForms GUI with dark theme
- **Pixel diff + SSIM**: Configurable tolerance, composite diff images
- **HTML reports**: Self-contained with embedded images, status badges
- **JUnit XML**: CI-compatible test output
- **Input recording**: Win32 hooks (mouse/keyboard) or CDP mouse events
- **Programmatic camera**: Deterministic camera positioning via scripted coordinates
- **72+ unit tests** across 12 build phases

## Quick Start

```bash
# Build
dotnet build Canary.sln

# Run unit tests
dotnet test tests/Canary.Tests/Canary.Tests.csproj --filter "Category=Unit"

# Run visual regression tests for a workload
canary run --workload penumbra

# Approve new baselines after intentional visual changes
canary approve --workload penumbra

# Open the HTML report
canary report
```

## Abort

**Press `Ctrl+C` at any time** to kill the test run. The harness terminates all child processes and exits cleanly.

## Workloads

| Workload | Host Application | Agent Type | Status |
|----------|-----------------|------------|--------|
| Rhino | Rhino 8 | RhinoCommon plugin (.rhp) | Done |
| Penumbra | Chrome via CDP | Browser bridge agent | Active |
| Qualia | Custom viewer | Built-in module | Stub |
| Grasshopper | Rhino 8 + GH | RhinoCommon plugin (.rhp) | Planned |

## Project Structure

```
Canary.sln
src/
  Canary.Core/           # Shared library (comparison, config, orchestration, reporting)
  Canary.Harness/        # CLI entry point
  Canary.UI/             # WinForms GUI
  Canary.Agent/          # Shared agent interface (net8.0 + net48)
  Canary.Agent.Rhino/    # Rhino plugin (net48, outputs .rhp)
  Canary.Agent.Penumbra/ # CDP bridge for browser testing
tests/
  Canary.Tests/          # Unit tests
  Canary.Tests.Integration/
workloads/
  rhino/                 # Rhino smoke test definitions
  penumbra/              # Penumbra visual regression tests (5 test suites)
  qualia/                # Stub
docs/
  bugs/                  # Bug reports with queryable frontmatter
  decisions/             # Architecture Decision Records
  debug-sessions/        # Investigation journals
  features/              # Feature status tracking
  templates/             # Reusable doc templates
spec/                    # Frozen design documents (7 files)
```

## Documentation

- [CHANGELOG.md](CHANGELOG.md) — version history
- [BUILD_LOG.md](BUILD_LOG.md) — phase checkpoint records
- [Feature Status](docs/features/FEATURE_STATUS.md) — living feature tracker
- [Creating a Workload](docs/creating-a-workload.md) — agent development guide
- [Architecture Decisions](docs/decisions/) — ADRs

## Requirements

- Windows 10/11
- .NET 8.0 SDK
- Chrome or Edge (for Penumbra workload)
- Rhino 8 (for Rhino workload)

## License

MIT
