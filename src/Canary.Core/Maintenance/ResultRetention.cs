namespace Canary.Maintenance;

// Phase 3 / §C2 — per-run dir retention. Walks
// `workloads/<w>/results/[<suite>/]<test>/runs/*/` and deletes any
// timestamped subdir older than the configured age. The flat-layout
// legacy artifacts (`<test>/result.json`, `<test>/baselines/`,
// `<test>/candidates/`, `<test>/diffs/`, `<test>/composite.png`) are
// untouched — those overwrite per-run today and stay current by
// definition.
//
// Default retention: 14 days, matching STANDARD.md §16's candidates +
// diffs auto-clean window.
public static class ResultRetention
{
    public static TimeSpan DefaultRetention { get; } = TimeSpan.FromDays(14);

    public sealed class PurgeReport
    {
        public int DirsScanned { get; set; }
        public int DirsPurged { get; set; }
        public long BytesFreed { get; set; }
        public List<string> Errors { get; } = new();
    }

    // Synchronous because the work is filesystem-bound and typically tiny;
    // callers (Phase 3 wiring is a one-shot invocation at run start) don't
    // need an async surface.
    public static PurgeReport PurgeOlderThan(string workloadsDir, TimeSpan maxAge)
    {
        var report = new PurgeReport();
        if (!Directory.Exists(workloadsDir)) return report;

        var cutoffUtc = DateTime.UtcNow - maxAge;

        // workloadsDir / <workload> / results / [<suite> /] <test> / runs / <timestamp>
        foreach (var runsDir in Directory.EnumerateDirectories(workloadsDir, "runs", SearchOption.AllDirectories))
        {
            // Only consider runs/ dirs that look like they're under a results tree.
            if (!runsDir.Contains($"{Path.DirectorySeparatorChar}results{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var timestampDir in Directory.EnumerateDirectories(runsDir))
            {
                report.DirsScanned++;
                try
                {
                    var lastWrite = Directory.GetLastWriteTimeUtc(timestampDir);
                    if (lastWrite >= cutoffUtc) continue;

                    var size = DirectorySize(timestampDir);
                    Directory.Delete(timestampDir, recursive: true);
                    report.DirsPurged++;
                    report.BytesFreed += size;
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"{timestampDir}: {ex.Message}");
                }
            }
        }

        return report;
    }

    private static long DirectorySize(string path)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { /* skip */ }
            }
        }
        catch { /* skip */ }
        return total;
    }
}
