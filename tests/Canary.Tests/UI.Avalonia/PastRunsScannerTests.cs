using System.IO;
using System.Threading.Tasks;
using Canary.UI.Avalonia.Services;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class PastRunsScannerTests
{
    [Fact]
    public async Task ScanAsync_returns_empty_when_no_runs_dir()
    {
        var tmp = NewTempDir();
        try
        {
            var rows = await PastRunsScanner.ScanAsync(tmp, "rhino", "cpig-kin-01");
            Assert.Empty(rows);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    [Fact]
    public async Task ScanAsync_lists_newest_first_with_status_and_duration()
    {
        var tmp = NewTempDir();
        try
        {
            var runsDir = Path.Combine(tmp, "rhino", "results", "cpig-kin-15", "runs");
            Directory.CreateDirectory(runsDir);
            CreateRun(runsDir, "20260601-100000-aaaaaaaa", "Passed", "00:00:35.5610710");
            CreateRun(runsDir, "20260602-130246-bbbbbbbb", "Crashed", "00:03:07.3351820");
            CreateRun(runsDir, "20260602-130430-cccccccc", "New",     "00:00:39.8014961");

            var rows = await PastRunsScanner.ScanAsync(tmp, "rhino", "cpig-kin-15");
            Assert.Equal(3, rows.Count);
            // Newest first (lexicographic-on-yyyyMMdd-HHmmss works because the
            // format is fixed-width).
            Assert.Equal("20260602-130430-cccccccc", rows[0].TimestampDir);
            Assert.Equal("New", rows[0].Status);
            Assert.Equal("39.8s", rows[0].DurationDisplay);
            Assert.Equal("2026-06-02 13:04:30", rows[0].StartedDisplay);
            Assert.Equal("20260601-100000-aaaaaaaa", rows[2].TimestampDir);
            Assert.Equal("Passed", rows[2].Status);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    [Fact]
    public async Task ScanAsync_skips_runs_dir_without_result_json()
    {
        var tmp = NewTempDir();
        try
        {
            var runsDir = Path.Combine(tmp, "rhino", "results", "cpig-kin-15", "runs");
            var orphanDir = Path.Combine(runsDir, "20260602-999999-orphaned");
            Directory.CreateDirectory(orphanDir);
            // No result.json inside.
            CreateRun(runsDir, "20260602-130430-cccccccc", "Passed", "00:00:30.0000000");

            var rows = await PastRunsScanner.ScanAsync(tmp, "rhino", "cpig-kin-15");
            Assert.Single(rows);
            Assert.Equal("20260602-130430-cccccccc", rows[0].TimestampDir);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void CreateRun(string runsDir, string timestamp, string status, string durationIso)
    {
        var dir = Path.Combine(runsDir, timestamp);
        Directory.CreateDirectory(dir);
        var json = $$"""
        {
          "TestName": "cpig-kin-15-watt-straight-line",
          "Workload": "rhino",
          "Status": "{{status}}",
          "CheckpointResults": [],
          "CompositeImagePath": null,
          "ErrorMessage": null,
          "Duration": "{{durationIso}}"
        }
        """;
        File.WriteAllText(Path.Combine(dir, "result.json"), json);
    }

    private static string NewTempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "canary-past-runs-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }
}
