# Agent Guide — Canary

> **What this is.** Canary — cross-application visual regression testing harness
> (pixel-diff + VLM modes, Rhino + web workloads, supervised sessions + flight
> recorder). This file is the **front door**: the must-know rules
> plus a map to the depth. It is kept **under the ~8,000-character auto-load limit
> on purpose** so it always loads in full. Anything longer lives in [`spec/`](spec/)
> and [`AGENTS-DETAIL.md`](AGENTS-DETAIL.md) — the map below tells you which file to
> open for what you're doing. **Read the file the map points you to before doing
> that kind of work.**
> **Editing this file: keep it under 7,900 chars** — move depth to `AGENTS-DETAIL.md`; a size
> guard alarms via `MultiVerse/GOVERNANCE-ALERTS.md` on breach.

## Before any work (do this first)
1. Read [`CODE-TRACING-CHECKLIST.md`](CODE-TRACING-CHECKLIST.md) before any non-trivial change — and **update it** when you discover a new load-bearing path (`MultiVerse/SUPERVISOR.md` Discipline 6).
2. Read [`spec/SUPERVISOR.md`](spec/SUPERVISOR.md) — single source of truth for build decisions.
3. The `multiverse-supervisor` skill enforces [`MultiVerse/SUPERVISOR.md`](../MultiVerse/SUPERVISOR.md) at session start — especially **Discipline 7 (ground before you assert)** + **Discipline 8 (trust through verification)**; full text → § Skills.
4. If `docs/feedback/inbox/` is non-empty, list new items before other work (§ Feedback inbox).

## Where to look (the map)
| Working on… | Read first |
|---|---|
| Spec reading order (SUPERVISOR → … → workload specs) | [`AGENTS-DETAIL.md`](AGENTS-DETAIL.md) § Spec files |
| Test modes — pixel-diff / vlm / both / capture-only | § Test modes |
| Supervised sessions + flight recorder | § Sessions · `docs/session-flight-recorder.md` |
| Debug-overhaul surfaces (UI tabs, run dirs, telemetry, MCP server) | § Debug overhaul · `docs/mcp-server.md` |
| Penumbra (web) tests — shared Vite/Chrome, C2 event gate | § Penumbra web tests |
| CPig / Pigture / Slop suites — shared runMode, file-source checkpoints | § Rhino-workload suites · `spec/{CPIG,PIGTURE}_WORKLOAD.md` |
| KinematicBridge suite — env deps, cm units | § KinematicBridge tests |
| Penumbra-in-Rhino suites — OOP fallback, glsl, fieldops, display-matrix | § Penumbra-in-Rhino suites |
| Authoring Rhino `setup.commands` macros | `docs/features/rhino-setup-commands-macros.md` |
| Slop Log-Tap debugging of failing runs | § Logging |
| Asks to peers | § Asks queue · `docs/asks/README.md` |
| Feedback inbox layout + lifecycle | § Feedback inbox · `docs/feedback/README.md` |
| Frontmatter schema, docs tree | § Journaling |
| Dependencies · release type · bug-repro steps | § Dependencies · § Repro |
| Active Penumbra initiatives | § Penumbra initiatives |

## Build & run (quick reference)
- **Build:** `dotnet build Canary.sln` — must be **0 errors, 0 warnings**.
- **Unit tests:** `dotnet test tests/Canary.Tests/Canary.Tests.csproj --filter "Category=Unit"`.
- **GUI:** kill→build→launch the built exe, NOT `dotnet run` (backgrounds wrong): `taskkill //IM Canary.UI.exe //F` → build Release → `start "" "src/Canary.UI.Avalonia/bin/Release/net8.0-windows/Canary.UI.exe"` (§ Repro).
- **UI-first runs (canonical, `MultiVerse/STANDARD.md` §16 locked rule 8):** every operator-triggered `canary run` launches with `Canary.UI.exe` visible; `--headless` bypasses for CI; `--quiet` implies `--headless`. **When the operator says "run canary" in chat: do NOT use `--headless`** — they mean the UI-visible default (`canary run --workload <w> [--test <t> | --suite <s>]`). You (the agent) may still prefer `--headless` for your own end-to-end verification (the UI launch flakes from agent sessions) — an agent-internal choice, not what the operator means. Full text → § UI-first runs.
- **Run suites:** `canary run --workload penumbra` (web) · rhino workload (from `C:\Repos\Canary`): `--suite cpig` · `pigture` · `slop` · `kbridge` · `penumbra` (deprecated OOP) · `penumbra-glsl` · `cpig-fieldops` · `cpig-display-matrix`. **Run CPig tests via `--suite cpig`, never individual `--test`** — all are `runMode: shared` (ONE Rhino, sequential); `--test` respawns Rhino each time.
- **Modes:** `--mode pixel-diff` (default) | `vlm` | `both`; per-checkpoint `mode: "vlm"` wins over the flag; `mode: "capture"` = save-only, never FAILs, wins over `--mode` (§ Test modes).
- **Supervised session:** `canary session start --workload {qualia|penumbra|rhino} [--file <abs>.3dm]` — capture REPL / Sessions UI tab; manifest + telemetry per § Sessions.
- **Status:** `spec/PHASES.md` + tail of `BUILD_LOG.md`. Test counts move every commit — check `dotnet test --list-tests | wc -l`, don't trust stamped numbers.

