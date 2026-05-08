using System.CommandLine;
using Canary.Agent.Penumbra;
using Canary.Agent.Qualia;
using Canary.Config;
using Canary.Orchestration;
using Canary.Reporting;

namespace Canary.Cli;

/// <summary>
/// The <c>canary run</c> command — executes visual regression tests.
/// </summary>
public static class RunCommand
{
    /// <summary>
    /// Creates the <c>run</c> subcommand with its options.
    /// </summary>
    public static Command Create()
    {
        var workloadOption = new Option<string?>(
            "--workload",
            "Run all tests for the specified workload (e.g., pigment)");

        var testOption = new Option<string?>(
            "--test",
            "Run a single test by name");

        var suiteOption = new Option<string?>(
            "--suite",
            "Run a named test suite (e.g., smoke, scenes)");

        var verboseOption = new Option<bool>(
            "--verbose",
            "Show detailed per-checkpoint output");

        var quietOption = new Option<bool>(
            "--quiet",
            "Suppress output except summary and exit code (for CI)");

        var keepOpenOption = new Option<bool>(
            "--keep-open",
            "Keep the target application open after tests complete for manual inspection. Press Ctrl+C to close.");

        var modeOption = new Option<string>(
            "--mode",
            description: "Comparison mode override: 'pixel-diff' (default — visual regression), 'vlm' (semantic correctness), or 'both' (run each checkpoint twice). Per-checkpoint mode='vlm' in test JSON still wins.",
            getDefaultValue: () => "pixel-diff");

        var command = new Command("run", "Run visual regression and/or VLM tests against a workload")
        {
            workloadOption,
            testOption,
            suiteOption,
            verboseOption,
            quietOption,
            keepOpenOption,
            modeOption
        };

        command.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var workload = ctx.ParseResult.GetValueForOption(workloadOption);
            var test = ctx.ParseResult.GetValueForOption(testOption);
            var suite = ctx.ParseResult.GetValueForOption(suiteOption);
            var verbose = ctx.ParseResult.GetValueForOption(verboseOption);
            var quiet = ctx.ParseResult.GetValueForOption(quietOption);
            var keepOpen = ctx.ParseResult.GetValueForOption(keepOpenOption);
            var modeStr = ctx.ParseResult.GetValueForOption(modeOption) ?? "pixel-diff";

            Program.Verbose = verbose;
            Program.Quiet = quiet;
            var logger = new ConsoleTestLogger(verbose, quiet);
            var modeOverride = ParseModeOverride(modeStr, logger);
            await RunAsync(workload, test, suite, logger, Program.CancellationToken, keepOpen, modeOverride).ConfigureAwait(false);
        });

        return command;
    }

    /// <summary>
    /// Parse the <c>--mode</c> flag string into a <see cref="ModeOverride"/>.
    /// Logs and falls back to <see cref="ModeOverride.PixelDiff"/> on unknown values.
    /// </summary>
    private static ModeOverride ParseModeOverride(string raw, ConsoleTestLogger logger) => raw.ToLowerInvariant() switch
    {
        "pixel-diff" or "pixeldiff" or "regression" => ModeOverride.PixelDiff,
        "vlm" or "semantic" or "correctness"        => ModeOverride.Vlm,
        "both" or "all"                              => ModeOverride.Both,
        _ => LogAndDefault(raw, logger),
    };

    private static ModeOverride LogAndDefault(string raw, ConsoleTestLogger logger)
    {
        logger.Log($"Warning: unknown --mode '{raw}'. Falling back to 'pixel-diff'.");
        return ModeOverride.PixelDiff;
    }

    /// <summary>
    /// Run the Penumbra CDP suite: create the bridge agent once, run all tests through it, then clean up.
    /// </summary>
    private static async Task<SuiteResult> RunQualiaSuiteAsync(
        TestRunner runner,
        WorkloadConfig workload,
        List<TestDefinition> tests,
        string configPath,
        ConsoleTestLogger logger,
        CancellationToken ct,
        string? suiteName = null)
    {
        logger.Log("Initializing Qualia CDP bridge agent...");

        var qualiaConfig = await QualiaWorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
        using var agent = new QualiaBridgeAgent(qualiaConfig.QualiaConfig);

        ct.Register(() =>
        {
            try { agent.AbortAsync().Wait(3000); } catch { }
        });

        await agent.InitializeAsync(ct).ConfigureAwait(false);
        logger.Log("Qualia bridge agent ready.  Press Ctrl+C to abort");

        try
        {
            return await runner.RunAgentSuiteAsync(workload, tests, agent, ct, suiteName).ConfigureAwait(false);
        }
        finally
        {
            logger.Log("Shutting down Qualia bridge agent...");
        }
    }

    private static async Task<SuiteResult> RunPenumbraSuiteAsync(
        TestRunner runner,
        WorkloadConfig workload,
        List<TestDefinition> tests,
        string configPath,
        ConsoleTestLogger logger,
        CancellationToken ct,
        string? suiteName = null)
    {
        logger.Log("Initializing Penumbra CDP bridge agent...");

        var penumbraConfig = await PenumbraWorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
        using var agent = new PenumbraBridgeAgent(penumbraConfig.PenumbraConfig);

        // Register Ctrl+C abort
        ct.Register(() =>
        {
            try { agent.AbortAsync().Wait(3000); } catch { }
        });

        await agent.InitializeAsync(ct).ConfigureAwait(false);
        logger.Log("Penumbra bridge agent ready.  Press Ctrl+C to abort");

        try
        {
            return await runner.RunAgentSuiteAsync(workload, tests, agent, ct, suiteName).ConfigureAwait(false);
        }
        finally
        {
            logger.Log("Shutting down Penumbra bridge agent...");
        }
    }

    private static async Task RunAsync(string? workloadName, string? testName, string? suiteName, ConsoleTestLogger logger, CancellationToken ct, bool keepOpen = false, ModeOverride modeOverride = ModeOverride.PixelDiff)
    {
        var workloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "workloads");

        if (workloadName == null)
        {
            logger.Log("Error: --workload is required.  Press Ctrl+C to abort");
            return;
        }

        // Validate mutual exclusion of --test and --suite
        if (testName != null && suiteName != null)
        {
            logger.Log("Error: --test and --suite are mutually exclusive. Use one or the other.");
            return;
        }

        // Load workload config
        var configPath = Path.Combine(workloadsDir, workloadName, "workload.json");
        if (!File.Exists(configPath))
        {
            logger.Log($"Error: Workload config not found: {configPath}");
            return;
        }

        var workload = await WorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
        var pm = new ProcessManager();

        // Register Ctrl+C cleanup
        Console.CancelKeyPress += (_, _) => pm.KillAll();

        try
        {
            var runner = new TestRunner(pm, workloadsDir, logger)
            {
                ModeOverride = modeOverride
            };
            if (modeOverride != ModeOverride.PixelDiff)
                logger.Log($"Mode override: {modeOverride}");

            List<TestDefinition> tests;
            if (testName != null)
            {
                // Run single test
                var testPath = Path.Combine(workloadsDir, workloadName, "tests", $"{testName}.json");
                if (!File.Exists(testPath))
                {
                    logger.Log($"Error: Test definition not found: {testPath}");
                    return;
                }
                tests = new List<TestDefinition> { await TestDefinition.LoadAsync(testPath).ConfigureAwait(false) };
            }
            else if (suiteName != null)
            {
                // Run named suite
                try
                {
                    var (suite, suiteTests) = await TestDiscovery.DiscoverTestsForSuiteAsync(
                        workloadsDir, workloadName, suiteName, logger).ConfigureAwait(false);
                    tests = suiteTests;
                    if (suite.KeepOpen) keepOpen = true;
                    logger.Log($"Suite '{suiteName}': {suite.Description}");
                }
                catch (FileNotFoundException ex)
                {
                    logger.Log($"Error: {ex.Message}");
                    return;
                }
            }
            else
            {
                tests = await TestDiscovery.DiscoverTestsAsync(workloadsDir, workloadName, logger).ConfigureAwait(false);
            }

            if (tests.Count == 0)
            {
                logger.Log("No tests found.");
                return;
            }

            var runLabel = suiteName != null
                ? $"Running {tests.Count} test(s) for suite '{suiteName}' in workload '{workloadName}'"
                : $"Running {tests.Count} test(s) for workload '{workloadName}'";
            logger.Log($"{runLabel}  Press Ctrl+C to abort");

            SuiteResult suiteResult;
            if (workload.AgentType == "penumbra-cdp")
            {
                suiteResult = await RunPenumbraSuiteAsync(runner, workload, tests, configPath, logger, ct, suiteName).ConfigureAwait(false);
            }
            else if (workload.AgentType == "qualia-cdp")
            {
                suiteResult = await RunQualiaSuiteAsync(runner, workload, tests, configPath, logger, ct, suiteName).ConfigureAwait(false);
            }
            else if (tests.Count > 1 && tests.All(t => string.Equals(t.RunMode, "shared", StringComparison.OrdinalIgnoreCase)))
            {
                logger.Log($"All {tests.Count} test(s) declare runMode=shared — using single-launch session.");
                suiteResult = await runner.RunSharedSuiteAsync(workload, tests, ct).ConfigureAwait(false);
            }
            else
            {
                suiteResult = await runner.RunSuiteAsync(workload, tests, ct, suiteName).ConfigureAwait(false);
            }

            // Auto-enable keepOpen if any failed/crashed test requested it
            if (!keepOpen)
            {
                keepOpen = tests.Any(t => t.KeepOpenOnFailure
                    && suiteResult.TestResults.Any(r => r.TestName == t.Name
                        && r.Status is TestStatus.Failed or TestStatus.Crashed));
            }

            // Generate reports — scoped under suite name when running a suite
            var resultsDir = suiteName != null
                ? Path.Combine(workloadsDir, workloadName, "results", suiteName)
                : Path.Combine(workloadsDir, workloadName, "results");
            Directory.CreateDirectory(resultsDir);

            var htmlPath = Path.Combine(resultsDir, "report.html");
            await HtmlReportGenerator.SaveAsync(suiteResult, workloadName, htmlPath).ConfigureAwait(false);

            var junitPath = Path.Combine(resultsDir, "junit.xml");
            await JUnitReportGenerator.SaveAsync(suiteResult, workloadName, junitPath).ConfigureAwait(false);

            if (!Program.Quiet)
                logger.Log($"Reports saved: {htmlPath}");
        }
        finally
        {
            if (keepOpen)
            {
                logger.Log("Application kept open for inspection. Press Ctrl+C to close.");
                try { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            pm.KillAll();
        }
    }
}
