using System.CommandLine;
using Canary.Commissioning;
using Canary.Config;
using Canary.Orchestration;

namespace Canary.Cli;

/// <summary>
/// <c>canary commission</c> — can this machine test at all?
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Stage C2, ruling 7A. The campaign requires a <b>three-way
/// distinction</b>, because collapsing it wastes days:
/// </para>
/// <list type="bullet">
/// <item>commissioning red → the harness is broken; any plug-in result is <b>unreadable</b></item>
/// <item><c>doctor</c> red → the install is incomplete; <b>not</b> a defect in the plug-in</item>
/// <item>commissioning green + doctor green + smoke red → <b>the only real finding</b></item>
/// </list>
/// <para>
/// So this <b>gates</b> results rather than merely preceding them, and it has its own exit
/// code so a script can tell "the harness is broken" from doctor's "the install is
/// incomplete". Those are different problems with different owners, and a single non-zero
/// would hide which one happened.
/// </para>
/// <para>
/// Layer 1 needs no application and is the entire value of the USER tier: it runs on a
/// machine where nothing else works. Layers 2 and 3 need an app, and say so honestly when
/// there isn't one rather than passing by default.
/// </para>
/// </remarks>
public static class CommissionCommand
{
    /// <summary>Exit code when a fatal layer failed. Distinct from doctor's 1 and the run path's 3.</summary>
    internal const int ExitHarnessUnusable = 4;

    /// <summary>Creates the <c>commission</c> subcommand.</summary>
    /// <returns>The configured command.</returns>
    public static Command Create()
    {
        var workloadOption = new Option<string?>(
            "--workload", "Workload supplying the app for layers 2 and 3. Omit to run layer 1 only.");
        var workloadsDirOption = new Option<string?>(
            "--workloads-dir", "Workloads root. Overrides discovery.");
        var showOption = new Option<bool>(
            "--show", "Print the last commissioning report instead of running anything.");

        var command = new Command("commission",
            "Answer whether this machine can test at all: the comparer agrees with shipped images, capture is repeatable here, and a reference made elsewhere matches.")
        {
            workloadOption, workloadsDirOption, showOption,
        };

        command.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            ctx.ExitCode = await RunAsync(
                ctx.ParseResult.GetValueForOption(workloadOption),
                ctx.ParseResult.GetValueForOption(workloadsDirOption),
                ctx.ParseResult.GetValueForOption(showOption),
                new ConsoleTestLogger(verbose: false, quiet: false),
                ctx.GetCancellationToken()).ConfigureAwait(false);
        });

        return command;
    }

    /// <summary>Runs commissioning.</summary>
    /// <param name="workloadName">Optional workload supplying the app.</param>
    /// <param name="workloadsDirOverride">Optional explicit workloads root.</param>
    /// <param name="showOnly">Print the last report rather than running.</param>
    /// <param name="logger">Output sink.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>0 when the harness is usable; 4 when a fatal layer failed; 1 on a setup error.</returns>
    internal static async Task<int> RunAsync(
        string? workloadName, string? workloadsDirOverride, bool showOnly,
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
        var reportPath = CommissioningReport.PathFor(workloadsDir);

        if (showOnly)
        {
            try
            {
                foreach (var line in CommissioningReport.Load(reportPath).Format()) logger.Log(line);
                return 0;
            }
            catch (FileNotFoundException)
            {
                logger.Log($"ERROR: no commissioning report at {reportPath}. Run `canary commission` first.");
                return 1;
            }
            catch (InvalidDataException ex)
            {
                logger.Log($"ERROR: {ex.Message}");
                return 1;
            }
        }

        var commissioningDir = Path.Combine(workloadsDir, MachineTier.CommissioningWorkload);
        var referencesDir = Path.Combine(commissioningDir, Commissioner.ReferencesFolder);
        var layers = new List<CommissioningLayer>();

        // --- layer 1: no app, and the one that must work everywhere -------------
        layers.Add(Commissioner.CheckComparer(referencesDir));

        // --- layers 2 + 3: need an application -----------------------------------
        var usedWorkload = string.Empty;
        if (string.IsNullOrWhiteSpace(workloadName))
        {
            // NOT a pass. A layer nobody attempted has answered nothing, and calling that
            // green is the silent-green defect this campaign exists to remove.
            layers.Add(new CommissioningLayer(2, "repeatable", LayerOutcome.NotRun,
                "no --workload given, so no app was launched - capture repeatability is UNKNOWN on this machine", true));
            layers.Add(new CommissioningLayer(3, "reference", LayerOutcome.NotRun,
                "no --workload given", false));
        }
        else
        {
            usedWorkload = workloadName!;
            var configPath = Path.Combine(workloadsDir, usedWorkload, "workload.json");
            if (!File.Exists(configPath))
            {
                logger.Log($"ERROR: no workload.json for '{usedWorkload}' at {configPath}");
                return 1;
            }

            var outDir = ResultPaths.RollupDir(workloadsDir, MachineTier.CommissioningWorkload, null);
            var first = Path.Combine(outDir, "repeat-1.png");
            var second = Path.Combine(outDir, "repeat-2.png");

            try
            {
                var workload = await WorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
                var pm = new ProcessManager();
                try
                {
                    // An empty ledger, as `canary env` does: commissioning compares nothing
                    // against a baseline, and it has to work on a machine that has no ledger.
                    var runner = new TestRunner(pm, workloadsDir, logger, new BaselineLedger { Workload = usedWorkload });
                    var captured = await runner.CaptureCommissioningFramesAsync(
                        workload, first, second, 800, 600, ct).ConfigureAwait(false);

                    layers.Add(captured
                        ? Commissioner.CheckRepeatable(first, second)
                        : new CommissioningLayer(2, "repeatable", LayerOutcome.NotRun,
                            $"{workload.DisplayName} produced no captures - layer not attempted", true));

                    // The shipped reference is per workload, because a frame from Rhino means
                    // nothing to a browser workload.
                    var shipped = Path.Combine(referencesDir, $"{usedWorkload}-reference.png");
                    layers.Add(Commissioner.CheckShippedReference(shipped, first));
                }
                finally { pm.KillAll(); }
            }
            catch (OperationCanceledException)
            {
                logger.Log("Aborted.");
                return 1;
            }
            catch (Exception ex)
            {
                layers.Add(new CommissioningLayer(2, "repeatable", LayerOutcome.Failed,
                    $"could not capture from {usedWorkload}: {ex.GetType().Name}: {ex.Message}", true));
                layers.Add(new CommissioningLayer(3, "reference", LayerOutcome.NotRun,
                    "no capture to compare", false));
            }
        }

        var report = new CommissioningReport
        {
            CapturedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Machine = MachineIdentity.Describe(workloadsDir),
            Workload = usedWorkload,
            Layers = layers,
        };
        report.Save(reportPath);

        logger.Log(string.Empty);
        foreach (var line in report.Format()) logger.Log(line);
        logger.Log(string.Empty);
        logger.Log($"written to     : {reportPath}");

        return report.HarnessUsable ? 0 : ExitHarnessUnusable;
    }
}
