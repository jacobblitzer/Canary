using System.CommandLine;
using System.Diagnostics;
using Canary.Agent.Penumbra;
using Canary.Agent.Qualia;
using Canary.Config;
using Canary.Orchestration;
using Canary.Reporting;
using Canary.Telemetry;

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

        // Deployment campaign Phase 1: lets a machine point at content it did not ship
        // with, without depending on the working directory. CANARY_WORKLOADS_DIR does
        // the same for an installer or a service that cannot pass a flag.
        var workloadsDirOption = new Option<string?>(
            "--workloads-dir",
            $"Path to the workloads content root. Overrides discovery and the {Canary.Config.CanaryPaths.WorkloadsDirEnvVar} environment variable.");

        var headlessOption = new Option<bool>(
            "--headless",
            "Run without launching the Canary UI. Required for CI / scripted use. Default behavior launches Canary.UI.exe with auto-run args per STANDARD.md §16 rule 8. `--quiet` implies `--headless`.");

        var command = new Command("run", "Run visual regression and/or VLM tests against a workload")
        {
            workloadOption,
            testOption,
            suiteOption,
            workloadsDirOption,
            verboseOption,
            quietOption,
            keepOpenOption,
            modeOption,
            headlessOption
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
            var headless = ctx.ParseResult.GetValueForOption(headlessOption) || quiet;
            var workloadsDir = ctx.ParseResult.GetValueForOption(workloadsDirOption);

            Program.Verbose = verbose;
            Program.Quiet = quiet;
            var logger = new ConsoleTestLogger(verbose, quiet);
            var modeOverride = ParseModeOverride(modeStr, logger);

            // A single-test run with the UI visible is an INSPECTION run —
            // the operator wants to look at the result, not watch it close
            // (operator directive 2026-07-31). Suites and headless/CI runs
            // keep the close-after-run default.
            if (!headless && !string.IsNullOrEmpty(test))
            {
                if (!keepOpen && !Program.Quiet)
                    logger.Log("Single-test UI run: keeping the app open for inspection (Ctrl+C / Stop to close).");
                keepOpen = true;
            }

            // STANDARD.md §16 rule 8 — every operator-triggered `canary run`
            // launches with the Canary UI visible unless --headless. If the UI
            // exe is locatable we hand off to it (it auto-runs + we exit 0);
            // if not, fall through to the text-only path.
            if (!headless && TryLaunchUi(workload, test, suite, modeStr, keepOpen, logger))
            {
                ctx.ExitCode = 0;
                return;
            }

            ctx.ExitCode = await RunAsync(workload, test, suite, logger, Program.CancellationToken, keepOpen, modeOverride, workloadsDir).ConfigureAwait(false);
        });

        return command;
    }

    // Attempts to spawn Canary.UI.exe with the auto-run args, returning true
    // on a successful spawn. Returns false (the caller falls through to the
    // text-only path) if the UI exe can't be located or Process.Start throws.
    private static bool TryLaunchUi(string? workload, string? test, string? suite, string? mode, bool keepOpen, ConsoleTestLogger logger)
    {
        if (!UiLocator.TryFindUiExe(out var uiPath))
        {
            logger.Log("Canary.UI.exe not found alongside canary.exe; running headless.");
            return false;
        }

        var args = new AutoRunArgs
        {
            Workload = workload,
            Test = test,
            Suite = suite,
            Mode = mode,
            KeepOpen = keepOpen,
        };

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = uiPath,
                UseShellExecute = true,  // launches the WinExe in its own message loop
                WorkingDirectory = Directory.GetCurrentDirectory(),
            };
            foreach (var a in args.ToArgs()) psi.ArgumentList.Add(a);

            Process.Start(psi);
            logger.Log($"Launched Canary.UI ({uiPath}) with auto-run args.");
            return true;
        }
        catch (Exception ex)
        {
            logger.Log($"Failed to launch Canary.UI ({uiPath}): {ex.Message}. Running headless.");
            return false;
        }
    }

    // Bug 0007: 0 when no failures, 1 when any test failed or crashed. `New` (no
    // baseline yet) is not a failure — the first run of a new test creates the
    // baseline and is expected to count as pass.
    internal static int ExitCodeFromSuiteResult(SuiteResult result)
        => (result.Failed + result.Crashed) == 0 ? 0 : 1;

    /// <summary>
    /// Exit code for "this machine is missing something the content needs".
    /// </summary>
    /// <remarks>
    /// Distinct from 1 (a test failed) on purpose. 0 = everything ran and passed, 1 = the
    /// software under test is wrong, 3 = the machine could not answer the question at all.
    /// A caller that cannot tell 1 from 3 will chase a product bug that is actually a
    /// missing install.
    /// </remarks>
    internal const int ExitPrecondition = 3;

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

        // Phase 2 / §C1: register the per-suite telemetry sink before
        // InitializeAsync so the CDP subscribers it sets up start writing
        // immediately.
        if (agent is Canary.Telemetry.ITelemetryAware ta) ta.RegisterTelemetrySink(runner.TelemetrySink);

        ct.Register(() =>
        {
            try { agent.AbortAsync().Wait(3000); } catch { }
        });

        await agent.InitializeAsync(ct).ConfigureAwait(false);
        logger.Log("Qualia bridge agent ready.  Press Ctrl+C to abort");

        try
        {
            return await runner.RunAgentSuiteAsync(workload, tests, agent, ct).ConfigureAwait(false);
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

        // Phase 2 / §C1: see RunQualiaSuiteAsync — same wiring.
        if (agent is Canary.Telemetry.ITelemetryAware ta) ta.RegisterTelemetrySink(runner.TelemetrySink);

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

    internal static async Task<int> RunAsync(string? workloadName, string? testName, string? suiteName, ConsoleTestLogger logger, CancellationToken ct, bool keepOpen = false, ModeOverride modeOverride = ModeOverride.PixelDiff, string? workloadsDirOverride = null)
    {
        // Report the resolution, not just the path: on a machine with several
        // candidates, "which tree am I actually bound to" is the first question worth
        // answering and it used to be unanswerable.
        var resolution = CanaryPaths.ResolveWorkloadsRootDetailed(workloadsDirOverride);
        var workloadsDir = resolution.Path;
        if (!resolution.Exists)
        {
            logger.Log($"Error: no workloads directory at {CanaryPaths.Describe(resolution)}.  Press Ctrl+C to abort");
            return 1;
        }
        if (Program.Verbose)
            logger.Log($"Workloads root: {CanaryPaths.Describe(resolution)}");

        if (workloadName == null)
        {
            logger.Log("Error: --workload is required.  Press Ctrl+C to abort");
            return 1;
        }

        // Validate mutual exclusion of --test and --suite
        if (testName != null && suiteName != null)
        {
            logger.Log("Error: --test and --suite are mutually exclusive. Use one or the other.");
            return 1;
        }

        // Load workload config
        var configPath = Path.Combine(workloadsDir, workloadName, "workload.json");
        if (!File.Exists(configPath))
        {
            logger.Log($"Error: Workload config not found: {configPath}");
            return 1;
        }

        var workload = await WorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
        var testsDiscovered = 0;
        var pm = new ProcessManager();

        // Register Ctrl+C cleanup
        Console.CancelKeyPress += (_, _) => pm.KillAll();

        try
        {
            // Phase 2b G3, and it runs BEFORE the application launches: an absent ledger is
            // not an empty ledger. If a missing file meant "nothing is armed", deleting it -
            // or shipping a payload that omits it - would disable the guard while every run
            // still printed a pass.
            BaselineLedger ledger;
            try
            {
                ledger = BaselineLedger.LoadRequired(workloadsDir, workloadName);
            }
            catch (Exception lex) when (lex is FileNotFoundException or InvalidDataException)
            {
                logger.Log($"Error: {lex.Message}");
                return 1;
            }

            var runner = new TestRunner(pm, workloadsDir, logger, ledger)
            {
                ModeOverride = modeOverride
            };
            if (modeOverride != ModeOverride.PixelDiff)
                logger.Log($"Mode override: {modeOverride}");

            // Phase 2 / §C1: per-suite telemetry sink. Writes to
            // results/[<suite>/]telemetry.ndjson alongside the existing
            // result.json. Phase 3 will move both into runs/<timestamp>/.
            var telemetryDir = ResultPaths.RollupDir(workloadsDir, workloadName, suiteName);
            Directory.CreateDirectory(telemetryDir);
            var telemetryPath = Path.Combine(telemetryDir, "telemetry.ndjson");
            using var telemetrySink = new NdjsonFileSink(telemetryPath);
            runner.TelemetrySink = telemetrySink;

            List<TestDefinition> tests;
            // Captured outside the try so the precondition handler can say how many tests
            // never ran - "skipped", which is the honest word when the machine was unfit.
            testsDiscovered = 0;
            if (testName != null)
            {
                // Run single test
                var testPath = Path.Combine(workloadsDir, workloadName, "tests", $"{testName}.json");
                if (!File.Exists(testPath))
                {
                    logger.Log($"Error: Test definition not found: {testPath}");
                    return 1;
                }
                tests = new List<TestDefinition> { await TestDefinition.LoadAsync(testPath).ConfigureAwait(false) };
            }
            else if (suiteName != null)
            {
                // Run named suite
                try
                {
                    var (suite, suiteTests, missingTests) = await TestDiscovery.DiscoverTestsForSuiteAsync(
                        workloadsDir, workloadName, suiteName, logger).ConfigureAwait(false);

                    // Phase 3: a SHORT SUITE IS A HARD FAILURE. Running a subset and
                    // reporting success on it is the single most expensive outcome
                    // available - it retires the question without answering it. Better to
                    // refuse and name what is absent.
                    if (missingTests.Count > 0)
                    {
                        logger.Log($"Error: suite '{suiteName}' declares {suite.Tests.Count} tests but {missingTests.Count} could not be loaded:");
                        foreach (var name in missingTests) logger.Log($"         missing or unparsable: {name}");
                        logger.Log("       Refusing to run a partial suite - it would report on tests it never executed.");
                        logger.Log("       Run 'canary doctor' for the full picture.  Press Ctrl+C to abort");
                        return 1;
                    }

                    tests = suiteTests;
                    if (suite.KeepOpen) keepOpen = true;
                    logger.Log($"Suite '{suiteName}': {suite.Description}");
                }
                catch (FileNotFoundException ex)
                {
                    logger.Log($"Error: {ex.Message}");
                    return 1;
                }
            }
            else
            {
                tests = await TestDiscovery.DiscoverTestsAsync(workloadsDir, workloadName, logger).ConfigureAwait(false);
            }

            if (tests.Count == 0)
            {
                logger.Log("No tests found.");
                return 1;
            }

            testsDiscovered = tests.Count;

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
                suiteResult = await runner.RunSuiteAsync(workload, tests, ct).ConfigureAwait(false);
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
                ? ResultPaths.RollupDir(workloadsDir, workloadName, suiteName)
                : ResultPaths.RollupDir(workloadsDir, workloadName, null);
            Directory.CreateDirectory(resultsDir);

            var htmlPath = Path.Combine(resultsDir, "report.html");
            await HtmlReportGenerator.SaveAsync(suiteResult, workloadName, htmlPath).ConfigureAwait(false);

            var junitPath = Path.Combine(resultsDir, "junit.xml");
            await JUnitReportGenerator.SaveAsync(suiteResult, workloadName, junitPath).ConfigureAwait(false);

            if (!Program.Quiet)
                logger.Log($"Reports saved: {htmlPath}");

            return ExitCodeFromSuiteResult(suiteResult);
        }
        catch (PreconditionFailedException pex)
        {
            // Phase 5. A precondition failure is NOT a test failure and gets its own exit
            // code, because the two demand opposite responses: a failing test means look at
            // the software, an unmet precondition means look at the machine. Reporting one
            // as the other sends whoever reads it to the wrong place.
            //
            // Nothing has been opened at this point - the check runs after the first
            // heartbeat and before any setup command - so there is nothing to tear down and
            // no partial results to explain.
            var skipped = testsDiscovered;
            foreach (var line in HostPreconditions.Format(pex, workloadName, skipped))
                logger.Log(line);
            return ExitPrecondition;
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
