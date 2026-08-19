using System.CommandLine;
using Canary.Commissioning;
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
    /// <summary>Exit code when checks could not run. NOT the same as a failing check.</summary>
    /// <remarks>
    /// <para>
    /// Its own code rather than reusing 1, because 1 already means "the install is
    /// incomplete" and that has an owner and a fix. "Nothing has asked this machine yet" has
    /// neither. Collapsing the two into one number is the same mistake, one layer down, as
    /// collapsing a broken harness into a broken plug-in.
    /// </para>
    /// <para>
    /// The reason this needed a code at all: <c>qc-capture.ps1</c> judges doctor by its exit
    /// code and never by scraping its text - deliberately, and correctly. So two checks that
    /// on a fresh payload have NEVER RUN (no environment capture, no commissioning report)
    /// were warnings, which means they were structurally invisible to the bundle that comes
    /// back from the QC machine. A bundle could report a green install for a machine nothing
    /// had ever looked at.
    /// </para>
    /// </remarks>
    public const int ExitNotProven = 5;

    private sealed class Findings
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Notes { get; } = new();

        /// <summary>Checks that could not run here. An unrun check is not a passing check.</summary>
        /// <remarks>
        /// Separate from <see cref="Errors"/> so install-vs-harness stays separable, and
        /// separate from <see cref="Warnings"/> because a warning does not move the exit code
        /// and this must. The campaign's rule, enforced in the one place a script reads.
        /// </remarks>
        public List<string> Unproven { get; } = new();

        /// <summary>Requirements only the running app can answer.</summary>
        /// <remarks>
        /// Recorded rather than reported immediately: whether these being unchecked matters
        /// depends on whether a same-machine capture exists, and that is not known until
        /// check 8 has run. Evaluated once at the end.
        /// </remarks>
        public int InAppRequirements { get; set; }

        /// <summary>Whether a capture taken on THIS machine was found and read.</summary>
        public bool HaveLocalCapture { get; set; }

        /// <summary>Whether any workload here declares a plug-in requirement.</summary>
        /// <remarks>
        /// Decides whether an absent package map is merely untidy or actively disables the
        /// only route to a fix.
        /// </remarks>
        public bool DeclaresPlugins { get; set; }

        /// <summary>
        /// Faults in the HARNESS, kept apart from faults in the install.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The campaign requires a three-way distinction, and collapsing it is how a day gets
        /// wasted: <c>doctor</c> red means the install is incomplete and is <b>not</b> a defect
        /// in the plug-in; commissioning red means the harness is broken and <b>every</b>
        /// result from this machine is unreadable, whatever the install looks like. Those are
        /// different problems with different owners and different fixes.
        /// </para>
        /// <para>
        /// So a harness fault does not go in <see cref="Errors"/> — it would be reported as an
        /// install problem and send someone to fix the wrong thing. It still makes doctor exit
        /// non-zero, because a machine whose comparer is unproven must not be allowed to
        /// proceed quietly; the distinction lives in the verdict, and
        /// <c>canary commission</c>'s own exit code (4) is what a script tests to tell them
        /// apart.
        /// </para>
        /// </remarks>
        public List<string> HarnessFaults { get; } = new();
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
    /// <param name="logger">
    /// Where to report. Widened from <c>ConsoleTestLogger</c> to the interface for the same
    /// reason this method is public: the UI has no console. Every line doctor emits goes
    /// through <c>Log</c>, so the caller decides where it lands - and the UI therefore shows
    /// the SAME text the CLI prints rather than a second rendering that can drift from it.
    /// </param>
    /// <returns>0 when everything checked is usable; 1 when anything is not.</returns>
    /// <remarks>
    /// <b>Public</b> so the Pretest tab can ask doctor directly rather than shelling out to
    /// canary.exe and scraping its text. The three signals a QC operator needs - harness,
    /// install, plug-in - must stay distinguishable, and a report that carries only two of
    /// them (as the first version of the Pretest report did) invites the reader to infer the
    /// third.
    /// </remarks>
    public static async Task<int> RunAsync(
        string? workloadName, string? suiteName, string? workloadsDirOverride, ITestLogger logger)
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

        var tokensFile = Path.Combine(root, CanaryTokens.TokensFileName);
        var tokensFilePresent = File.Exists(tokensFile);

        var tokens = CanaryTokens.Load(root);
        var declared = tokens.Keys.Where(k => !k.StartsWith("__", StringComparison.Ordinal)).ToList();
        logger.Log($"tokens         : {declared.Count} declared in {CanaryTokens.TokensFileName}");

        // A token that RESOLVES to a path that does not exist is the QC failure mode: the
        // table is present and syntactically fine, and every path built from it is wrong.
        foreach (var name in declared.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var value = CanaryTokens.Expand($"%{name}%", root);

            // Naming the OVERRIDE matters on a QC machine: tokens.json documents that any
            // entry can be repointed by an environment variable of the same name, and a
            // reader comparing two machines' doctor output otherwise cannot tell a shipped
            // default from a local repoint that happens to resolve.
            var fromEnv = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
            var how = fromEnv ? " (from the environment, overriding the table)" : string.Empty;

            if (Directory.Exists(value) || File.Exists(value))
            {
                if (fromEnv) f.Notes.Add($"token %{name}% is overridden by an environment variable to '{value}'");
                continue;
            }
            f.Errors.Add($"token %{name}% resolves to '{value}'{how}, which does not exist on this machine");
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

        // An ABSENT table is not an empty one. Nothing said so: the loader has no else branch
        // for a missing file, its parse-error field is only set by a JsonException, and doctor
        // then printed "0 declared in tokens.json" - a line naming a file it never found. The
        // Drive payload shipped without one for weeks and that line read as a clean zero.
        //
        // Reported HERE rather than at check 2 because severity depends on what the content
        // needs. A root whose content uses no token at all is fine without a table, and
        // calling that an error is how a check earns its way onto someone's ignore list. When
        // content DOES use tokens the per-token errors above have already fired; this names
        // the single cause behind all of them, which is the part a reader acts on.
        if (!tokensFilePresent)
        {
            var where = $"{CanaryTokens.TokensFileName} not found at {root}";
            if (unresolved.Count > 0)
            {
                f.Errors.Add($"{where} - an absent token table is not an empty one, and it is why the " +
                             $"{unresolved.Count} unresolved token(s) above have nothing to resolve against");
            }
            else
            {
                f.Notes.Add($"{where} - no content here uses a token, so nothing is broken by its absence");
            }
        }

        // --- 4. suite completeness -----------------------------------------
        // EVERY suite in the workload, not only one the caller thought to name.
        //
        // This check was opt-in for its whole life: gated behind --suite, so a doctor run
        // that named no suite verified NO suite. It cost exactly one live defect, and that
        // defect sat in the tree for three months. qualia's suites/multi-display.json names
        // 11 rh2-* tests whose JSON has never parsed - the malformed quoting is present in
        // the commit that created them (870cad9, 2026-05-14). Named explicitly the check
        // reported "0 of 11 tests loadable" and errored on every one; nobody ever named it.
        //
        // A completeness check you have to ask for is not a completeness check. Same shape
        // as bug 0022: the guard was correct and simply never ran.
        if (!string.IsNullOrWhiteSpace(suiteName) && string.IsNullOrWhiteSpace(workloadName))
        {
            f.Errors.Add("--suite requires --workload");
        }
        else if (!string.IsNullOrWhiteSpace(workloadName))
        {
            var suites = string.IsNullOrWhiteSpace(suiteName)
                ? EnumerateSuiteNames(root, workloadName!)
                : new[] { suiteName! };

            // A workload with no suites made this loop run ZERO times and print nothing, so a
            // doctor report that had LOST its suites folder was simply shorter than one that
            // had not, and otherwise identical - still ending "doctor: OK". Detecting it
            // required noticing a line that was not there, the same shape as bug 0022.
            //
            // The count is the fix. The severity is deliberately only a warning: a workload
            // can legitimately have no suite and be driven test by test, and a check that
            // cannot be satisfied is a check someone switches off.
            logger.Log($"suites         : {suites.Count} declared for '{workloadName}'");
            if (suites.Count == 0)
            {
                f.Warnings.Add($"workload '{workloadName}' declares no suites under " +
                               $"{Path.Combine(root, workloadName!, "suites")} - nothing can be run as a set. " +
                               "On a payload this means the suite did not travel");
            }

            foreach (var name in suites)
            {
                try
                {
                    var (suite, tests, missing) = await TestDiscovery
                        .DiscoverTestsForSuiteAsync(root, workloadName!, name, null).ConfigureAwait(false);
                    logger.Log($"suite {name,-22}: {tests.Count} of {suite.Tests.Count} tests loadable");
                    foreach (var m in missing)
                        f.Errors.Add($"suite '{name}' declares '{m}' but it is missing or unparsable");
                }
                catch (FileNotFoundException ex)
                {
                    f.Errors.Add(ex.Message);
                }
            }
        }

        // --- 4b. every test file must parse, suite membership or not --------
        // The other half of the same hole. A test no suite names is never opened by check 4
        // even now, so an unparsable one is invisible: it is not "failing", it simply is not
        // there. 18 of qualia's tests are in no suite. An unreadable test definition is a
        // defect wherever it sits, and finding it should not depend on someone having wired
        // it into a suite first.
        if (!string.IsNullOrWhiteSpace(workloadName))
        {
            var testsDir = Path.Combine(root, workloadName!, "tests");
            if (!Directory.Exists(testsDir))
            {
                // The guard used to wrap the summary line too, so an absent tests directory
                // produced silence. Absence is now stated, and it is an error: a workload with
                // no tests cannot be the thing anyone meant to ship.
                f.Errors.Add($"no tests directory at {testsDir} - this workload's content did not arrive");
                logger.Log($"test files     : NONE - {testsDir} does not exist");
            }
            else
            {
                var files = Directory.GetFiles(testsDir, "*.json");
                var unreadable = 0;
                foreach (var file in files)
                {
                    try { TestDefinition.Parse(File.ReadAllText(file)); }
                    catch (Exception ex)
                    {
                        unreadable++;
                        f.Errors.Add($"test '{Path.GetFileNameWithoutExtension(file)}' does not parse: {ex.Message}");
                    }
                }
                logger.Log($"test files     : {files.Length - unreadable} of {files.Length} parse");
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
                if (reqs.Any(d => string.Equals(d.Requirement.Kind, Requirement.KindPlugin, StringComparison.OrdinalIgnoreCase))) f.DeclaresPlugins = true;
                logger.Log($"preconditions : {reqs.Count} declared ({offline} checkable here, {inApp} need the app running)");

                foreach (var miss in await RequirementChecker.CheckOfflineAsync(reqs, root).ConfigureAwait(false))
                    f.Errors.Add("PRECONDITION  " + miss.Format());

                if (inApp > 0)
                {
                    f.Notes.Add($"{inApp} plugin requirement(s) are NOT verified by doctor — they can only be " +
                                "checked from inside the running app, and are gated at run time via GetHostState");

                    // Whether that note is benign depends on something not known yet: if a
                    // capture from THIS machine exists, the app has already answered for them.
                    // If it does not, this many requirements have never been checked by
                    // anything at all, and a note does not move the exit code. Decided at the end.
                    f.InAppRequirements = inApp;
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
                f.Unproven.Add($"no environment capture for '{workloadName}' — nothing has asked the " +
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
                    f.HaveLocalCapture = capture.IsFromThisMachine();

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

        // --- 9. has the HARNESS itself been proven on this machine? ------------
        // Stage C2/C3, ruling 7A. Checks 1-8 all ask "is the content complete and does it
        // resolve here". None of them asks whether Canary's own comparer and capture path
        // work at all - and if they do not, every answer above is beside the point.
        //
        // Deliberately NOT scoped to --workload: commissioning is about the machine, not
        // about one workload's content, and its report lives under the commissioning workload
        // wherever the caller happened to point doctor.
        {
            var reportPath = CommissioningReport.PathFor(root);
            if (!File.Exists(reportPath))
            {
                // A warning, not an error, and for the same reason check 8's absent capture is:
                // doctor has to be runnable on a machine where nothing has happened yet. It is
                // the FIRST thing you run on a fresh QC box, so it cannot require that
                // something already ran.
                f.Unproven.Add("the harness has not been commissioned on this machine — run " +
                               "`canary commission --workload <w>`. Until it passes, no test result " +
                               "from this machine can be read, whatever the checks above say");
            }
            else
            {
                try
                {
                    var commissioning = CommissioningReport.Load(reportPath);
                    var summary = string.Join(", ", commissioning.Layers
                        .OrderBy(l => l.Number)
                        .Select(l => $"{l.Name}={l.Outcome.ToString().ToLowerInvariant()}"));
                    logger.Log($"commissioning  : {MachineIdentity.Format(commissioning.Machine)}, " +
                               $"{commissioning.CapturedUtc} — {summary}");

                    // A report from somewhere else is an integrity problem in doctor's own
                    // domain - the state on THIS machine is not what it appears - so it is a
                    // genuine error rather than a harness fault.
                    if (!MachineIdentity.IsThisMachine(commissioning.Machine))
                    {
                        f.Errors.Add($"the commissioning report is from another machine " +
                                     $"({MachineIdentity.Format(commissioning.Machine)}); this is " +
                                     $"{Environment.MachineName}. It says nothing about THIS harness — " +
                                     "re-run `canary commission`");
                    }
                    else if (!commissioning.HarnessUsable)
                    {
                        // A layer that RAN and disagreed is a harness fault. A layer that could
                        // not start because its inputs never arrived is not - and routing both
                        // into HarnessFaults made doctor print "This is NOT an install problem"
                        // over the two failures a payload machine actually produces: the
                        // commissioning content did not travel, and the agent is not registered.
                        // That sentence asserted the opposite of the truth, and its advice was
                        // a dead end.
                        foreach (var l in commissioning.Layers.Where(l => l.Fatal && l.Outcome == LayerOutcome.Failed))
                            f.HarnessFaults.Add($"layer {l.Number} {l.Name}: {l.Detail}");

                        foreach (var l in commissioning.Layers.Where(l => l.Fatal && l.Outcome == LayerOutcome.NotRun))
                        {
                            var kind = l.ContentFault
                                ? "could not run because something is not installed or did not ship"
                                : "did not run";
                            f.Unproven.Add($"commissioning layer {l.Number} {l.Name} {kind}: {l.Detail}");
                        }
                    }

                    // A non-fatal layer that failed is worth saying out loud: it means pixel
                    // baselines do not travel to this machine, which changes how its results
                    // must be read without making it unusable.
                    foreach (var l in commissioning.Layers.Where(l => !l.Fatal && l.Outcome == LayerOutcome.Failed))
                        f.Warnings.Add($"commissioning layer {l.Number} {l.Name}: {l.Detail}");
                }
                catch (InvalidDataException ex)
                {
                    f.Errors.Add(ex.Message);
                }
            }
        }

        // --- 10. the runtime the payload does not carry --------------------
        CheckDesktopRuntime(f, logger);

        // --- 11. the map from requirement ids to installable packages -------
        CheckPackageMap(f, root, f.DeclaresPlugins);

        return Report(f, logger);
    }

    /// <summary>Every suite a workload declares, by name.</summary>
    /// <param name="root">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <returns>Suite names, ordered; empty when the workload has no suites directory.</returns>
    private static IReadOnlyList<string> EnumerateSuiteNames(string root, string workload)
    {
        var dir = Path.Combine(root, workload, "suites");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    /// <summary>
    /// Check 10 - the runtime the payload does not carry.
    /// </summary>
    /// <param name="f">Findings to add to.</param>
    /// <param name="logger">Where to report.</param>
    /// <remarks>
    /// Both shipped projects are <c>net8.0-windows</c> and the payload is published
    /// framework-dependent - no <c>--self-contained</c>, no RuntimeIdentifier anywhere. So it
    /// REQUIRES the .NET 8 Windows Desktop Runtime on the target, ships nothing that provides
    /// it, and until now asserted nothing that checks it. When it is absent, canary.exe dies
    /// with the apphost's own "You must install .NET" message before a single line of Canary
    /// code runs - which reads as a corrupt payload, and sends someone to re-copy files that
    /// were never the problem.
    /// </remarks>
    private static void CheckDesktopRuntime(Findings f, ITestLogger logger)
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--list-runtimes",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc == null)
            {
                f.Unproven.Add("could not run `dotnet --list-runtimes` - cannot tell whether the .NET 8 " +
                               "Windows Desktop Runtime is present");
                return;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(15000);

            var desktop = stdout.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("Microsoft.WindowsDesktop.App 8.", StringComparison.Ordinal))
                .ToList();

            if (desktop.Count > 0)
            {
                logger.Log($"runtime        : {desktop.Count} .NET 8 Windows Desktop runtime(s) present");
            }
            else
            {
                f.Errors.Add("the .NET 8 Windows Desktop Runtime is not installed. This payload is published " +
                             "framework-dependent and carries no runtime, so canary.exe cannot start at all - " +
                             "install it from https://dotnet.microsoft.com/download/dotnet/8.0 " +
                             "(Desktop Runtime, x64)");
            }
        }
        catch (Exception ex)
        {
            // Not an error: `dotnet` being absent from PATH does not prove the runtime is
            // absent, because the apphost finds it without the CLI. Unproven is the honest
            // answer, and it is the tier that exists precisely for this.
            f.Unproven.Add($"could not determine whether the .NET 8 Windows Desktop Runtime is present " +
                           $"({ex.GetType().Name}). The apphost does not need `dotnet` on PATH, so this is " +
                           "not evidence either way");
        }
    }

    /// <summary>
    /// Check 11 - the map from requirement ids to installable packages.
    /// </summary>
    /// <param name="f">Findings to add to.</param>
    /// <param name="root">Workloads root.</param>
    /// <param name="declaresPlugins">Whether any workload here declares a plug-in.</param>
    /// <remarks>
    /// <see cref="InstallReadiness"/> returns an empty map when this file is absent, with a
    /// comment handing the reporting job to doctor - and doctor never took it. Nobody
    /// reported it anywhere. The visible effect is in the UI: every requirement renders as
    /// "no package provides this", and the install plan, which is gated on a package name
    /// being present, can never appear. The tab's only remediation surface is dead and
    /// nothing on screen says why.
    /// </remarks>
    private static void CheckPackageMap(Findings f, string root, bool declaresPlugins)
    {
        var mapPath = Path.Combine(root, InstallReadiness.PackageMapFileName);
        if (File.Exists(mapPath)) return;

        var msg = $"{InstallReadiness.PackageMapFileName} not found at {root} - plug-in requirements " +
                  "cannot be mapped to packages, so no fix can be named and the install plan cannot be built";
        if (declaresPlugins) f.Unproven.Add(msg); else f.Warnings.Add(msg);
    }

    private static int Report(Findings f, ITestLogger logger)
    {
        // An in-app requirement is only unchecked if nothing on this machine ever asked the
        // app. With a same-machine capture, the app has already answered for all of them.
        if (f.InAppRequirements > 0 && !f.HaveLocalCapture)
        {
            f.Unproven.Add($"{f.InAppRequirements} plug-in requirement(s) can only be answered by the running " +
                           "app, and no capture from this machine exists - so nothing has checked them");
        }

        logger.Log(string.Empty);
        foreach (var n in f.Notes) logger.Log($"  note    {n}");
        foreach (var w in f.Warnings) logger.Log($"  WARN    {w}");
        foreach (var u in f.Unproven) logger.Log($"  UNPROVEN {u}");
        foreach (var e in f.Errors) logger.Log($"  ERROR   {e}");
        foreach (var h in f.HarnessFaults) logger.Log($"  HARNESS {h}");

        // The verdict names WHICH of the two problems this is, because they have different
        // owners: an install fault is content that did not arrive or does not resolve; a
        // harness fault is Canary's own comparer or capture path not working here, and no
        // amount of correct content compensates for it.
        if (f.HarnessFaults.Count > 0)
        {
            logger.Log(string.Empty);
            logger.Log($"doctor: THE HARNESS ITSELF IS NOT PROVEN on this machine " +
                       $"({f.HarnessFaults.Count} failing layer(s))." +
                       (f.Errors.Count > 0 ? $" There are also {f.Errors.Count} install error(s)." : string.Empty));
            logger.Log("        This is NOT an install problem. Fix it with `canary commission` first —");
            logger.Log("        until it passes, no test result from this machine is readable.");
            return 1;
        }

        if (f.Errors.Count > 0)
        {
            logger.Log($"doctor: {f.Errors.Count} error(s). This machine cannot be trusted to report on these tests.");
            return 1;
        }

        // Definite failures outrank unknowns, so this is tested after Errors and after
        // HarnessFaults - but BEFORE OK, because the whole point is that it is not one.
        if (f.Unproven.Count > 0)
        {
            logger.Log(string.Empty);
            logger.Log($"doctor: NOT PROVEN — {f.Unproven.Count} check(s) could not run on this machine.");
            logger.Log("        This is NOT a pass and NOT an install failure. Nothing above has been");
            logger.Log("        contradicted; some of it has simply never been asked.");
            return ExitNotProven;
        }

        logger.Log(f.Warnings.Count == 0
            ? "doctor: OK — this machine can run the content it has."
            : $"doctor: OK with {f.Warnings.Count} warning(s).");
        return 0;
    }
}
