using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Canary.UI.Avalonia.Services;

/// <summary>
/// Feedback 2026-06-10-run-history-log-window — enumerates EVERY past run
/// across ALL workloads for the docked Run History pane, newest first.
///
/// Walks <c>{workloadsDir}/{workload}/results/</c> handling both layouts the
/// Phase 3 per-run writer produced over time:
/// <list type="bullet">
///   <item><c>results/{test}/runs/{stamp}/result.json</c></item>
///   <item><c>results/{suite}/{test}/runs/{stamp}/result.json</c></item>
/// </list>
/// A results child with a <c>runs/</c> subdir is a test dir; otherwise its
/// children are probed one level deeper for the suite-nested form. Legacy
/// pre-Phase-3 flat <c>results/{test}/result.json</c> files (no timestamped
/// run dir) and <c>archived/</c> snapshots are intentionally excluded — the
/// pane is a log of runs.
/// </summary>
public static class RunHistoryScanner
{
    public sealed class RunHistoryRow
    {
        public required string Workload { get; init; }
        public string Suite { get; init; } = "";
        public required string Test { get; init; }
        public required string TimestampDir { get; init; }
        public string Status { get; init; } = "?";
        public string DurationDisplay { get; init; } = "";
        public required string RunDirectory { get; init; }
        public required string ResultJsonPath { get; init; }
        public string StartedDisplay => PastRunsScanner.ParseTimestampDisplay(TimestampDir);
        public string ReportPath => Path.Combine(RunDirectory, "REPORT.md");
    }

    public static async Task<IReadOnlyList<RunHistoryRow>> ScanAsync(string workloadsDir, int maxRows = 500)
    {
        var rows = new List<RunHistoryRow>();
        if (string.IsNullOrEmpty(workloadsDir) || !Directory.Exists(workloadsDir)) return rows;

        foreach (var workloadDir in Directory.GetDirectories(workloadsDir))
        {
            // Same workload test as WorkloadExplorer — skip non-workload dirs.
            if (!File.Exists(Path.Combine(workloadDir, "workload.json"))) continue;
            var resultsDir = Path.Combine(workloadDir, "results");
            if (!Directory.Exists(resultsDir)) continue;
            var workloadName = Path.GetFileName(workloadDir);

            foreach (var child in Directory.GetDirectories(resultsDir))
            {
                if (Directory.Exists(Path.Combine(child, "runs")))
                {
                    await ScanTestDirAsync(rows, workloadName, suite: "", child).ConfigureAwait(false);
                }
                else
                {
                    foreach (var nested in Directory.GetDirectories(child))
                    {
                        if (Directory.Exists(Path.Combine(nested, "runs")))
                            await ScanTestDirAsync(rows, workloadName, Path.GetFileName(child), nested).ConfigureAwait(false);
                    }
                }
            }
        }

        // Timestamp prefix (yyyyMMdd-HHmmss) sorts chronologically as text.
        return rows.OrderByDescending(r => r.TimestampDir).Take(maxRows).ToList();
    }

    private static async Task ScanTestDirAsync(List<RunHistoryRow> rows, string workload, string suite, string testDir)
    {
        foreach (var runDir in Directory.GetDirectories(Path.Combine(testDir, "runs")))
        {
            var resultJson = Path.Combine(runDir, "result.json");
            // Runs always write result.json — missing means abandoned mid-write.
            if (!File.Exists(resultJson)) continue;

            string status = "?";
            string duration = "";
            try
            {
                using var fs = File.OpenRead(resultJson);
                using var doc = await JsonDocument.ParseAsync(fs).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("Status", out var s)) status = s.GetString() ?? status;
                if (doc.RootElement.TryGetProperty("Duration", out var d)
                    && TimeSpan.TryParse(d.GetString(), out var ts))
                {
                    duration = $"{ts.TotalSeconds:F1}s";
                }
            }
            catch { /* keep defaults — row still surfaces */ }

            rows.Add(new RunHistoryRow
            {
                Workload = workload,
                Suite = suite,
                Test = Path.GetFileName(testDir),
                TimestampDir = Path.GetFileName(runDir),
                Status = status,
                DurationDisplay = duration,
                RunDirectory = runDir,
                ResultJsonPath = resultJson,
            });
        }
    }
}
