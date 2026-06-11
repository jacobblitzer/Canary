using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Canary.Orchestration;

namespace Canary.UI.Avalonia.Services;

/// <summary>
/// Phase 14.3 — enumerates a test's past runs by scanning
/// <c>{workloadsDir}/{workloadName}/results/{testName}/runs/*/result.json</c>
/// and returning one row per timestamped directory. Used by
/// <c>PastRunsViewModel</c> to populate the Past Runs DataGrid in the test
/// details side panel.
///
/// Phase 14.6 — also enumerates <c>archived/{stamp}/result.json</c>
/// (snapshots written by the SaveSnapshot button on the runner toolbar and
/// the ResultsViewer header) so the operator can find snapshots in the same
/// surface as runs. Snapshots get <see cref="RowKind.Snapshot"/>.
/// </summary>
public static class PastRunsScanner
{
    public enum RowKind { Run, Snapshot }

    public sealed class PastRunRow
    {
        public required string TimestampDir { get; init; }  // e.g. "20260602-130246-5e7295d0" (run) or "20260602-073700" (snapshot)
        public required string ResultJsonPath { get; init; }
        public required string RunDirectory { get; init; }
        public RowKind Kind { get; init; } = RowKind.Run;
        public string KindDisplay => Kind == RowKind.Snapshot ? "Snapshot" : "Run";
        public string Status { get; init; } = "?";
        public string DurationDisplay { get; init; } = "";
        public DateTime LastWriteUtc { get; init; }
        public string StartedDisplay => ParseTimestampDisplay(TimestampDir);
    }

    /// <summary>Shared with <see cref="RunHistoryScanner"/>.</summary>
    internal static string ParseTimestampDisplay(string dirname)
    {
        // Run dirs: yyyyMMdd-HHmmss-<hex>. Snapshot dirs: yyyyMMdd-HHmmss
        // (no hex suffix). Both parse cleanly from the first 15 chars.
        if (dirname.Length >= 15 && dirname[8] == '-')
        {
            var date = dirname.Substring(0, 8);
            var time = dirname.Substring(9, 6);
            if (date.All(char.IsDigit) && time.All(char.IsDigit))
                return $"{date.Substring(0,4)}-{date.Substring(4,2)}-{date.Substring(6,2)} " +
                       $"{time.Substring(0,2)}:{time.Substring(2,2)}:{time.Substring(4,2)}";
        }
        return dirname;
    }

    public static async Task<IReadOnlyList<PastRunRow>> ScanAsync(string workloadsDir, string workloadName, string testName)
    {
        var rows = new List<PastRunRow>();
        if (string.IsNullOrEmpty(workloadsDir)) return rows;

        var testDir = Path.Combine(workloadsDir, workloadName, "results", testName);
        if (!Directory.Exists(testDir)) return rows;

        await ScanKindAsync(rows, Path.Combine(testDir, "runs"), RowKind.Run).ConfigureAwait(false);
        await ScanKindAsync(rows, Path.Combine(testDir, "archived"), RowKind.Snapshot).ConfigureAwait(false);

        // Newest first by timestamp prefix. Runs have an extra -<hex> suffix
        // that sorts after the bare hms in snapshots, so a same-timestamp
        // run + snapshot pair will sort run-after-snapshot — fine.
        return rows.OrderByDescending(r => r.TimestampDir).ToList();
    }

    private static async Task ScanKindAsync(List<PastRunRow> rows, string parentDir, RowKind kind)
    {
        if (!Directory.Exists(parentDir)) return;
        foreach (var dir in Directory.GetDirectories(parentDir))
        {
            // Snapshots written by TestRunnerViewModel.SaveSnapshot DON'T include
            // result.json (only candidates / manual-captures / logs / *.json
            // sidecars); snapshots written by ResultsViewerViewModel.SaveSnapshot
            // from a past-run load DO include it. Either way we add the row so
            // the operator sees the snapshot exists; the viewer surfaces
            // "(load failed)" when ResultJson is missing but the operator can
            // still navigate to archived/<stamp>/ from the row.
            var resultJson = Path.Combine(dir, "result.json");
            string status = kind == RowKind.Snapshot ? "(snapshot)" : "?";
            string duration = "";
            DateTime lastWrite = File.Exists(resultJson)
                ? File.GetLastWriteTimeUtc(resultJson)
                : Directory.GetLastWriteTimeUtc(dir);

            if (File.Exists(resultJson))
            {
                try
                {
                    using var fs = File.OpenRead(resultJson);
                    using var doc = await JsonDocument.ParseAsync(fs).ConfigureAwait(false);
                    if (doc.RootElement.TryGetProperty("Status", out var s)) status = s.GetString() ?? status;
                    if (doc.RootElement.TryGetProperty("Duration", out var d))
                    {
                        duration = d.GetString() ?? "";
                        if (TimeSpan.TryParse(duration, out var ts)) duration = $"{ts.TotalSeconds:F1}s";
                    }
                }
                catch { /* keep defaults — row still surfaces */ }
            }
            else if (kind == RowKind.Run)
            {
                // Runs should always have result.json — missing means abandoned mid-write.
                continue;
            }

            rows.Add(new PastRunRow
            {
                TimestampDir = Path.GetFileName(dir),
                ResultJsonPath = resultJson,
                RunDirectory = dir,
                Kind = kind,
                Status = status,
                DurationDisplay = duration,
                LastWriteUtc = lastWrite,
            });
        }
    }
}
