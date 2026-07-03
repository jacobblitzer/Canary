using System.CommandLine;
using System.Text.Json;
using Canary.Orchestration;

namespace Canary.Cli;

/// <summary>
/// The <c>canary approve</c> command — promotes candidate screenshots to baselines.
///
/// R1.3 (2026-07-03): grew per-SUITE bulk approval + prints exactly what it blessed.
/// Forms:
///   canary approve --workload rhino --test cpig-repmatrix-sphere-auto     (one test)
///   canary approve --workload rhino --suite cpig-display-matrix           (every test in the suite JSON)
///   canary approve --workload rhino --suite s --test t                    (one test, suite-scoped path)
///
/// Path semantics: single-test and suite-scoped forms use BaselineManager's layout
/// (results/[&lt;suite&gt;/]&lt;test&gt;/). Bulk-suite mode tries the suite-scoped path first and falls
/// back to the SHARED layout (results/&lt;test&gt;/ with no suite dir) — shared-runMode suites
/// (all rhino suites) write per-test artifacts there (TestRunner.RunSharedSuite passes no
/// suiteName to GetTestDirectory).
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
            "Workload name (e.g., rhino)") { IsRequired = true };

        var testOption = new Option<string?>(
            "--test",
            "Name of the test whose candidates to approve as baselines");

        var suiteOption = new Option<string?>(
            "--suite",
            "Suite name. With --test: scopes the baseline path. Alone: bulk-approves EVERY test listed in workloads/<w>/suites/<suite>.json");

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
            var test = ctx.ParseResult.GetValueForOption(testOption);
            var suite = ctx.ParseResult.GetValueForOption(suiteOption);
            ctx.ExitCode = ApproveInner(workload, test, suite);
        });

        return command;
    }

    internal static int ApproveInner(string workload, string? test, string? suite)
    {
        var workloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "workloads");

        if (test == null && suite == null)
        {
            Program.Log("Error: provide --test, --suite, or both.");
            return 1;
        }

        try
        {
            if (test != null)
                return ApproveSingle(workloadsDir, workload, test, suite);
            return ApproveWholeSuite(workloadsDir, workload, suite!);
        }
        catch (Exception ex)
        {
            Program.Log($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int ApproveSingle(string workloadsDir, string workload, string test, string? suite)
    {
        var files = BaselineManager.ApproveTestFiles(workloadsDir, workload, test, suite);
        var label = suite != null ? $"test '{test}' (suite '{suite}')" : $"test '{test}'";
        if (files.Length == 0)
        {
            Program.Log($"No candidates found for {label} — nothing to approve.");
            return 1;
        }
        Program.Log($"Approved {files.Length} baseline(s) for {label}:");
        foreach (var f in files) Program.Log($"  + {test}/{f}");
        return 0;
    }

    private static int ApproveWholeSuite(string workloadsDir, string workload, string suite)
    {
        var suitePath = Path.Combine(workloadsDir, workload, "suites", $"{suite}.json");
        if (!File.Exists(suitePath))
        {
            Program.Log($"Error: suite definition not found: {suitePath}");
            return 1;
        }

        string[] tests;
        using (var doc = JsonDocument.Parse(File.ReadAllText(suitePath)))
        {
            if (!doc.RootElement.TryGetProperty("tests", out var testsEl) || testsEl.ValueKind != JsonValueKind.Array)
            {
                Program.Log($"Error: suite '{suite}' has no tests[] array.");
                return 1;
            }
            tests = testsEl.EnumerateArray()
                .Select(t => t.GetString())
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t!)
                .ToArray();
        }

        int approvedTotal = 0, testsBlessed = 0, testsSkipped = 0;
        foreach (var test in tests)
        {
            // Suite-scoped layout first (per-test-launch suites), then the SHARED layout
            // (results/<test>/ — every shared-runMode rhino suite writes there).
            string[] files;
            try { files = BaselineManager.ApproveTestFiles(workloadsDir, workload, test, suite); }
            catch (DirectoryNotFoundException)
            {
                try { files = BaselineManager.ApproveTestFiles(workloadsDir, workload, test, suiteName: null); }
                catch (DirectoryNotFoundException)
                {
                    Program.Log($"  - {test}: no candidates (test not run?) — skipped.");
                    testsSkipped++;
                    continue;
                }
            }
            if (files.Length == 0)
            {
                Program.Log($"  - {test}: candidates dir empty — skipped.");
                testsSkipped++;
                continue;
            }
            testsBlessed++;
            approvedTotal += files.Length;
            foreach (var f in files) Program.Log($"  + {test}/{f}");
        }

        Program.Log($"Suite '{suite}': approved {approvedTotal} baseline(s) across {testsBlessed} test(s)" +
                    (testsSkipped > 0 ? $"; {testsSkipped} test(s) had no candidates." : "."));
        return approvedTotal > 0 ? 0 : 1;
    }
}
