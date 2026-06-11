using System.Text.Json;
using Canary.UI.Avalonia.Services;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

/// <summary>
/// Feedback 2026-06-10-run-history-log-window — the global run-history
/// scanner behind the docked Run History pane.
/// </summary>
[Trait("Category", "Unit")]
public class RunHistoryScannerTests
{
    private static string WriteResult(string runDir, string status, string duration)
    {
        Directory.CreateDirectory(runDir);
        var path = Path.Combine(runDir, "result.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { Status = status, Duration = duration }));
        return path;
    }

    private static string CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-runhist-" + Guid.NewGuid().ToString("N"));

        var alpha = Path.Combine(root, "alpha");
        Directory.CreateDirectory(alpha);
        File.WriteAllText(Path.Combine(alpha, "workload.json"),
            JsonSerializer.Serialize(new { name = "alpha", displayName = "Alpha", agentType = "qualia-cdp", appPath = "" }));
        var results = Path.Combine(alpha, "results");

        // Flat test dir: results/<test>/runs/<stamp>/result.json (two runs).
        WriteResult(Path.Combine(results, "smoke", "runs", "20260601-120000-aaaa1111"), "Passed", "00:00:05");
        WriteResult(Path.Combine(results, "smoke", "runs", "20260603-090000-cccc3333"), "Failed", "00:00:12.5");

        // Suite-nested test dir: results/<suite>/<test>/runs/<stamp>/result.json.
        WriteResult(Path.Combine(results, "nightly", "deep", "runs", "20260602-130000-bbbb2222"), "Passed", "00:01:00");

        // Legacy flat result (pre-Phase-3, no runs/ dir) — excluded.
        Directory.CreateDirectory(Path.Combine(results, "legacy"));
        File.WriteAllText(Path.Combine(results, "legacy", "result.json"),
            JsonSerializer.Serialize(new { Status = "Passed" }));

        // Archived snapshot — excluded (the pane logs runs only).
        WriteResult(Path.Combine(results, "smoke", "archived", "20260601-130000"), "Passed", "00:00:01");

        // Abandoned run dir without result.json — excluded.
        Directory.CreateDirectory(Path.Combine(results, "smoke", "runs", "20260604-100000-dddd4444"));

        // Non-workload sibling dir (no workload.json) — excluded entirely.
        WriteResult(Path.Combine(root, "not-a-workload", "results", "x", "runs", "20260605-110000-eeee5555"), "Passed", "00:00:01");

        return root;
    }

    [Fact]
    public async Task Scan_FindsRunsAcrossLayouts_NewestFirst()
    {
        var root = CreateFixture();
        try
        {
            var rows = await RunHistoryScanner.ScanAsync(root);

            Assert.Equal(3, rows.Count);
            Assert.Equal(new[]
            {
                "20260603-090000-cccc3333",   // smoke, flat
                "20260602-130000-bbbb2222",   // deep, suite-nested
                "20260601-120000-aaaa1111",   // smoke, flat
            }, rows.Select(r => r.TimestampDir).ToArray());

            var failed = rows[0];
            Assert.Equal("alpha", failed.Workload);
            Assert.Equal("", failed.Suite);
            Assert.Equal("smoke", failed.Test);
            Assert.Equal("Failed", failed.Status);
            Assert.Equal("12.5s", failed.DurationDisplay);
            Assert.Equal("2026-06-03 09:00:00", failed.StartedDisplay);
            Assert.EndsWith("REPORT.md", failed.ReportPath);

            var nested = rows[1];
            Assert.Equal("nightly", nested.Suite);
            Assert.Equal("deep", nested.Test);
            Assert.Equal("60.0s", nested.DurationDisplay);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scan_CapsRowCount()
    {
        var root = CreateFixture();
        try
        {
            var rows = await RunHistoryScanner.ScanAsync(root, maxRows: 1);
            Assert.Single(rows);
            Assert.Equal("20260603-090000-cccc3333", rows[0].TimestampDir);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scan_MissingDir_ReturnsEmpty()
    {
        var rows = await RunHistoryScanner.ScanAsync(Path.Combine(Path.GetTempPath(), "canary-runhist-does-not-exist"));
        Assert.Empty(rows);
    }
}
