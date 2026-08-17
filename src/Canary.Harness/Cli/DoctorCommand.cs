using System.CommandLine;
using Canary.Config;
using Canary.Orchestration;

namespace Canary.Cli;

/// <summary>
/// <c>canary doctor</c> — reports whether this machine can actually run what it has.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 3. Three existing behaviours composed into the worst possible
/// failure: a suite naming an absent test silently shrank, a missing baseline yielded
/// <c>New</c>, and <c>New</c> is excluded from the exit code. A machine carrying 1 of 51
/// tests and no baselines reported a pass.
/// </para>
/// <para>
/// The exit-code semantics are deliberately NOT changed - <c>New</c> is correct for the
/// first run of a genuinely new test. What was missing is a check that answers "is this
/// install complete" BEFORE a run is trusted, and which fails loudly when it is not.
/// </para>
/// <para>
/// This is also the third class of check the payload verifier cannot do.
/// <c>verify-payload.ps1</c> confirms bytes are present and identical; it passes happily on
/// a payload whose tests reference roots this machine does not have. Byte integrity and
/// readiness are different questions.
/// </para>
/// </remarks>
public static class DoctorCommand
{
    private sealed class Findings
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Notes { get; } = new();
    }

    /// <summary>Creates the <c>doctor</c> subcommand.</summary>
    /// <returns>The configured command.</returns>
    public static Command Create()
    {
        var workloadOption = new Option<string?>(
            "--workload", "Check a specific workload's content as well as the machine.");
        var suiteOption = new Option<string?>(
            "--suite", "Check that every test a suite declares can actually be loaded.");
        var workloadsDirOption = new Option<string?>(
            "--workloads-dir", "Workloads root to check. Overrides discovery.");

        var command = new Command("doctor",
            "Check whether this machine can run the content it has: paths resolve, tokens resolve, suites are complete.")
        {
            workloadOption, suiteOption, workloadsDirOption,
        };

        command.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            ctx.ExitCode = await RunAsync(
                ctx.ParseResult.GetValueForOption(workloadOption),
                ctx.ParseResult.GetValueForOption(suiteOption),
                ctx.ParseResult.GetValueForOption(workloadsDirOption),
                new ConsoleTestLogger(verbose: false, quiet: false)).ConfigureAwait(false);
        });

        return command;
    }

    /// <summary>Runs the checks.</summary>
    /// <param name="workloadName">Optional workload to inspect.</param>
    /// <param name="suiteName">Optional suite to check for completeness.</param>
    /// <param name="workloadsDirOverride">Optional explicit workloads root.</param>
    /// <param name="logger">Where to report.</param>
    /// <returns>0 when everything checked is usable; 1 when anything is not.</returns>
    internal static async Task<int> RunAsync(
        string? workloadName, string? suiteName, string? workloadsDirOverride, ConsoleTestLogger logger)
    {
        var f = new Findings();

        // --- 1. the content root -------------------------------------------
        var res = CanaryPaths.ResolveWorkloadsRootDetailed(workloadsDirOverride);
        logger.Log($"workloads root : {CanaryPaths.Describe(res)}");
        if (!res.Exists)
        {
            f.Errors.Add($"no workloads directory at {CanaryPaths.Describe(res)}");
            return Report(f, logger);   // nothing else is checkable
        }
        var root = res.Path;

        // --- 2. the token table --------------------------------------------
        CanaryTokens.Invalidate();
        var problem = CanaryTokens.DescribeProblem(root);
        if (problem != null) f.Errors.Add(problem);

        var tokens = CanaryTokens.Load(root);
        var declared = tokens.Keys.Where(k => !k.StartsWith("__", StringComparison.Ordinal)).ToList();
        logger.Log($"tokens         : {declared.Count} declared in {CanaryTokens.TokensFileName}");

        // A token that RESOLVES to a path that does not exist is the QC failure mode: the
        // table is present and syntactically fine, and every path built from it is wrong.
        foreach (var name in declared.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var value = CanaryTokens.Expand($"%{name}%", root);
            if (Directory.Exists(value) || File.Exists(value)) continue;
            f.Errors.Add($"token %{name}% resolves to '{value}', which does not exist on this machine");
        }

        // --- 3. tokens the content uses but nothing declares ---------------
        var unresolved = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var contentFiles = 0;
        foreach (var file in EnumerateContent(root))
        {
            contentFiles++;
            foreach (var name in CanaryTokens.FindUnresolved(File.ReadAllText(file), root))
                unresolved.Add(name);
        }
        logger.Log($"content        : {contentFiles} test/suite files scanned");
        foreach (var name in unresolved)
            f.Errors.Add($"content uses %{name}% but nothing declares it (not in {CanaryTokens.TokensFileName}, not an environment variable)");

        // --- 4. suite completeness -----------------------------------------
        if (!string.IsNullOrWhiteSpace(suiteName))
        {
            if (string.IsNullOrWhiteSpace(workloadName))
                f.Errors.Add("--suite requires --workload");
            else
            {
                try
                {
                    var (suite, tests, missing) = await TestDiscovery
                        .DiscoverTestsForSuiteAsync(root, workloadName!, suiteName!, null).ConfigureAwait(false);
                    logger.Log($"suite {suiteName,-14}: {tests.Count} of {suite.Tests.Count} tests loadable");
                    foreach (var name in missing)
                        f.Errors.Add($"suite '{suiteName}' declares '{name}' but it is missing or unparsable");
                }
                catch (FileNotFoundException ex)
                {
                    f.Errors.Add(ex.Message);
                }
            }
        }

        // --- 5. the workload itself ----------------------------------------
        if (!string.IsNullOrWhiteSpace(workloadName))
        {
            var wl = Path.Combine(root, workloadName!, "workload.json");
            if (!File.Exists(wl))
                f.Errors.Add($"workload config not found: {wl}");
            else
            {
                try
                {
                    var cfg = await WorkloadConfig.LoadAsync(wl).ConfigureAwait(false);
                    var appPath = CanaryTokens.Expand(cfg.AppPath, root);
                    // A bare command name (npm.cmd, cmd.exe) is resolved via PATH by the
                    // launcher, so its absence here is not evidence of anything.
                    if (appPath.Contains(Path.DirectorySeparatorChar) || appPath.Contains('/'))
                    {
                        if (!File.Exists(appPath))
                            f.Warnings.Add($"workload appPath '{appPath}' does not exist");
                    }
                    else
                    {
                        f.Notes.Add($"workload appPath '{appPath}' is resolved via PATH at launch, not checked here");
                    }
                }
                catch (Exception ex)
                {
                    f.Errors.Add($"workload config {wl} could not be read: {ex.Message}");
                }
            }
        }

        return Report(f, logger);
    }

    private static IEnumerable<string> EnumerateContent(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            // Generated output, and content whose consumer cannot expand a token (sweeps
            // JSON is read by JavaScript that has no expansion at all).
            if (rel.Contains("/results/") || rel.Contains("/sessions/")) continue;
            if (rel.Contains("/sweeps/")) continue;
            if (rel.Equals(CanaryTokens.TokensFileName, StringComparison.OrdinalIgnoreCase)) continue;
            yield return file;
        }
    }

    private static int Report(Findings f, ConsoleTestLogger logger)
    {
        logger.Log(string.Empty);
        foreach (var n in f.Notes) logger.Log($"  note    {n}");
        foreach (var w in f.Warnings) logger.Log($"  WARN    {w}");
        foreach (var e in f.Errors) logger.Log($"  ERROR   {e}");

        if (f.Errors.Count == 0)
        {
            logger.Log(f.Warnings.Count == 0
                ? "doctor: OK — this machine can run the content it has."
                : $"doctor: OK with {f.Warnings.Count} warning(s).");
            return 0;
        }

        logger.Log($"doctor: {f.Errors.Count} error(s). This machine cannot be trusted to report on these tests.");
        return 1;
    }
}
