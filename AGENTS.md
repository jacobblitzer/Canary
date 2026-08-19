# Agent Guide — Canary

> **What this is.** Canary — cross-application visual regression testing harness
> (pixel-diff + VLM modes, Rhino + web workloads, supervised sessions + flight
> recorder). This file is the **front door**: must-know rules plus a map to the
> depth in [`spec/`](spec/) and [`AGENTS-DETAIL.md`](AGENTS-DETAIL.md). **Read the
> file the map points you to before doing that kind of work.**
> **Editing this file: keep it under 7,900 chars** — move depth to `AGENTS-DETAIL.md`; a size
> guard alarms via `MultiVerse/GOVERNANCE-ALERTS.md` on breach.

## Before any work (do this first)
1. Read [`CODE-TRACING-CHECKLIST.md`](CODE-TRACING-CHECKLIST.md) before any non-trivial change — and **update it** when you discover a new load-bearing path (`MultiVerse/SUPERVISOR.md` D6).
2. Read [`spec/SUPERVISOR.md`](spec/SUPERVISOR.md) — single source of truth for build decisions.
3. The `multiverse-supervisor` skill enforces [`MultiVerse/SUPERVISOR.md`](../MultiVerse/SUPERVISOR.md) — especially **D7 (ground before you assert)** + **D8 (trust through verification)**; § Skills.
4. If `docs/feedback/inbox/` is non-empty, list new items before other work (§ Feedback inbox).

## Where to look (the map)
| Working on… | Read first |
|---|---|
| Spec reading order (SUPERVISOR → … → workload specs) | [`AGENTS-DETAIL.md`](AGENTS-DETAIL.md) § Spec files |
| Test modes — pixel-diff / vlm / both / capture-only | § Test modes |
| Supervised sessions + flight recorder | § Sessions · `docs/session-flight-recorder.md` |
| Debug-overhaul surfaces (UI tabs, run dirs, telemetry, MCP server) | § Debug overhaul · `docs/mcp-server.md` |
| Penumbra (web) tests — shared Vite/Chrome, C2 event gate | § Penumbra web tests |
| CPig / Pigture / Slop / Lightro / Bristle suites — shared runMode, file-source checkpoints | § Rhino-workload suites · `spec/*_WORKLOAD.md` |
| KinematicBridge suite — env deps, cm units | § KinematicBridge tests |
| Penumbra-in-Rhino suites — OOP fallback, glsl, fieldops, display-matrix | § Penumbra-in-Rhino suites |
| Authoring Rhino `setup.commands` macros | `docs/features/rhino-setup-commands-macros.md` |
| Slop Log-Tap debugging of failing runs | § Logging |
| Reviewing a generated definition's LAYOUT (whole-canvas PNG) | `scripts/slop-canvas-shot.ps1` |
| Asks to peers | § Asks queue · `docs/asks/README.md` |
| Feedback inbox layout + lifecycle | § Feedback inbox · `docs/feedback/README.md` |
| QC round trip — writing a learning on a QC box, filing it here | § QC round trip · `docs/templates/qc-learning-template.md` |
| Frontmatter schema, docs tree | § Journaling |
| Dependencies · release type · bug-repro steps | § Dependencies · § Repro |
| Active Penumbra initiatives | § Penumbra initiatives |