## Key rules (non-negotiable)
- **Namespace:** `Canary` (core + harness), `Canary.Agent` (shared), `Canary.Agent.*` (per-app).
- **Framework:** `net8.0-windows` (Core, Harness, UI), `net8.0;net48` (Agent), `net48` (Rhino). UI is **Avalonia 11.2 + FluentAvaloniaUI 2.2 + CommunityToolkit.Mvvm 8.3** (`docs/features/canary-ui-avalonia.md`).
- **IPC:** named pipes + JSON-RPC only — no HTTP, no sockets.
- **Screenshots:** captured by agent inside the app, not by the harness.
- **Ctrl+C:** must always work. Display "Press Ctrl+C to abort" in status output.
- **Tests:** `[Trait("Category", "Unit")]` headless, `[Trait("Category", "Integration")]` needs app.
- **`runMode: shared` is the DEFAULT for ALL tests** — one `"runMode": "fresh"` test forces the whole suite to per-test launches; every shared test MUST begin its `actions` with a cleanup pulse (Build off → Cleanup on → Cleanup off). Full rules → § Rhino-workload suites.
- **Rhino units-macro gotcha:** any `-_DocumentProperties` units macro MUST include `_UnitSystem` before the unit name and prefer `_EnterEnd` over hand-counted `_Enter`s — omitting either hangs the Rhino command line and blocks the whole test (agent pipe disconnects). Full macro reference → map row above.

## Cross-Repo Change Protocol (mandatory)
When your session's changes affect other repos (new features they consume, contract/schema changes, corrected docs):
1. **Update `AGENTS.md` in every affected repo** — #1 priority; it's what the next session reads first.
2. **Update `spec/PEERS.md`** in every affected repo that has one (contracts, I/O maps, GUID tables).
3. **Log to MultiVerse** — append to `C:\Repos\MultiVerse\BUILD_LOG.md`: `YYYY-MM-DD | cross-repo | Canary → AffectedRepos | one-line summary`.

**Triggers** — any change that would leave another repo's AGENTS.md/PEERS.md stale: adding a `TestCheckpoint` field → Pigture/CPig AGENTS.md; adding an agent action → repos whose tests use it; changing test conventions → Slop AGENTS.md if it affects JSON authoring. Full text → § Cross-repo protocol.

## Journaling (mandatory while you work)
**Bug fix** → `docs/bugs/NNNN-slug.md` + `CHANGELOG` `### Fixed`; **feature** → `CHANGELOG` + `docs/features/FEATURE_STATUS.md`; **debug session** → `docs/debug-sessions/YYYY-MM-DD-slug.md`; **decision** → `docs/decisions/NNNN-slug.md` (MADR); **research** → `docs/research/YYYY-MM-DD-slug.md`; **build/test run** → append `BUILD_LOG.md`. Frontmatter schema + docs tree → [`AGENTS-DETAIL.md`](AGENTS-DETAIL.md) § Journaling.

## Conventions
- **Commits:** conventional (`feat:` / `fix:` / `docs:` / `test:` / `refactor:` / `chore:`).
- **Release type:** infrastructure — no formal release; milestone tags only (e.g. `canary-v1`).
- **Asks to peers:** file at `docs/asks/<peer>/<NNNN>-slug.md` (§ Asks queue).
- **Skills:** see [`MultiVerse/SKILLS.md`](../MultiVerse/SKILLS.md); supervisor disciplines full text → § Skills.
