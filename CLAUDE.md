# CLAUDE.md

## Project: Canary — Cross-Application Visual Regression Testing Harness

### Before Any Work
Read `spec/SUPERVISOR.md` — it is the single source of truth for all build decisions.

### Spec Files (read in order)
1. `spec/SUPERVISOR.md` — Orchestration, constraints, gate checklists, dependency matrix
2. `spec/ARCHITECTURE.md` — System design, IPC protocol, comparison engine, two-process model
3. `spec/PHASES.md` — Build phases with checkpoints
4. `spec/TESTS.md` — Unit and integration test specifications

### Key Rules
- **Namespace**: `Canary` (harness), `Canary.Agent` (shared agent interface), `Canary.Agent.Rhino` (Rhino-specific agent)
- **Framework**: `net8.0` for the harness and agent library; `net48` for Rhino agent plugin
- **Solution**: `Canary.sln` contains all projects
- **IPC**: Named pipes with JSON-RPC messages — no HTTP, no sockets
- **Input replay**: Win32 `SendInput` via the WindowsInput NuGet — mouse/keyboard events injected at the OS level
- **Screenshots**: Captured by the agent inside the app (not by the harness) — the agent has access to the app's rendering
- **Comparison**: Pixel diff with configurable tolerance, SSIM as secondary metric
- **Ctrl+C**: The harness MUST always respond to Ctrl+C. Display "Press Ctrl+C to abort" in every status output
- **Tests**: `[Trait("Category", "Unit")]` for headless, `[Trait("Category", "Integration")]` for tests requiring a running application

### Workflow
1. Read phase requirements
2. Check Supervisor gate checklist
3. Build checkpoint
4. Run `dotnet build` (0 errors, 0 warnings)
5. Run `dotnet test --filter "Category=Unit"`
6. Log results in BUILD_LOG.md
7. Advance to next checkpoint

### Current Phase: 7 (complete — all phases done)
