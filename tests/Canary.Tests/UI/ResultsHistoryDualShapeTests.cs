using Canary.Orchestration;
using Canary.UI.Services;
using Xunit;

namespace Canary.Tests.UI;

// Verifies the Phase 3 / §C2 dual-shape scan in ResultsHistory: legacy
// `<test>/result.json` and the new `<test>/runs/<timestamp>/result.json`
// both surface in the same scan.
public class ResultsHistoryDualShapeTests : IDisposable
{
    private readonly string _root;
    public ResultsHistoryDualShapeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "canary-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }
    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task Scan_LegacyLayoutOnly_PicksUpResult()
    {
        var dir = Path.Combine(_root, "wl", "results", "alpha");
        Directory.CreateDirectory(dir);
        await TestResultSerializer.SaveAsync(
            new TestResult { TestName = "alpha", Workload = "wl", Status = TestStatus.Passed },
            Path.Combine(dir, "result.json"));

        var entries = await new ResultsHistory().ScanAsync(_root, "wl");

        Assert.Single(entries);
        Assert.Equal("alpha", entries[0].Result.TestName);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task Scan_PerRunLayoutOnly_PicksUpResult()
    {
        var dir = Path.Combine(_root, "wl", "results", "alpha", "runs", "20260524-142300-a3f1");
        Directory.CreateDirectory(dir);
        await TestResultSerializer.SaveAsync(
            new TestResult { TestName = "alpha", Workload = "wl", Status = TestStatus.Failed },
            Path.Combine(dir, "result.json"));

        var entries = await new ResultsHistory().ScanAsync(_root, "wl");

        Assert.Single(entries);
        Assert.Equal("alpha", entries[0].Result.TestName);
        Assert.Equal(TestStatus.Failed, entries[0].Result.Status);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task Scan_BothLayouts_ReturnsBoth()
    {
        // Legacy flat
        var legacyDir = Path.Combine(_root, "wl", "results", "alpha");
        Directory.CreateDirectory(legacyDir);
        await TestResultSerializer.SaveAsync(
            new TestResult { TestName = "alpha", Workload = "wl", Status = TestStatus.Passed },
            Path.Combine(legacyDir, "result.json"));

        // New per-run (two runs)
        var runDir1 = Path.Combine(legacyDir, "runs", "20260524-142300-aaaa");
        Directory.CreateDirectory(runDir1);
        await TestResultSerializer.SaveAsync(
            new TestResult { TestName = "alpha", Workload = "wl", Status = TestStatus.Failed },
            Path.Combine(runDir1, "result.json"));

        var runDir2 = Path.Combine(legacyDir, "runs", "20260525-090000-bbbb");
        Directory.CreateDirectory(runDir2);
        await TestResultSerializer.SaveAsync(
            new TestResult { TestName = "alpha", Workload = "wl", Status = TestStatus.Passed },
            Path.Combine(runDir2, "result.json"));

        var entries = await new ResultsHistory().ScanAsync(_root, "wl");

        // 1 legacy + 2 per-run = 3 entries (no dedup at this layer; the UI
        // chooses which to show).
        Assert.Equal(3, entries.Count);

        // Sorted by timestamp desc — the most recently written file wins.
        // Both per-run dirs are newer than the legacy file we wrote first.
        var paths = entries.Select(e => e.FilePath).ToList();
        Assert.Contains(paths, p => p.Contains("20260524-142300-aaaa"));
        Assert.Contains(paths, p => p.Contains("20260525-090000-bbbb"));
        Assert.Contains(paths, p => !p.Contains($"runs{Path.DirectorySeparatorChar}"));
    }
}
