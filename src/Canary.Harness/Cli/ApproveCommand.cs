using System.CommandLine;
using System.Text.Json;
using Canary.Orchestration;
using Canary.Config;

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
/// Path semantics (Phase 2b): there is ONE layout, results/&lt;test&gt;/, derived by
/// <see cref="Canary.Orchestration.ResultPaths"/>. <c>--suite</c> selects WHICH tests to
/// bless and never affects WHERE the images go. The previous nested-then-flat fallback is
/// gone: it blessed at whichever layout it found and returned success, which would turn a
/// half-applied migration into a silent pass.
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
        var workloadsDir = CanaryPaths.ResolveWorkloadsRoot();

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
        var files = BaselineManager.ApproveTestFiles(workloadsDir, workload, test);
        // The suite, when given, is a SELECTOR - it says which tests to bless, never where
        // the images go. Phase 2b removed it from the path.
        var label = suite != null ? $"test '{test}' (selected via suite '{suite}')" : $"test '{test}'";
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
            // ONE layout, so no fallback. The nested-then-flat probe that used to live here
            // was load-bearing only while two layouts existed - and it was dangerous: it
            // blessed at whichever layout it happened to find and returned 0, which would
            // convert a half-applied migration into a silent success. Exactly the failure
            // this phase exists to make impossible.
            string[] files;
            try { files = BaselineManager.ApproveTestFiles(workloadsDir, workload, test); }
            catch (DirectoryNotFoundException)
            {
                Program.Log($"  - {test}: no candidates (test not run?) — skipped.");
                testsSkipped++;
                continue;
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
