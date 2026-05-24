using System.Diagnostics;

namespace Canary.Localhost;

// Phase 8 / design §C7 Tier 3 — name-heuristic process listing.
// Enumerates running processes whose name matches known dev-server
// shells (node, deno, bun, python, dotnet, cargo, tauri, vite). The
// design also calls for command-line keyword filtering (dev / serve /
// --port / etc.) — that needs WMI Win32_Process which is slow and
// expensive, so v1 ships name-only filtering with a "may be false
// positive" caveat. Command-line filtering can land in a polish
// follow-up if signal is too noisy.
public static class HeuristicProcessLister
{
    public static readonly string[] DefaultProcessNames = new[]
    {
        "node", "deno", "bun", "npm", "npx", "yarn", "pnpm",
        "python", "python3",
        "dotnet",
        "cargo", "rustc",
        "tauri",
        "ruby", "rails",
        "go",
    };

    public sealed record HeuristicEntry(int Pid, string Name, DateTime? StartTime, string? MainWindowTitle);

    public static List<HeuristicEntry> Enumerate(IEnumerable<string>? namesFilter = null)
    {
        var filter = (namesFilter ?? DefaultProcessNames)
            .Select(n => n.ToLowerInvariant())
            .ToHashSet();

        var entries = new List<HeuristicEntry>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (!filter.Contains(proc.ProcessName.ToLowerInvariant())) { proc.Dispose(); continue; }

                DateTime? start = null;
                string? title = null;
                try { start = proc.StartTime.ToUniversalTime(); } catch { }
                try { title = proc.MainWindowTitle; } catch { }

                entries.Add(new HeuristicEntry(proc.Id, proc.ProcessName, start, title));
            }
            catch { /* PID exited between snapshot and our access — skip */ }
            finally
            {
                try { proc.Dispose(); } catch { }
            }
        }
        return entries.OrderBy(e => e.Name).ThenBy(e => e.Pid).ToList();
    }
}
