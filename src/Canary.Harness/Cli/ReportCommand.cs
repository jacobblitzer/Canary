using System.CommandLine;
using System.Diagnostics;

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

        command.SetHandler((workload) =>
        {
            var workloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "workloads");
            string? reportPath = null;

            if (workload != null)
            {
                reportPath = Path.Combine(workloadsDir, workload, "results", "report.html");
            }
            else
            {
                // Find the most recent report.html across all workloads
                if (Directory.Exists(workloadsDir))
                {
                    reportPath = Directory.GetFiles(workloadsDir, "report.html", SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                }
            }

            if (reportPath == null || !File.Exists(reportPath))
            {
                Program.Log("No report found. Run tests first with 'canary run'.");
                return;
            }

            Program.Log($"Opening report: {reportPath}");
            Process.Start(new ProcessStartInfo
            {
                FileName = reportPath,
                UseShellExecute = true
            });
        }, workloadOption);

        return command;
    }
}
