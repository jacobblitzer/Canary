---
title: "Use named pipes with JSON-RPC for IPC"
date: 2026-04-04
tags:
  - decision
  - architecture
status: accepted
project: canary
---

# Use named pipes with JSON-RPC for IPC

## Context and Problem Statement
Canary's two-process model requires the harness (`canary.exe`) to communicate with agents running inside target applications. The IPC mechanism must be low-latency, bidirectional, and work reliably on Windows without port conflicts or firewall issues.

## Considered Options
1. **HTTP REST** -- harness runs an HTTP server, agents POST results
2. **Raw TCP sockets** -- custom protocol over localhost TCP
3. **Named pipes with JSON-RPC** -- OS-level IPC with structured messages

## Decision Outcome
Chosen option: **Named pipes with JSON-RPC** because:
- No port conflicts (pipe names are process-scoped: `canary-{workload}-{pid}`)
- No firewall issues (pipes are kernel objects, not network)
- Low latency for local IPC
- JSON-RPC is a known protocol that maps cleanly to request/response patterns
- .NET has built-in `System.IO.Pipes` support on both `net8.0` and `net48`

### Consequences
- Good: Zero external dependencies, works on both .NET 8 and .NET Framework 4.8
- Good: Pipe names are unique per process, so multiple test runs don't collide
- Bad: Windows-only (acceptable -- Canary targets Windows desktop apps)
- Bad: Debugging is harder than HTTP (no browser dev tools), mitigated by structured logging

## Related
- Spec: `spec/ARCHITECTURE.md` (IPC Protocol section)
- Phase: 1 (Named Pipe IPC + Agent Protocol)
