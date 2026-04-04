using System.CommandLine;
using Canary.Orchestration;

namespace Canary.Cli;

/// <summary>
/// The <c>canary approve</c> command — promotes candidate screenshots to baselines.
/// </summary>
public static class ApproveCommand
{
    /// <summary>
    /// Creates the <c>approve</c> subcommand with its options.
    /// </summary>
    public static Command Create()
    {
        var workloadOption = new Option<string>(
            "--workload",
            "Workload name (e.g., pigment)") { IsRequired = true };

        var testOption = new Option<string>(
            "--test",
            "Name of the test whose candidates to approve as baselines") { IsRequired = true };

        var command = new Command("approve", "Approve candidate screenshots as new baselines")
        {
            workloadOption,
            testOption
        };

        command.SetHandler((workload, test) =>
        {
            var workloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "workloads");

            try
            {
                var count = BaselineManager.ApproveTest(workloadsDir, workload, test);
                Program.Log($"Approved {count} baseline(s) for test '{test}'.");
            }
            catch (Exception ex)
            {
                Program.Log($"Error: {ex.Message}");
            }
        }, workloadOption, testOption);

        return command;
    }
}
