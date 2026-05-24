using System.Diagnostics;
using System.Net.NetworkInformation;
using Canary.Telemetry;

namespace Canary.Localhost;

// Tier 1 of design §C7 — passive port enumeration via `netstat -ano` +
// Process.GetProcessById enrichment. Also exposes KillByPortAsync as the
// successor to the duplicate ViteManager.KillStaleListenerAsync helpers.
// Tier 2 (Phase 6) will fold in SpawnRegistry; Tier 3 (Phase 8) will fold
// in name-heuristic listings.
//
// Windows-only — shells out to netstat.exe and taskkill.exe. The Canary
// shell is net8.0-windows.
public sealed class LocalhostManager
{
    // §0.3 default port list — common dev-server ports + Canary's own.
    public static readonly int[] DefaultPorts = { 3000, 3001, 4173, 4200, 5173, 5174, 8000, 8080, 8081, 1420 };

    // Synchronous wrapper around netstat -ano. Returns one entry per
    // LISTENING port that matches the filter. Process enrichment is
    // best-effort (system PIDs, exited PIDs, sandbox restrictions can
    // leave fields null).
    public List<PortEntry> EnumeratePorts(IEnumerable<int>? portFilter = null)
    {
        var filter = portFilter?.ToHashSet();
        var entries = new List<PortEntry>();
        var canaryPid = Process.GetCurrentProcess().Id;

        // Phase 6 / §C7 Tier 2 — Canary-spawn registry, indexed by PID
        // so the Tier 1 netstat enrichment can attach CanarySpawn
        // provenance + intent string.
        var registry = SpawnRegistry.LoadAllSessions().ToDictionary(s => s.Pid, s => s);

        // Pass 1: netstat -ano for every LISTENING socket.
        foreach (var (port, pid) in ReadNetstatListeners())
        {
            if (filter != null && !filter.Contains(port)) continue;

            string? processName = null;
            string? commandLine = null;
            string? workingDir = null;
            DateTime? startTime = null;
            var provenance = pid == canaryPid ? PortProvenance.CanaryHarness : PortProvenance.Unknown;

            if (pid is int p)
            {
                try
                {
                    using var proc = Process.GetProcessById(p);
                    processName = proc.ProcessName;
                    try { startTime = proc.StartTime.ToUniversalTime(); } catch { }
                    try { commandLine = proc.MainModule?.FileName; } catch { /* access denied for system PIDs */ }
                }
                catch (ArgumentException)
                {
                    // PID exited between netstat snapshot and our lookup.
                }

                // Tier 2 overlay — if the registry has this PID, prefer its
                // intent string + mark provenance as CanarySpawn (overrides
                // CanaryHarness for child processes; the harness itself is
                // the parent Canary.exe / Canary.UI.exe).
                if (registry.TryGetValue(p, out var spawn))
                {
                    provenance = PortProvenance.CanarySpawn;
                    commandLine = spawn.Intent;       // surface the human-readable intent
                    workingDir = spawn.WorkingDirectory;
                    if (string.IsNullOrEmpty(processName)) processName = spawn.Name;
                    if (startTime == null) startTime = spawn.SpawnedAt;
                }
            }

            entries.Add(new PortEntry(
                Port: port,
                Pid: pid,
                ProcessName: processName,
                CommandLine: commandLine,
                WorkingDirectory: workingDir,
                StartTime: startTime,
                Provenance: provenance));
        }

        // Pass 2: include Canary's own listeners that aren't a "dev" port —
        // named-pipe surfaces don't show in netstat as TCP, but the MCP
        // server / future SignalR / etc. will. IPGlobalProperties gives
        // the authoritative list of TCP listeners for the current process.
        foreach (var addr in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
        {
            if (entries.Any(e => e.Port == addr.Port)) continue;
            if (filter != null && !filter.Contains(addr.Port)) continue;

            entries.Add(new PortEntry(
                Port: addr.Port,
                Pid: canaryPid,
                ProcessName: Process.GetCurrentProcess().ProcessName,
                CommandLine: null,
                WorkingDirectory: null,
                StartTime: Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                Provenance: PortProvenance.CanaryHarness));
        }

        return entries.OrderBy(e => e.Port).ToList();
    }

    public Task<List<PortEntry>> EnumeratePortsAsync(IEnumerable<int>? portFilter = null, CancellationToken ct = default)
        => Task.Run(() => EnumeratePorts(portFilter), ct);

    // Replaces the duplicate ViteManager.KillStaleListenerAsync helpers
    // in Penumbra + Qualia. Tree-kill default per design §C7.
    public async Task<bool> KillByPortAsync(int port, CancellationToken ct = default)
    {
        var pid = ReadNetstatListeners().FirstOrDefault(p => p.Port == port).Pid;
        if (pid is null) return false;

        var psi = new ProcessStartInfo
        {
            FileName = "taskkill.exe",
            Arguments = $"/F /T /PID {pid.Value}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }

        // Give the OS a moment to release the socket.
        try { await Task.Delay(TimeSpan.FromMilliseconds(300), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        var stillBound = ReadNetstatListeners().Any(p => p.Port == port);
        return !stillBound;
    }

    // Internal so unit tests can stub via friend-assembly seam if needed.
    // For Phase 4 the parsing is exercised through the public API.
    private static IEnumerable<(int Port, int? Pid)> ReadNetstatListeners()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netstat.exe",
            Arguments = "-ano",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        string output;
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) yield break;
            output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
        }
        finally { }

        foreach (var parsed in ParseNetstat(output))
            yield return parsed;
    }

    // Internal for unit-testability — `Canary.Tests` has InternalsVisibleTo.
    internal static IEnumerable<(int Port, int? Pid)> ParseNetstat(string netstatOutput)
    {
        // Lines look like:
        //   TCP    0.0.0.0:3000           0.0.0.0:0              LISTENING       12345
        //   TCP    [::]:3000              [::]:0                 LISTENING       12345
        //   TCP    [::1]:3000             [::]:0                 LISTENING       12345
        //   UDP    0.0.0.0:1234           *:*                                    7890
        // We want LISTENING TCP only.
        foreach (var rawLine in netstatOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) continue;
            if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;

            // Local address is parts[1]. PID is the trailing field.
            var localAddr = parts[1];
            var colon = localAddr.LastIndexOf(':');
            if (colon < 0) continue;
            if (!int.TryParse(localAddr.AsSpan(colon + 1), out var port)) continue;
            if (!int.TryParse(parts[^1], out var pid)) continue;

            yield return (port, pid);
        }
    }
}
