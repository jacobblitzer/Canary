using Canary.Cli;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Cli;

/// <summary>
/// Deployment campaign Phase 3 — the completeness gate.
/// </summary>
/// <remarks>
/// <para>
/// These pin the single most expensive behaviour in the harness. Three things composed
/// into it: a suite naming an absent test <b>silently shrank</b>, a missing baseline
/// yielded <c>New</c>, and <c>New</c> is excluded from the exit code. A machine carrying
/// 1 of 51 tests and no baselines <b>reported a pass</b>.
/// </para>
/// <para>
/// A green harness that asserts nothing is worse than no harness, because it retires the
/// question without answering it. Every test here exists so that cannot come back
/// silently.
/// </para>
/// </remarks>
public class DoctorAndShortSuiteTests
{
    /// <summary>A rig that is COMPLETE, so that anything a test then breaks is the only fault.</summary>
    /// <remarks>
    /// The workload config is written here rather than per-test on purpose. Without it,
    /// <c>doctor</c> errored on the absent config, and
    /// <see cref="Doctor_ExitsNonZero_WhenASuiteIsShort"/> exited 1 for that reason instead
    /// of the short suite - it passed with the completeness gate deleted. Mutation-testing
    /// the gate is what surfaced it: only one of the three guards fired. A guard that is
    /// green for an unrelated reason is indistinguishable from no guard.
    /// </remarks>
    private static string NewRig()
    {
        var d = Path.Combine(Path.GetTempPath(), "canary-rig-" + Guid.NewGuid().ToString("N")[..12]);
        var root = Path.Combine(d, "workloads");
        Directory.CreateDirectory(Path.Combine(root, "w", "tests"));
        Directory.CreateDirectory(Path.Combine(root, "w", "suites"));
        // appPath is a bare command name, so doctor reports it as resolved-via-PATH (a note,
        // not a warning) and a complete rig can legitimately reach exit 0.
        File.WriteAllText(Path.Combine(root, "w", "workload.json"),
            "{ \"name\": \"w\", \"appPath\": \"cmd.exe\", \"agentType\": \"none\" }");
        return root;
    }

    // 'workload' is required by TestDefinition.Parse, and omitting it is indistinguishable
    // from a corrupt file: the test lands in `missing` for the right reason by the wrong
    // route, which is how the first version of these fixtures reported four failures that
    // said nothing about the code under test.
    private static void WriteTest(string root, string name) =>
        File.WriteAllText(Path.Combine(root, "w", "tests", name + ".json"),
            $"{{ \"name\": \"{name}\", \"workload\": \"w\", \"description\": \"d\", \"actions\": [], \"checkpoints\": [] }}");

    private static void WriteSuite(string root, string name, params string[] tests) =>
        File.WriteAllText(Path.Combine(root, "w", "suites", name + ".json"),
            $"{{ \"name\": \"{name}\", \"description\": \"d\", \"tests\": [{string.Join(",", tests.Select(t => $"\"{t}\""))}] }}");

    [Trait("Category", "Unit")]
    [Fact]
    public async Task CompleteSuite_ReportsNothingMissing()
    {
        var root = NewRig();
        WriteTest(root, "a");
        WriteTest(root, "b");
        WriteSuite(root, "s", "a", "b");

        var (suite, tests, missing) = await TestDiscovery
            .DiscoverTestsForSuiteAsync(root, "w", "s");

        Assert.Equal(2, suite.Tests.Count);
        Assert.Equal(2, tests.Count);
        Assert.Empty(missing);
    }

    // THE defect. Before Phase 3 this returned one test and no indication that two more
    // were declared, and the run reported on it as though the suite had passed.
    [Trait("Category", "Unit")]
    [Fact]
    public async Task ShortSuite_ReportsEveryAbsentTestByName()
    {
        var root = NewRig();
        WriteTest(root, "present");
        WriteSuite(root, "s", "present", "gone-1", "gone-2");

        var (suite, tests, missing) = await TestDiscovery
            .DiscoverTestsForSuiteAsync(root, "w", "s");

        Assert.Equal(3, suite.Tests.Count);
        Assert.Single(tests);
        Assert.Equal(new[] { "gone-1", "gone-2" }, missing);
    }

    // An unparsable test is missing too. Eleven qualia tests have been invalid JSON for
    // months and the suite naming exactly those eleven has never been runnable - it was
    // only ever warned about, so nothing ever failed because of it.
    [Trait("Category", "Unit")]
    [Fact]
    public async Task UnparsableTest_CountsAsMissing_NotAsPresent()
    {
        var root = NewRig();
        WriteTest(root, "good");
        File.WriteAllText(Path.Combine(root, "w", "tests", "broken.json"),
            "{ \"name\": \"broken\", \"actions\": [ { \"text\": \"unescaped \" quote\" } ] }");
        WriteSuite(root, "s", "good", "broken");

        var (_, tests, missing) = await TestDiscovery
            .DiscoverTestsForSuiteAsync(root, "w", "s");

        Assert.Single(tests);
        Assert.Contains("broken", missing);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_ExitsZero_OnACompleteRig()
    {
        var root = NewRig();
        WriteTest(root, "a");
        WriteSuite(root, "s", "a");
        File.WriteAllText(Path.Combine(root, "tokens.json"), "{ \"_note\": \"comments are skipped\" }");

        var exit = await DoctorCommand.RunAsync("w", "s", root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(0, exit);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_ExitsNonZero_WhenASuiteIsShort()
    {
        var root = NewRig();
        WriteTest(root, "a");
        WriteSuite(root, "s", "a", "absent");

        var exit = await DoctorCommand.RunAsync("w", "s", root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(1, exit);
    }

    // A token table that is present, valid, and points somewhere that does not exist is
    // the QC failure mode: every path built from it is wrong, and nothing else notices.
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_ExitsNonZero_WhenATokenResolvesToNothing()
    {
        var root = NewRig();
        WriteTest(root, "a");
        File.WriteAllText(Path.Combine(root, "tokens.json"),
            "{ \"CANARY_TEST_ROOT\": \"C:/definitely/not/here/at/all\" }");
        Canary.Config.CanaryTokens.Invalidate();

        var exit = await DoctorCommand.RunAsync(null, null, root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Canary.Config.CanaryTokens.Invalidate();
        Assert.Equal(1, exit);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_ExitsNonZero_WhenTheWorkloadsRootDoesNotExist()
    {
        var exit = await DoctorCommand.RunAsync(null, null,
            Path.Combine(Path.GetTempPath(), "canary-no-such-root-" + Guid.NewGuid().ToString("N")[..8]),
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(1, exit);
    }

    // Comment keys are documentation, not tokens. doctor found these being loaded as real
    // tokens on its first ever run and reported six errors for prose.
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_IgnoresUnderscorePrefixedCommentKeys()
    {
        var root = NewRig();
        WriteTest(root, "a");
        File.WriteAllText(Path.Combine(root, "tokens.json"),
            "{ \"_comment\": \"this is prose, not a path\", \"_c2\": \"also prose\" }");
        Canary.Config.CanaryTokens.Invalidate();

        var exit = await DoctorCommand.RunAsync(null, null, root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Canary.Config.CanaryTokens.Invalidate();
        Assert.Equal(0, exit);
    }
}
