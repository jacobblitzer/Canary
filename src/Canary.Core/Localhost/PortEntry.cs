namespace Canary.Localhost;

// Per design §C7 Tier 1 — single row in the Localhost tab. Phase 4
// populates Pid + ProcessName via netstat + Process.GetProcessById.
// CommandLine + WorkingDirectory + StartTime are best-effort (some
// system-owned PIDs deny Process.MainModule access; fallback values
// stay null). Tier 2 (Phase 6) adds CanarySpawn provenance via
// SpawnRegistry. Tier 3 (Phase 8) adds DevServerHeuristic.
public sealed record PortEntry(
    int Port,
    int? Pid,
    string? ProcessName,
    string? CommandLine,
    string? WorkingDirectory,
    DateTime? StartTime,
    PortProvenance Provenance);

public enum PortProvenance
{
    // Default — port is bound but we couldn't classify the listener.
    Unknown,

    // Tier 3 (Phase 8): name + command-line matched a dev-server heuristic
    // (node + dev / serve / --port etc.). Caveat: may be false positive.
    DevServerHeuristic,

    // Tier 2 (Phase 6): registered in SpawnRegistry by Canary code. Full
    // provenance — we know the intent and the originating workload.
    CanarySpawn,

    // The Canary process itself (named-pipe servers, MCP server, etc.).
    // Filtered at enumeration time via IPGlobalProperties.GetActiveTcpListeners
    // matched against Canary's own PID.
    CanaryHarness,
}
