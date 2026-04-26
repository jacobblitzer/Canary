using System.CommandLine;
using Canary.Agent.Penumbra;
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

        var verboseOption = new Option<bool>(
            "--verbose",
            "Show detailed per-checkpoint output");

        var quietOption = new Option<bool>(
            "--quiet",
            "Suppress output except summary and exit code (for CI)");

        var command = new Command("run", "Run visual regression tests against a workload")
        {
            workloadOption,
            testOption,
            verboseOption,
            quietOption
        };

        command.SetHandler(async (workload, test, verbose, quiet) =>
        {
            Program.Verbose = verbose;
            Program.Quiet = quiet;
            var logger = new ConsoleTestLogger(verbose, quiet);
            await RunAsync(workload, test, logger, Program.CancellationToken).ConfigureAwait(false);
        }, workloadOption, testOption, verboseOption, quietOption);

        return command;
    }

    /// <summary>
    /// Run the Penumbra CDP suite: create the bridge agent once, run all tests through it, then clean up.
    /// </summary>
    private static async Task<SuiteResult> RunPenumbraSuiteAsync(
        TestRunner runner,
        WorkloadConfig workload,
        List<TestDefinition> tests,
        string configPath,
        ConsoleTestLogger logger,
        CancellationToken ct)
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
            return await runner.RunAgentSuiteAsync(workload, tests, agent, ct).ConfigureAwait(false);
        }
        finally
        {
            logger.Log("Shutting down Penumbra bridge agent...");
        }
    }

    private static async Task RunAsync(string? workloadName, string? testName, ConsoleTestLogger logger, CancellationToken ct)
    {
        var workloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "workloads");

        if (workloadName == null)
        {
            logger.Log("Error: --workload is required.  Press Ctrl+C to abort");
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
            var runner = new TestRunner(pm, workloadsDir, logger);

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
            else
            {
                tests = await TestDiscovery.DiscoverTestsAsync(workloadsDir, workloadName, logger).ConfigureAwait(false);
            }

            if (tests.Count == 0)
            {
                logger.Log("No tests found.");
                return;
            }

            logger.Log($"Running {tests.Count} test(s) for workload '{workloadName}'  Press Ctrl+C to abort");

            SuiteResult suite;
            if (workload.AgentType == "penumbra-cdp")
            {
                suite = await RunPenumbraSuiteAsync(runner, workload, tests, configPath, logger, ct).ConfigureAwait(false);
            }
            else if (tests.Count > 1 && tests.All(t => string.Equals(t.RunMode, "shared", StringComparison.OrdinalIgnoreCase)))
            {
                logger.Log($"All {tests.Count} test(s) declare runMode=shared — using single-launch session.");
                suite = await runner.RunSharedSuiteAsync(workload, tests, ct).ConfigureAwait(false);
            }
            else
            {
                suite = await runner.RunSuiteAsync(workload, tests, ct).ConfigureAwait(false);
            }

            // Generate reports
            var resultsDir = Path.Combine(workloadsDir, workloadName, "results");
            Directory.CreateDirectory(resultsDir);

            var htmlPath = Path.Combine(resultsDir, "report.html");
            await HtmlReportGenerator.SaveAsync(suite, workloadName, htmlPath).ConfigureAwait(false);

            var junitPath = Path.Combine(resultsDir, "junit.xml");
            await JUnitReportGenerator.SaveAsync(suite, workloadName, junitPath).ConfigureAwait(false);

            if (!Program.Quiet)
                logger.Log($"Reports saved: {htmlPath}");
        }
        finally
        {
            pm.KillAll();
        }
    }
}