## Build & run (quick reference)
- **Build:** `dotnet build Canary.sln` — must be **0 errors, 0 warnings**.
- **Drive payload:** only via `scripts/publish-payload.ps1` → `verify-payload.ps1`.
- **Unit tests:** `dotnet test tests/Canary.Tests --filter "Category=Unit"`.
- **`canary commission`** — run FIRST on a machine you did not set up: can it test at all? **Exit 4** = harness unproven (≠ doctor's 1, ≠ run path's 3). Detail → `workloads/commissioning/README.md`; open items → `docs/OPEN-ITEMS.md`.
- **`canary doctor`** — before trusting a run on a machine you did not set up: root, tokens, and **every** test a suite declares. **Exit 1** = install incomplete; **exit 5** = NOT PROVEN — checks could not RUN, neither a pass nor an install failure; **0** = complete for the content present. Short suite = hard failure (§ Doctor). Reads the commissioning report, keeping a harness fault separate.
- **QC bundle back:** `scripts/import-qc-bundle.ps1 <bundlePath>` — the dev-side door for a QC machine's bundle: files its `learnings\` into `docs/feedback/inbox/` and rebuilds the three signals from `qc-summary.json` exit codes, never from the prose (§ QC round trip).
- **GUI:** kill→build→launch the built exe, NOT `dotnet run` — § Repro.
- **`canary baselines lock|verify`** — git-tracked ledger of which checkpoints have an approved baseline. Absent ledger = error; `rows: []` = legal (§ Doctor).
- **UI-first runs** (`STANDARD.md` §16 rule 8): "run canary" means **no `--headless`** — that flag is for CI and agent-internal verification only. → [`AGENTS-DETAIL.md`](AGENTS-DETAIL.md) § UI-first runs.
- **Keep-open:** `--test` without `--headless` keeps the app open; `--keep-open` forces it. → [`AGENTS-DETAIL.md`](AGENTS-DETAIL.md) § Keep-open.
- **Run suites:** `canary run --workload penumbra` (web) · rhino `--suite <name>`. **CPig via `--suite cpig`, never `--test`** (all `runMode: shared`). Full list + why → [`AGENTS-DETAIL.md`](AGENTS-DETAIL.md) § Run suites.
- **Modes:** `--mode pixel-diff` (default) | `vlm` | `both`; per-checkpoint `mode: "vlm"` wins over the flag; `mode: "capture"` = save-only, never FAILs, wins over `--mode` (§ Test modes).
- **Supervised session:** `canary session start --workload {qualia|penumbra|rhino} [--file <abs>.3dm]` — § Sessions.
- **Status:** `spec/PHASES.md` + tail of `BUILD_LOG.md`. Test counts move every commit — count them.

## Key rules (non-negotiable)
- **Namespace:** `Canary` (core + harness), `Canary.Agent` (shared), `Canary.Agent.*` (per-app).
- **Framework:** `net8.0-windows` (Core, Harness, UI), `net8.0;net48` (Agent), `net48` (Rhino). UI is **Avalonia 11.2 + FluentAvaloniaUI 2.2 + CommunityToolkit.Mvvm 8.3** (`docs/features/canary-ui-avalonia.md`).
- **IPC:** named pipes + JSON-RPC only — no HTTP, no sockets.
- **Screenshots:** captured by agent inside the app, not by the harness.
- **Ctrl+C:** must always work. Display "Press Ctrl+C to abort" in status output.
- **Tests:** `[Trait("Category", "Unit")]` headless, `[Trait("Category", "Integration")]` needs app.
- **`runMode: shared` is the DEFAULT for ALL tests** — one `"runMode": "fresh"` test forces the whole suite to per-test launches; every shared test MUST begin its `actions` with a cleanup pulse (Build off → Cleanup on → Cleanup off). Full rules → § Rhino-workload suites.
- **Rhino units-macro gotcha:** a `-_DocumentProperties` units macro MUST include `_UnitSystem` before the unit name and prefer `_EnterEnd` — omitting either hangs the command line and blocks the whole test.

## Cross-Repo Change Protocol (mandatory)
When your changes affect other repos:
1. **Update `AGENTS.md` in every affected repo** — #1 priority; it's what the next session reads first.
2. **Update `spec/PEERS.md`** in every affected repo that has one (contracts, I/O maps, GUID tables).
3. **Log to MultiVerse** — append to `C:\Repos\MultiVerse\BUILD_LOG.md`: `YYYY-MM-DD | cross-repo | Canary → AffectedRepos | one-line summary`.

**Triggers** — any change that would leave another repo's AGENTS.md/PEERS.md stale (new `TestCheckpoint` field, new agent action, changed test conventions). Trigger table → § Cross-repo protocol.

## Journaling (mandatory while you work)
**Bug fix** → `docs/bugs/NNNN-slug.md` + `CHANGELOG` `### Fixed`; **feature** → `CHANGELOG` + `docs/features/FEATURE_STATUS.md`; **debug session** → `docs/debug-sessions/YYYY-MM-DD-slug.md`; **decision** → `docs/decisions/NNNN-slug.md` (MADR); **research** → `docs/research/YYYY-MM-DD-slug.md`; **build/test run** → append `BUILD_LOG.md`.

## Conventions
- **Commits:** conventional (`feat:` / `fix:` / `docs:` / `test:` / `refactor:` / `chore:`).
- **Release type:** infrastructure — no formal release; milestone tags only (e.g. `canary-v1`).
- **Asks to peers:** file at `docs/asks/<peer>/<NNNN>-slug.md` (§ Asks queue).
- **Skills:** see [`MultiVerse/SKILLS.md`](../MultiVerse/SKILLS.md); supervisor disciplines full text → § Skills.
