# CLAUDE.md

## Project: Canary — Cross-Application Visual Regression Testing Harness

### Before Any Work
Read `spec/SUPERVISOR.md` — it is the single source of truth for all build decisions.

### Spec Files (read in order)
1. `spec/SUPERVISOR.md` — Orchestration, constraints, gate checklists, dependency matrix
2. `spec/ARCHITECTURE.md` — System design, IPC protocol, comparison engine, two-process model
3. `spec/PHASES.md` — Build phases with checkpoints (0–7)
4. `spec/PHASES_UI.md` — Build phases with checkpoints (8–12: Core extraction + WinForms GUI)
5. `spec/TESTS.md` — Unit and integration test specifications (0–7)
6. `spec/TESTS_UI.md` — Test specifications (8–12)

### Key Rules
- **Namespace**: `Canary` (core + harness), `Canary.Agent` (shared agent interface), `Canary.Agent.Rhino` (Rhino-specific agent)
- **Projects**: `Canary.Core` (shared library), `Canary.Harness` (CLI), `Canary.UI` (WinForms GUI, Phase 9+)
- **Framework**: `net8.0-windows` for Core, Harness, UI; `net8.0;net48` for Agent; `net48` for Rhino agent plugin
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

### Current Phase: 12 (Phases 0–12 complete — 72+ tests passing)
