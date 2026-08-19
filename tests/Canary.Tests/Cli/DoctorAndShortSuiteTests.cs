using Canary.Agent;
using Canary.Cli;
using Canary.Commissioning;
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
        // A complete rig now includes a baseline ledger, because doctor's check 6 fails
        // closed: an ABSENT ledger is an error, while an explicitly empty row set is
        // legal. Nothing here arms a checkpoint, so zero rows is the honest declaration.
        File.WriteAllText(Path.Combine(root, "w", Canary.Orchestration.BaselineLedger.FileName),
            "{ \"version\": 1, \"workload\": \"w\", \"rows\": [] }");
        return root;
    }

    /// <summary>Gives a rig the two artefacts that prove something has looked at it.</summary>
    /// <param name="root">Workloads root from <c>NewRig</c>.</param>
    /// <remarks>
    /// <para>
    /// Doctor now separates "a check failed" (exit 1) from "a check never ran"
    /// (<see cref="DoctorCommand.ExitNotProven"/>), so a rig with no commissioning report and
    /// no environment capture no longer reaches 0 - correctly, because nothing has asked that
    /// machine anything.
    /// </para>
    /// <para>
    /// Tests whose subject is a DIFFERENT check therefore have to establish that baseline
    /// first, or they assert on an exit code driven by something they are not testing.
    /// </para>
    /// </remarks>
    private static void MarkAsMeasured(string root)
    {
        WriteCommissioning(root);
        EnvironmentCapture.Create(
                "w",
                new Dictionary<string, string> { [HostStateFields.Loaded] = string.Empty },
                Array.Empty<EnvironmentClash>(),
                workloadsDir: root)
            .Save(EnvironmentCapture.PathFor(root, "w"));
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
        MarkAsMeasured(root);
        File.WriteAllText(Path.Combine(root, "tokens.json"), "{ \"_note\": \"comments are skipped\" }");

        var exit = await DoctorCommand.RunAsync("w", "s", root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(0, exit);
    }

    // --- check 9: the harness itself -------------------------------------------
    //
    // Doctor answers "is the install complete". Commissioning answers "does the harness
    // work". Collapsing those wastes days, because they have different owners and different
    // fixes - so a harness fault must never be reported as an install error, and must still
    // stop the machine being treated as ready.

    /// <summary>An uncommissioned machine reports NOT PROVEN. It is not an install failure, and it is not a pass.</summary>
    /// <remarks>
    /// <para>
    /// Doctor is still the FIRST thing run on a fresh QC box and still must not require that
    /// something already ran - which is why this is neither an error nor an exception. It is
    /// its own exit code.
    /// </para>
    /// <para>
    /// It used to be a warning, and that made it invisible where it mattered:
    /// <c>qc-capture.ps1</c> judges doctor by its exit code and never by scraping its text,
    /// so a bundle coming back from the QC machine could report a green install for a machine
    /// nothing had ever commissioned. The campaign's rule is that an unrun check and a
    /// passing check are different answers; a warning does not move the exit code, so a
    /// warning could not carry that rule.
    /// </para>
    /// </remarks>
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_WithNoCommissioningReport_ReportsNotProven_NotAPassAndNotAnError()
    {
        var root = NewRig();
        WriteTest(root, "a");
        WriteSuite(root, "s", "a");

        var exit = await DoctorCommand.RunAsync("w", "s", root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(DoctorCommand.ExitNotProven, exit);
        Assert.NotEqual(0, exit);
        Assert.NotEqual(1, exit);   // an install failure is a different finding with a different owner
    }

    /// <summary>A fatal layer that could not START is unproven, not a harness fault.</summary>
    /// <remarks>
    /// On a payload machine both fatal layers come back NotRun for install and packaging
    /// reasons - the commissioning content did not travel, and the agent is not registered.
    /// Routing those into HarnessFaults made doctor print "This is NOT an install problem"
    /// over precisely the two failures that ARE install problems, and its advice - run
    /// commission - was a dead end.
    /// </remarks>
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_WhenAFatalLayerNeverRan_ReportsNotProven_RatherThanAHarnessFault()
    {
        var root = NewRig();
        WriteTest(root, "a");
        WriteSuite(root, "s", "a");
        EnvironmentCapture.Create(
                "w",
                new Dictionary<string, string> { [HostStateFields.Loaded] = string.Empty },
                Array.Empty<EnvironmentClash>(),
                workloadsDir: root)
            .Save(EnvironmentCapture.PathFor(root, "w"));
        WriteCommissioning(root, repeatable: LayerOutcome.NotRun);

        var exit = await DoctorCommand.RunAsync("w", "s", root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(DoctorCommand.ExitNotProven, exit);
    }

    /// <summary>A fatal layer that RAN and disagreed is still a harness fault.</summary>
    /// <remarks>The other half of the discriminator: this one must not soften.</remarks>
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_WhenAFatalLayerRanAndFailed_IsStillAHarnessFault()
    {
        var root = NewRig();
        WriteTest(root, "a");
        WriteSuite(root, "s", "a");
        MarkAsMeasured(root);
        WriteCommissioning(root, repeatable: LayerOutcome.Failed);

        var exit = await DoctorCommand.RunAsync("w", "s", root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(1, exit);
    }

    /// <summary>A failing fatal layer stops the machine being called ready.</summary>
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_WhenTheHarnessIsUnproven_ExitsNonZero()
    {
        var root = NewRig();
        WriteTest(root, "a");
        WriteSuite(root, "s", "a");
        WriteCommissioning(root, repeatable: LayerOutcome.Failed);

        var exit = await DoctorCommand.RunAsync("w", "s", root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(1, exit);
    }

    /// <summary>
    /// A layer that was never ATTEMPTED is not a pass either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The campaign exists because a missing baseline yielded New and New was excluded from
    /// the exit code. A machine whose repeatability is unknown has not shown it can test.
    /// </para>
    /// <para>
    /// It used to exit 1, sharing a code with "the install is incomplete". It now exits
    /// <see cref="DoctorCommand.ExitNotProven"/>: still non-zero, still stops the machine
    /// being trusted, but no longer claims a failure nobody has observed. What made that
    /// worth splitting is that doctor's harness-fault verdict prints "This is NOT an install
    /// problem" - and on a payload machine, a fatal layer at NotRun is usually EXACTLY an
    /// install problem: the content did not travel, or the agent is not registered.
    /// </para>
    /// </remarks>
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_WhenAFatalLayerWasNeverRun_ExitsNonZero()
    {
        var root = NewRig();
        WriteTest(root, "a");
        WriteSuite(root, "s", "a");
        MarkAsMeasured(root);
        WriteCommissioning(root, repeatable: LayerOutcome.NotRun);

        var exit = await DoctorCommand.RunAsync("w", "s", root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.NotEqual(0, exit);
        Assert.Equal(DoctorCommand.ExitNotProven, exit);
    }

    /// <summary>A non-fatal layer failing does not stop the machine being usable.</summary>
    /// <remarks>
    /// Layer 3 asks whether baselines TRAVEL here. A machine that fails it tests perfectly
    /// well by approving locally or using VLM, and grounding it would report on a question
    /// the run never asked.
    /// </remarks>
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_WhenOnlyTheNonFatalLayerFails_StillPasses()
    {
        var root = NewRig();
        WriteTest(root, "a");
        WriteSuite(root, "s", "a");
        MarkAsMeasured(root);
        WriteCommissioning(root, reference: LayerOutcome.Failed);

        var exit = await DoctorCommand.RunAsync("w", "s", root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(0, exit);
    }

    /// <summary>A report from another machine is an integrity error, not a harness fault.</summary>
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_WhenTheCommissioningReportIsFromAnotherMachine_ExitsNonZero()
    {
        var root = NewRig();
        WriteTest(root, "a");
        WriteSuite(root, "s", "a");
        WriteCommissioning(root, machineName: Environment.MachineName + "-ELSEWHERE");

        var exit = await DoctorCommand.RunAsync("w", "s", root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(1, exit);
    }

    private static void WriteCommissioning(
        string root,
        LayerOutcome comparer = LayerOutcome.Passed,
        LayerOutcome repeatable = LayerOutcome.Passed,
        LayerOutcome reference = LayerOutcome.Passed,
        string? machineName = null)
    {
        new CommissioningReport
        {
            CapturedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Workload = "w",
            Machine = new Dictionary<string, string>
            {
                [MachineIdentity.MachineName] = machineName ?? Environment.MachineName,
            },
            Layers = new[]
            {
                new CommissioningLayer(1, "comparer", comparer, "", true),
                new CommissioningLayer(2, "repeatable", repeatable, "", true),
                new CommissioningLayer(3, "reference", reference, "", false),
            },
        }.Save(CommissioningReport.PathFor(root));
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

    // Phase 2b. The ledger is the only check that can see a baseline at all - doctor's
    // other checks skip anything under /results/ by design - so an absent ledger must be
    // an error rather than a quiet "nothing is armed". Otherwise deleting the file, or
    // shipping a payload without it, disables the guard while every run still prints a
    // pass. Mutation-proven: making LoadRequired return an empty ledger for a missing
    // file turns this red.
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_ExitsNonZero_WhenTheBaselineLedgerIsAbsent()
    {
        var root = NewRig();
        WriteTest(root, "a");
        File.Delete(Path.Combine(root, "w", Canary.Orchestration.BaselineLedger.FileName));

        var exit = await DoctorCommand.RunAsync("w", null, root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(1, exit);
    }

    // The counter-mutation at the doctor level: a workload that legitimately arms nothing
    // must pass. A guard that cannot be satisfied gets switched off.
    [Trait("Category", "Unit")]
    [Fact]
    public async Task Doctor_ExitsZero_WhenTheLedgerIsExplicitlyEmpty()
    {
        var root = NewRig();
        WriteTest(root, "a");
        MarkAsMeasured(root);

        var exit = await DoctorCommand.RunAsync("w", null, root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Assert.Equal(0, exit);
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
        MarkAsMeasured(root);
        File.WriteAllText(Path.Combine(root, "tokens.json"),
            "{ \"_comment\": \"this is prose, not a path\", \"_c2\": \"also prose\" }");
        Canary.Config.CanaryTokens.Invalidate();

        var exit = await DoctorCommand.RunAsync(null, null, root,
            new ConsoleTestLogger(verbose: false, quiet: true));

        Canary.Config.CanaryTokens.Invalidate();
        Assert.Equal(0, exit);
    }
}
