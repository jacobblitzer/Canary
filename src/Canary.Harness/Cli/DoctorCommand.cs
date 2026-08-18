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
                        {
                            // Phase 5: an ERROR, not a warning. Doctor exiting 0 on a machine
                            // with no target application installed is indefensible - it is
                            // the most basic form of "this install is not complete", and it
                            // is exactly the question doctor now exists to answer.
                            f.Errors.Add($"workload appPath '{appPath}' does not exist — the target application is not installed here");
                        }
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

        // --- 6. the baseline ledger ----------------------------------------
        // Phase 2b. Checks 1-5 cannot see a baseline: EnumerateContent below skips any
        // path containing /results/, deliberately, because generated output is not
        // authored content. That means everything above passes happily on a machine
        // whose baselines are absent or unreachable - and a run in that state reports
        // New, which the exit code excludes, so it prints a pass while comparing
        // nothing. This check is the only one that looks.
        if (!string.IsNullOrWhiteSpace(workloadName))
        {
            try
            {
                var ledger = BaselineLedger.LoadRequired(root, workloadName!);
                var v = ledger.Verify(root, LedgerLayout.Dual);
                logger.Log($"baselines      : {v.Ok} of {ledger.Rows.Count} ledgered baseline(s) resolve and match");
                foreach (var m in v.Missing)
                    f.Errors.Add($"baseline {m}");
                // Bytes changing is a re-blessing, which is legitimate and shows up as a
                // git diff on the ledger. Absence is the defect.
                foreach (var c in v.Changed)
                    f.Warnings.Add($"baseline {c}");
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                f.Errors.Add(ex.Message);
            }
        }

        // --- 6b. declared preconditions, offline half -----------------------
        // Phase 5, and the reason this whole check exists: on 2026-08-17 a workload whose
        // Grasshopper plug-in had silently not registered cost a 300-second timeout that
        // logged nothing, while doctor exited 0 the entire time. Checks 1-6 verify Canary's
        // OWN content; nothing verified what that content DEPENDS ON.
        //
        // Only the offline half runs here - `file` and `service`, a syscall and one HTTP GET
        // - so this costs nothing and needs no app launch. `plugin` cannot be answered from
        // out here at all: Grasshopper's library table and Rhino's plug-in table exist only
        // inside the running app, and a file check on the .gha is NOT a substitute, because
        // Slop.gha was present on a scanned path and still did not register. Presence is not
        // loaded.
        if (!string.IsNullOrWhiteSpace(workloadName))
        {
            try
            {
                var wlPath = Path.Combine(root, workloadName!, "workload.json");
                WorkloadConfig? cfg = File.Exists(wlPath)
                    ? await WorkloadConfig.LoadAsync(wlPath).ConfigureAwait(false)
                    : null;

                // Scope: the named suite's tests when given, else every test in the workload.
                List<TestDefinition> scope;
                if (!string.IsNullOrWhiteSpace(suiteName))
                {
                    var (_, tests, _) = await TestDiscovery
                        .DiscoverTestsForSuiteAsync(root, workloadName!, suiteName!, null).ConfigureAwait(false);
                    scope = tests;
                }
                else
                {
                    scope = await TestDiscovery.DiscoverTestsAsync(root, workloadName!, null).ConfigureAwait(false);
                }

                var reqs = RequirementChecker.Collect(cfg, scope, workloadName!);
                var offline = reqs.Count(d => d.Requirement.IsOfflineCheckable);
                var inApp = reqs.Count - offline;
                logger.Log($"preconditions : {reqs.Count} declared ({offline} checkable here, {inApp} need the app running)");

                foreach (var miss in await RequirementChecker.CheckOfflineAsync(reqs, root).ConfigureAwait(false))
                    f.Errors.Add("PRECONDITION  " + miss.Format());

                if (inApp > 0)
                {
                    f.Notes.Add($"{inApp} plugin requirement(s) are NOT verified by doctor — they can only be " +
                                "checked from inside the running app, and are gated at run time via GetHostState");
                }
            }
            catch (FileNotFoundException ex)
            {
                f.Errors.Add(ex.Message);
            }
            catch (Exception ex)
            {
                f.Errors.Add($"precondition check failed: {ex.Message}");
            }
        }

        // --- 7. suite names must not collide with test names ---------------
        // results/ holds suite rollups and test directories as SIBLINGS, so a suite
        // named like a test would have them writing into each other. Zero collisions
        // today; this keeps it that way rather than discovering it later.
        if (!string.IsNullOrWhiteSpace(workloadName))
        {
            var testsDir = Path.Combine(root, workloadName!, "tests");
            var suitesDir = Path.Combine(root, workloadName!, "suites");
            if (Directory.Exists(testsDir) && Directory.Exists(suitesDir))
            {
                var testNames = Directory.GetFiles(testsDir, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => n != null)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var s in Directory.GetFiles(suitesDir, "*.json"))
                {
                    var name = Path.GetFileNameWithoutExtension(s);
                    if (name != null && testNames.Contains(name))
                        f.Errors.Add($"suite '{name}' collides with a test of the same name — " +
                                     "their results/ directories would overlap");
                }
            }
        }

        // --- 8. the environment capture, and the clashes in it ---------------
        // Phase 5b. Checks 1-7 all answer from files on disk; this one reports what the
        // target APPLICATION said about itself the last time anyone asked. Doctor still
        // launches nothing - it reads the capture `canary run` and `canary env` write - so a
        // machine that has never been probed is a WARNING pointing at `canary env`, not an
        // error. Doctor's whole job is to run BEFORE anything is trusted, and a gate that
        // cannot pass until a run has happened would be useless on a fresh QC install.
        //
        // A hard clash IS an error. The capture's Error tier means a dependency the content
        // needs is not usable - a library loaded twice from two places, for instance - which
        // no amount of correct content on disk can compensate for.
        if (!string.IsNullOrWhiteSpace(workloadName))
        {
            var capturePath = EnvironmentCapture.PathFor(root, workloadName!);
            if (!File.Exists(capturePath))
            {
                f.Warnings.Add($"no environment capture for '{workloadName}' — nothing has asked the " +
                               $"application what it has loaded. Run `canary env --workload {workloadName}`");
            }
            else
            {
                try
                {
                    var capture = EnvironmentCapture.Load(capturePath);
                    var errors = capture.Findings.Count(c => c.Severity == ClashSeverity.Error);
                    var warnings = capture.Findings.Count(c => c.Severity == ClashSeverity.Warning);
                    logger.Log($"environment    : {MachineIdentity.Format(capture.Machine)}, captured {capture.CapturedUtc} — " +
                               $"{errors} error, {warnings} warning, {capture.Findings.Count - errors - warnings} note");

                    // A capture from somewhere else is the QC trap this exists to catch: copy a
                    // results tree between machines and the target appears to have been verified
                    // when nothing on it was ever probed.
                    if (!capture.IsFromThisMachine())
                    {
                        f.Errors.Add($"the environment capture is from another machine " +
                                     $"({MachineIdentity.Format(capture.Machine)}); this is {Environment.MachineName}. " +
                                     $"It says nothing about THIS machine — re-run `canary env --workload {workloadName}`");
                    }
                    else if (capture.Age() is { } age && age > TimeSpan.FromDays(7))
                    {
                        f.Warnings.Add($"the environment capture is {(int)age.TotalDays} days old; " +
                                       "plug-ins may have moved since");
                    }

                    foreach (var c in capture.Findings.Where(c => c.Severity == ClashSeverity.Error))
                        f.Errors.Add($"environment [{c.Kind}] {c.Detail}");
                    foreach (var c in capture.Findings.Where(c => c.Severity == ClashSeverity.Warning))
                        f.Warnings.Add($"environment [{c.Kind}] {c.Detail}");
                }
                catch (InvalidDataException ex)
                {
                    // A corrupt capture is an error, not a missing one: it read as SOMETHING,
                    // and silently degrading it to "not captured yet" would hide a real fault.
                    f.Errors.Add(ex.Message);
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
