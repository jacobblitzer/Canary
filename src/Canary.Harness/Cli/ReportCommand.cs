using System.CommandLine;
using System.Diagnostics;
using Canary.Config;

namespace Canary.Cli;

/// <summary>
/// The <c>canary report</c> command — opens the most recent HTML report.
/// </summary>
public static class ReportCommand
{
    /// <summary>
    /// Creates the <c>report</c> subcommand.
    /// </summary>
    public static Command Create()
    {
        var workloadOption = new Option<string?>(
            "--workload",
            "Workload name (defaults to searching all workloads)");

        var command = new Command("report", "Open the most recent HTML test report")
        {
            workloadOption
        };

        // BUG-0007 follow-up — exit code propagation.
        command.SetHandler(ctx =>
        {
            var workload = ctx.ParseResult.GetValueForOption(workloadOption);
            ctx.ExitCode = ReportInner(workload);
        });

        return command;
    }

    internal static int ReportInner(string? workload)
    {
        var workloadsDir = CanaryPaths.ResolveWorkloadsRoot();
        string? reportPath = null;

        if (workload != null)
        {
            // "Most recent report for this workload", not "the one at a fixed path". A
            // suite run writes its rollup to results/<suite>/report.html, so the bare
            // results/report.html this used to assume could not reach 56 of the 60
            // report.html files on disk — `canary report --workload rhino` opened a stale
            // whole-workload report, or nothing, while the suite report it should have
            // shown sat one directory down.
            var resultsRoot = Canary.Orchestration.ResultPaths.ResultsRoot(workloadsDir, workload);
            reportPath = Directory.Exists(resultsRoot)
                ? Directory.GetFiles(resultsRoot, "report.html", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
        }
        else if (Directory.Exists(workloadsDir))
        {
            reportPath = Directory.GetFiles(workloadsDir, "report.html", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        if (reportPath == null || !File.Exists(reportPath))
        {
            Program.Log("No report found. Run tests first with 'canary run'.");
            return 1;
        }

        Program.Log($"Opening report: {reportPath}");
        Process.Start(new ProcessStartInfo
        {
            FileName = reportPath,
            UseShellExecute = true
        });
        return 0;
    }
}
