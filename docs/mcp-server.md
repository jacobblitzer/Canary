---
title: "Canary MCP server"
tags: [mcp, claude-code, integration]
status: shipped
project: canary
component: mcp-server
date: 2026-05-24
---

# Canary MCP server

`Canary.McpServer.exe` is a Model Context Protocol server that exposes
Canary state to Claude Code (or any MCP client) over stdio JSON-RPC.
Phase 6 of the debug-overhaul implementation (design §C6).

## Tool surface

| Tool | What it returns |
|---|---|
| `list_feedback` | Items in `docs/feedback/{inbox,triaged,resolved}/`. Filter by `status`, `limit`. |
| `get_feedback` | Full body + frontmatter + sidecar PNG/JSON paths for one item by `id`. |
| `mark_feedback_triaged` | Moves an item + its sidecar from `inbox/` to `triaged/`. Updates frontmatter. |
| `list_recent_runs` | Recent test runs (REPORT.md per `runs/<timestamp>/` from Phase 3). Filter by `workload`, `verdict`, `limit`. |
| `get_run_report` | Full `REPORT.md` contents for a specific `runId`. |
| `list_localhost_ports` | TCP listeners on common dev-server ports (Tier 1 netstat + Tier 2 spawn registry overlay). |
| `list_running_apps` | Canary-spawned processes from the cross-session spawn registry. |
| `kill_localhost_port` | Tree-kill the holder of a TCP port. |
| `list_sessions` | Supervised sessions (one row per `workloads/<w>/sessions/<id>/session.json`). Filter by `workload`, `limit`. Added 2026-05-27 with the supervised-session feature. |
| `get_session_report` | Full `SESSION_REPORT.md` contents for a specific `sessionId`. Added 2026-05-27. |
| `get_session_manifest` | `manifest.json` verbatim for a `sessionId`: opened file + SHA256, machine, app+PID, applied env (incl. `PENUMBRA_SESSION_REF`), exit record, harvested Penumbra SHAs. Added R1.6 (2026-07-03). |
| `get_session_telemetry` | Raw NDJSON lines from a session's `telemetry.ndjson` (or `telemetry-prior.ndjson` with `prior:true`), filtered by `eventPrefix` on `Data.event` (Kind fallback), last-N `tail` (default 200, max 2000). Added R1.6. See `docs/session-flight-recorder.md`. |

## Setup

The MCP server is built as part of `dotnet build Canary.sln`:

```
src/Canary.McpServer/bin/{Debug|Release}/net8.0-windows/Canary.McpServer.exe
```

Register it in your Claude Code `.mcp.json` (project-scoped) or
`~/.claude.json` (user-scoped):

```json
{
  "mcpServers": {
    "canary": {
      "command": "C:\\Repos\\Canary\\src\\Canary.McpServer\\bin\\Release\\net8.0-windows\\Canary.McpServer.exe",
      "args": []
    }
  }
}
```

Restart Claude Code. The tools appear as `mcp__canary__list_feedback`,
`mcp__canary__list_recent_runs`, etc.

## Discovery roots

The server walks up from `AppContext.BaseDirectory` to find:
- `docs/feedback/` — feedback inbox (`list_feedback` / `get_feedback`).
- `workloads/` — test results trees (`list_recent_runs` / `get_run_report`).

This means launching the server from the deployed `bin/Release/`
location finds the surrounding repo root naturally. If you copy the
exe elsewhere, set `cwd` or `args` so the server can locate the
intended `docs/feedback/` + `workloads/`.

**`CANARY_WORKLOADS_DIR` override (R1.6, 2026-07-03):** when set (and the directory exists)
it takes precedence over the walk-up discovery for EVERY workloads-reading tool
(`list_recent_runs`, `get_run_report`, `list_sessions`, `get_session_report`,
`get_session_manifest`, `get_session_telemetry`). Intended for serving a workloads tree the
exe does not live under and for hermetic tests — a stale value silently redirects all those
tools, so unset it when done.

## Spawn registry (Tier 2 localhost provenance)

`list_running_apps` + `list_localhost_ports` read from the spawn
registry at:

```
%LocalAppData%\Canary\claude-spawns\<session-id>.json
```

Each Canary process (Canary.exe CLI, Canary.UI.exe, this MCP server)
writes one session file as it spawns child processes (Vite, Chrome).
The MCP server merges every session file at query time so the operator
sees all currently-running Canary-spawned dev servers regardless of
which Canary instance launched them.

Stale session files are harmless (Tier 1 netstat still works); a
`SpawnRegistry.PurgeOldSessions(maxAge)` helper is available for
ops scripts.

## Smoke test

```bash
printf '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}\n{"jsonrpc":"2.0","id":2,"method":"tools/list"}\n' \
  | Canary.McpServer.exe
```

Expect two JSON-RPC responses on stdout: the server's `initialize`
result (protocolVersion 2024-11-05, server name `canary`) and a
`tools/list` result with 12 tool entries.

## Wire protocol

Self-contained implementation of MCP 2024-11-05 over stdio
(newline-delimited JSON-RPC 2.0). See `src/Canary.McpServer/McpProtocol.cs`
for the message handler. We chose self-contained over the official
`ModelContextProtocol` NuGet package to keep zero external dependencies
and keep the wire shape readable from the source.
