using Canary.Config;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

/// <summary>
/// Deployment campaign Phase 2b C3 — the one derivation, and the gate that makes a
/// ledgered-but-unreachable baseline loud instead of green.
/// </summary>
public class ResultPathsAndGateTests
{
    private const string Root = @"C:\wl";

    // --- the contract ------------------------------------------------------

    [Trait("Category", "Unit")]
    [Fact]
    public void TestDir_NeverContainsASuiteSegment()
    {
        var dir = ResultPaths.TestDir(Root, "rhino", "cpig-00-smoke-ping");

        Assert.Equal(Path.Combine(Root, "rhino", "results", "cpig-00-smoke-ping"), dir);
    }

    // The property that actually mattered: the SAME test resolves to the SAME directory
    // however it was invoked. Before Phase 2b, `--suite cpig` and `--test cpig-00` landed
    // in different places, so an approval made one way was invisible the other way.
    [Trait("Category", "Unit")]
    [Fact]
    public void TheSameTest_ResolvesIdentically_HoweverItWasInvoked()
    {
        var viaSuite = ResultPaths.BaselinePath(Root, "rhino", "t", "front");
        var viaTest = ResultPaths.BaselineIn(ResultPaths.TestDir(Root, "rhino", "t"), "front");

        Assert.Equal(viaSuite, viaTest);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RollupDir_WithoutASuite_IsTheResultsRoot(string? suite)
    {
        // Load-bearing: the old helpers keyed off `suiteName != null`, and because
        // Path.Combine drops empty segments, "" read as "a suite was supplied" and
        // silently produced the unscoped path anyway. Whitespace must not be a third
        // behaviour.
        Assert.Equal(ResultPaths.ResultsRoot(Root, "rhino"), ResultPaths.RollupDir(Root, "rhino", suite));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void RollupDir_WithASuite_IsASiblingOfTestDirs()
    {
        var rollup = ResultPaths.RollupDir(Root, "rhino", "cpig");
        var test = ResultPaths.TestDir(Root, "rhino", "cpig-00");

        Assert.Equal(Path.GetDirectoryName(rollup), Path.GetDirectoryName(test));
    }

    // --- G4a: one derivation, enforced against the source tree -------------

    // Two byte-identical GetTestDirectory copies were the root cause of this whole class,
    // and a unification that edits only the helpers leaves the UI reading the old shape.
    // This fails on any NEW composition site rather than trusting a convention.
    [Trait("Category", "Unit")]
    [Fact]
    public void NoProductionFile_ComposesAResultsPath_ExceptResultPaths()
    {
        var srcRoot = SourceRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
            if (rel.EndsWith("ResultPaths.cs", StringComparison.Ordinal)) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("\"results\"", StringComparison.Ordinal)) continue;
                // A depth-tolerant SCAN of an existing tree is not a derivation; those are
                // marked, and the marker is what makes this guard maintainable rather than
                // something people switch off.
                if (lines[i].Contains("not a derivation", StringComparison.Ordinal)) continue;
                offenders.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "a result path may only be composed in ResultPaths.cs; found:\n  " + string.Join("\n  ", offenders));
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Canary.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    // --- G1 + G2: the arming gate -----------------------------------------

    private static (TestRunner Runner, string Root, string TestDir) Rig(params BaselineRow[] rows)
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-gate-" + Guid.NewGuid().ToString("N")[..12]);
        var testDir = ResultPaths.TestDir(root, "w", "t");
        Directory.CreateDirectory(Path.Combine(testDir, ResultPaths.BaselinesDir));

        var ledger = new BaselineLedger { Workload = "w", Rows = rows.ToList() };
        var runner = new TestRunner(new ProcessManager(), root, new SilentLogger(), ledger);
        return (runner, root, testDir);
    }

    private sealed class SilentLogger : ITestLogger
    {
        public bool Verbose => false;
        public void Log(string message) { }
        public void LogStatus(string symbol, string message, TestStatusLevel level) { }
        public void LogSummary(string message) { }
    }

    private static BaselineRow Row(string cp = "front") =>
        new() { Test = "t", Checkpoint = cp, Mode = "pixel-diff", Sha256 = "x", ApprovedUtc = "2026-01-01T00:00:00Z" };

    [Trait("Category", "Unit")]
    [Fact]
    public void LedgeredAndPresent_Proceeds()
    {
        var (runner, _, testDir) = Rig(Row());
        File.WriteAllText(ResultPaths.BaselineIn(testDir, "front"), "png");

        var gate = runner.GateLedgeredCheckpoint(new TestCheckpoint { Name = "front" }, "t", testDir);

        Assert.Null(gate);
    }

    // THE defect, at unit scale: the baseline the ledger promises is not where this run
    // reads it. Before Phase 2b this reported New, and New is excluded from the exit code.
    [Trait("Category", "Unit")]
    [Fact]
    public void LedgeredButAbsent_IsFailed_NotNew()
    {
        var (runner, _, testDir) = Rig(Row());

        var gate = runner.GateLedgeredCheckpoint(new TestCheckpoint { Name = "front" }, "t", testDir);

        Assert.NotNull(gate);
        Assert.Equal(TestStatus.Failed, gate!.Status);
        Assert.Contains("ledgered", gate.ErrorMessage!);
    }

    // The counter-case that must keep working: a genuinely new test has no ledger row, so
    // it still reports New at exit 0. A guard that broke this would be unusable.
    [Trait("Category", "Unit")]
    [Fact]
    public void UnledgeredAndAbsent_IsNotGated()
    {
        var (runner, _, testDir) = Rig();

        var gate = runner.GateLedgeredCheckpoint(new TestCheckpoint { Name = "front" }, "t", testDir);

        Assert.Null(gate);
    }

    // G2. This is the half a check placed inside `if (!File.Exists(baselinePath))` CANNOT
    // catch, because the capture early-return fires before that path is ever computed.
    // A one-word JSON edit would otherwise disarm a ledgered comparison to Passed with the
    // approved image still on disk.
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("capture")]
    [InlineData("none")]
    [InlineData("off")]
    [InlineData("vlm")]
    public void LedgeredButDisarmedInContent_IsFailed_EvenWithTheBaselinePresent(string mode)
    {
        var (runner, _, testDir) = Rig(Row());
        File.WriteAllText(ResultPaths.BaselineIn(testDir, "front"), "png");

        var gate = runner.GateLedgeredCheckpoint(
            new TestCheckpoint { Name = "front", Mode = mode }, "t", testDir);

        Assert.NotNull(gate);
        Assert.Equal(TestStatus.Failed, gate!.Status);
        Assert.Contains(mode, gate.ErrorMessage!);
    }

    // ...but a RUN FLAG is one operator asking for one different run, not a disarm, so it
    // must not fail. Content changes are permanent and silent; flags are neither.
    [Trait("Category", "Unit")]
    [Fact]
    public void ModeOverrideVlm_DoesNotTripTheGate_ItIsNotADisarm()
    {
        var (runner, _, testDir) = Rig(Row());
        runner.ModeOverride = ModeOverride.Vlm;

        var gate = runner.GateLedgeredCheckpoint(new TestCheckpoint { Name = "front" }, "t", testDir);

        Assert.Null(gate);
    }
}
