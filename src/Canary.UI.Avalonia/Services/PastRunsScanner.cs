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
/// </summary>
public static class PastRunsScanner
{
    public sealed class PastRunRow
    {
        public required string TimestampDir { get; init; }  // e.g. "20260602-130246-5e7295d0"
        public required string ResultJsonPath { get; init; }
        public required string RunDirectory { get; init; }
        public string Status { get; init; } = "?";
        public string DurationDisplay { get; init; } = "";
        public DateTime LastWriteUtc { get; init; }
        public string StartedDisplay => TryParseTimestampDisplay(TimestampDir);

        private static string TryParseTimestampDisplay(string dirname)
        {
            // Format: yyyyMMdd-HHmmss-<hex>; surface as "yyyy-MM-dd HH:mm:ss".
            var first = dirname.IndexOf('-');
            var second = first >= 0 ? dirname.IndexOf('-', first + 1) : -1;
            if (first == 8 && second == 15)
            {
                var date = dirname.Substring(0, 8);
                var time = dirname.Substring(9, 6);
                return $"{date.Substring(0,4)}-{date.Substring(4,2)}-{date.Substring(6,2)} " +
                       $"{time.Substring(0,2)}:{time.Substring(2,2)}:{time.Substring(4,2)}";
            }
            return dirname;
        }
    }

    public static async Task<IReadOnlyList<PastRunRow>> ScanAsync(string workloadsDir, string workloadName, string testName)
    {
        var rows = new List<PastRunRow>();
        if (string.IsNullOrEmpty(workloadsDir)) return rows;

        var runsDir = Path.Combine(workloadsDir, workloadName, "results", testName, "runs");
        if (!Directory.Exists(runsDir)) return rows;

        var dirs = Directory.GetDirectories(runsDir);
        foreach (var dir in dirs)
        {
            var resultJson = Path.Combine(dir, "result.json");
            if (!File.Exists(resultJson)) continue;

            string status = "?";
            string duration = "";
            DateTime lastWrite = File.GetLastWriteTimeUtc(resultJson);
            try
            {
                using var fs = File.OpenRead(resultJson);
                using var doc = await JsonDocument.ParseAsync(fs).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("Status", out var s)) status = s.GetString() ?? "?";
                if (doc.RootElement.TryGetProperty("Duration", out var d))
                {
                    // Duration is serialized as a hh:mm:ss.fffffff string via TimeSpanConverter.
                    duration = d.GetString() ?? "";
                    if (TimeSpan.TryParse(duration, out var ts)) duration = $"{ts.TotalSeconds:F1}s";
                }
            }
            catch { /* keep defaults — the row still surfaces, operator sees the timestamp */ }

            rows.Add(new PastRunRow
            {
                TimestampDir = Path.GetFileName(dir),
                ResultJsonPath = resultJson,
                RunDirectory = dir,
                Status = status,
                DurationDisplay = duration,
                LastWriteUtc = lastWrite,
            });
        }

        // Newest first by timestamp directory name (lexicographic on yyyyMMdd-HHmmss sorts correctly).
        return rows.OrderByDescending(r => r.TimestampDir).ToList();
    }
}
