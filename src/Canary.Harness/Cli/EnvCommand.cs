using System.CommandLine;
using Canary.Config;
using Canary.Orchestration;

namespace Canary.Cli;

/// <summary>
/// <c>canary env</c> — what the target application actually has loaded on THIS machine.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 5b. The environment capture already happened on every run, but a
/// run was the <b>only</b> way to produce one — and the machine that most needs interrogating is
/// a fresh QC install, which is exactly where a suite cannot run yet. A capture that requires a
/// working install in order to report whether the install works is no use on the one machine it
/// matters for.
/// </para>
/// <para>
/// The verb is deliberately read-only in effect: it launches the application, asks one question,
/// writes the capture, closes the application, and prints. <b>It never repairs anything.</b> The
/// one time this campaign touched an install decision it was the operator who fixed an
/// unregistered plug-in in Developer Settings; a tool that had "helpfully" registered it
/// somewhere else would have hidden the question rather than answered it.
/// </para>
/// <para>
/// It also exits 0 on a machine full of clashes. Reporting is this verb's whole job, and
/// <c>canary doctor</c> is the gate — one gate, in one place, rather than two commands with
/// overlapping opinions about what counts as broken.
/// </para>
/// </remarks>
public static class EnvCommand
{
    /// <summary>Creates the <c>env</c> subcommand.</summary>
    /// <returns>The configured command.</returns>
    public static Command Create()
    {
        var workloadOption = new Option<string>(
            "--workload", "Workload whose application to probe.") { IsRequired = true };
        var workloadsDirOption = new Option<string?>(
            "--workloads-dir", "Workloads root. Overrides discovery.");
        var showOption = new Option<bool>(
            "--show", "Print the LAST capture from disk instead of launching anything.");

        var command = new Command("env",
            "Report what the target application actually has loaded on this machine, and write it as JSON for diffing against another machine.")
        {
            workloadOption, workloadsDirOption, showOption,
        };

        command.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            ctx.ExitCode = await RunAsync(
                ctx.ParseResult.GetValueForOption(workloadOption)!,
                ctx.ParseResult.GetValueForOption(workloadsDirOption),
                ctx.ParseResult.GetValueForOption(showOption),
                new ConsoleTestLogger(verbose: false, quiet: false),
                ctx.GetCancellationToken()).ConfigureAwait(false);
        });

        return command;
    }

    /// <summary>Captures or shows the environment.</summary>
    /// <param name="workloadName">Workload to probe.</param>
    /// <param name="workloadsDirOverride">Optional explicit workloads root.</param>
    /// <param name="showOnly">Print the last capture rather than taking a new one.</param>
    /// <param name="logger">Output sink.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>0 on success; 1 when the environment could not be determined.</returns>
    internal static async Task<int> RunAsync(
        string workloadName, string? workloadsDirOverride, bool showOnly,
        ConsoleTestLogger logger, CancellationToken ct)
    {
        var res = CanaryPaths.ResolveWorkloadsRootDetailed(workloadsDirOverride);
        logger.Log($"workloads root : {CanaryPaths.Describe(res)}");
        if (string.IsNullOrEmpty(res.Path) || !Directory.Exists(res.Path))
        {
            logger.Log($"ERROR: no workloads directory at {CanaryPaths.Describe(res)}");
            return 1;
        }

        var workloadsDir = res.Path;
        var capturePath = EnvironmentCapture.PathFor(workloadsDir, workloadName);

        if (showOnly)
        {
            EnvironmentCapture existing;
            try
            {
                existing = EnvironmentCapture.Load(capturePath);
            }
            catch (FileNotFoundException)
            {
                // Deliberately an error, not "nothing loaded": those are different answers, and
                // rendering the first as the second is how a machine nobody probed reads as
                // clean. Run the verb without --show to produce one.
                logger.Log($"ERROR: no capture at {capturePath}. Run `canary env --workload {workloadName}` to take one.");
                return 1;
            }
            catch (InvalidDataException ex)
            {
                logger.Log($"ERROR: {ex.Message}");
                return 1;
            }

            Print(existing, capturePath, logger, live: false);
            return 0;
        }

        var configPath = Path.Combine(workloadsDir, workloadName, "workload.json");
        if (!File.Exists(configPath))
        {
            logger.Log($"ERROR: no workload.json for '{workloadName}' at {configPath}");
            return 1;
        }

        WorkloadConfig workload;
        List<TestDefinition> tests;
        try
        {
            workload = await WorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
            // For the declared origin expectations only. A test that will not parse must not
            // stop the probe: the whole point is to work on a machine whose content is suspect.
            tests = await TestDiscovery.DiscoverTestsAsync(workloadsDir, workloadName, logger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Log($"ERROR: could not read '{workloadName}': {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        var pm = new ProcessManager();
        try
        {
            // An EMPTY ledger, deliberately, and this is the one place it is correct: a probe
            // compares nothing against a baseline, so the ledger is never consulted. Elsewhere
            // in this campaign an absent ledger is a hard error precisely because it must not be
            // silently treated as empty - but `env` has to work on a machine that has no ledger
            // at all, which is the whole reason the verb exists.
            var runner = new TestRunner(pm, workloadsDir, logger, new BaselineLedger { Workload = workloadName });
            var capture = await runner.CaptureEnvironmentAsync(workload, tests, ct).ConfigureAwait(false);

            Print(capture, capturePath, logger, live: true);
            return 0;
        }
        catch (OperationCanceledException)
        {
            logger.Log("Aborted.");
            return 1;
        }
        catch (Exception ex)
        {
            logger.Log($"ERROR: could not probe {workload.DisplayName}: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            pm.KillAll();
        }
    }

    private static void Print(
        EnvironmentCapture capture, string path, ConsoleTestLogger logger, bool live)
    {
        logger.Log($"machine        : {MachineIdentity.Format(capture.Machine)}");

        // Only for a capture read off disk - one taken seconds ago needs no caveat.
        if (!live)
        {
            var caveat = capture.Caveat();
            if (!string.IsNullOrEmpty(caveat)) logger.Log($"NOTE           : {caveat}");
        }

        logger.Log($"captured       : {capture.CapturedUtc}");

        foreach (var line in EnvironmentReport.Format(capture.Host, capture.Findings))
            logger.Log(line);

        logger.Log(string.Empty);
        logger.Log($"written to     : {path}");
        logger.Log("Diff this against the same file from another machine to verify an install.");
    }

}
