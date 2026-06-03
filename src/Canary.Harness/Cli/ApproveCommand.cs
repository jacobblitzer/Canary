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

        var suiteOption = new Option<string?>(
            "--suite",
            "Suite name to scope baselines (e.g., smoke, scenes)");

        var command = new Command("approve", "Approve candidate screenshots as new baselines")
        {
            workloadOption,
            testOption,
            suiteOption
        };

        // BUG-0007 follow-up — exit code propagation. Use InvocationContext so
        // configuration / file-not-found errors are visible to CI consumers.
        command.SetHandler(ctx =>
        {
            var workload = ctx.ParseResult.GetValueForOption(workloadOption)!;
            var test = ctx.ParseResult.GetValueForOption(testOption)!;
            var suite = ctx.ParseResult.GetValueForOption(suiteOption);
            ctx.ExitCode = ApproveInner(workload, test, suite);
        });

        return command;
    }

    internal static int ApproveInner(string workload, string test, string? suite)
    {
        var workloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "workloads");

        try
        {
            var count = BaselineManager.ApproveTest(workloadsDir, workload, test, suite);
            if (count == 0)
            {
                var label = suite != null ? $"test '{test}' (suite '{suite}')" : $"test '{test}'";
                Program.Log($"No candidates found for {label} — nothing to approve.");
                return 1;
            }
            var lbl = suite != null ? $"test '{test}' (suite '{suite}')" : $"test '{test}'";
            Program.Log($"Approved {count} baseline(s) for {lbl}.");
            return 0;
        }
        catch (Exception ex)
        {
            Program.Log($"Error: {ex.Message}");
            return 1;
        }
    }
}
