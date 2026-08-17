using System.CommandLine;
using Canary.Config;
using Canary.Orchestration;

namespace Canary.Cli;

/// <summary>
/// <c>canary baselines</c> — lock, verify and list the git-tracked record of which
/// checkpoints have an approved baseline.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 2b. See <see cref="BaselineLedger"/> for why the record
/// lives outside <c>results/</c> and is keyed on identity rather than path.
/// </para>
/// <para>
/// Forms:
/// <list type="bullet">
/// <item><c>canary baselines lock --workload rhino --expect-rows 74</c> — write the ledger</item>
/// <item><c>canary baselines verify --workload rhino</c> — check every row against disk</item>
/// <item><c>canary baselines list --workload rhino</c> — print the rows</item>
/// </list>
/// </para>
/// </remarks>
public static class BaselinesCommand
{
    /// <summary>Creates the <c>baselines</c> command and its subcommands.</summary>
    /// <returns>The configured command.</returns>
    public static Command Create()
    {
        var command = new Command("baselines",
            "Lock, verify and list the record of which checkpoints have an approved baseline.");

        command.AddCommand(CreateLock());
        command.AddCommand(CreateVerify());
        command.AddCommand(CreateList());
        return command;
    }

    private static Option<string> WorkloadOption() =>
        new("--workload", "Workload name (e.g. rhino)") { IsRequired = true };

    private static Option<string?> WorkloadsDirOption() =>
        new("--workloads-dir", "Workloads root. Overrides discovery.");

    private static Option<string> LayoutOption() =>
        new("--layout", () => "dual",
            "Resolution rule: 'dual' (flat, then suite-nested — today's behaviour) or 'flat' (the post-cutover contract).");

    private static Command CreateLock()
    {
        var workload = WorkloadOption();
        var workloadsDir = WorkloadsDirOption();
        var layout = LayoutOption();

        // REQUIRED, and this is the whole point of it. Locking against the post-cutover
        // resolver instead of today's dual rule silently yields 40 penumbra rows instead
        // of 93 — and `verify` is GREEN on that truncated ledger, because all 40 resolve.
        // The row count is the only tell, so it is a gate rather than a glance.
        var expectRows = new Option<int>("--expect-rows",
            "Refuse to write unless the scan produces exactly this many rows.") { IsRequired = true };

        var cmd = new Command("lock", "Scan content and disk, and write the ledger.")
        {
            workload, workloadsDir, layout, expectRows,
        };

        cmd.SetHandler((System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            ctx.ExitCode = RunLock(
                ctx.ParseResult.GetValueForOption(workload)!,
                ctx.ParseResult.GetValueForOption(workloadsDir),
                ctx.ParseResult.GetValueForOption(layout)!,
                ctx.ParseResult.GetValueForOption(expectRows),
                new ConsoleTestLogger(verbose: false, quiet: false));
        });

        return cmd;
    }

    private static Command CreateVerify()
    {
        var workload = WorkloadOption();
        var workloadsDir = WorkloadsDirOption();
        var layout = LayoutOption();

        var cmd = new Command("verify", "Check that every ledgered baseline still resolves.")
        {
            workload, workloadsDir, layout,
        };

        cmd.SetHandler((System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            ctx.ExitCode = RunVerify(
                ctx.ParseResult.GetValueForOption(workload)!,
                ctx.ParseResult.GetValueForOption(workloadsDir),
                ctx.ParseResult.GetValueForOption(layout)!,
                new ConsoleTestLogger(verbose: false, quiet: false));
        });

        return cmd;
    }

    private static Command CreateList()
    {
        var workload = WorkloadOption();
        var workloadsDir = WorkloadsDirOption();

        var cmd = new Command("list", "Print the ledgered checkpoints.") { workload, workloadsDir };

        cmd.SetHandler((System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            ctx.ExitCode = RunList(
                ctx.ParseResult.GetValueForOption(workload)!,
                ctx.ParseResult.GetValueForOption(workloadsDir),
                new ConsoleTestLogger(verbose: false, quiet: false));
        });

        return cmd;
    }

    private static bool TryLayout(string text, ConsoleTestLogger logger, out LedgerLayout layout)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "dual": layout = LedgerLayout.Dual; return true;
            case "flat": layout = LedgerLayout.Flat; return true;
            default:
                layout = LedgerLayout.Dual;
                logger.Log($"Error: unknown --layout '{text}'. Use 'dual' or 'flat'.");
                return false;
        }
    }

    private static bool TryRoot(string? overrideDir, ConsoleTestLogger logger, out string root)
    {
        var res = CanaryPaths.ResolveWorkloadsRootDetailed(overrideDir);
        root = res.Path;
        if (res.Exists) return true;
        logger.Log($"Error: no workloads directory at {CanaryPaths.Describe(res)}");
        return false;
    }

    /// <summary>Scans and writes a workload's ledger.</summary>
    /// <param name="workloadName">Workload to lock.</param>
    /// <param name="workloadsDirOverride">Optional explicit workloads root.</param>
    /// <param name="layoutText">'dual' or 'flat'.</param>
    /// <param name="expectRows">Required row count; a mismatch refuses the write.</param>
    /// <param name="logger">Where to report.</param>
    /// <returns>0 on success; 1 on any refusal.</returns>
    internal static int RunLock(
        string workloadName, string? workloadsDirOverride, string layoutText, int expectRows,
        ConsoleTestLogger logger)
    {
        if (!TryLayout(layoutText, logger, out var layout)) return 1;
        if (!TryRoot(workloadsDirOverride, logger, out var root)) return 1;

        var scan = BaselineLedger.Scan(root, workloadName, layout);

        logger.Log($"workload       : {workloadName}  (layout {layout.ToString().ToLowerInvariant()})");
        logger.Log($"checkpoints    : {scan.Armed} armed, {scan.CaptureOnly} capture-only, {scan.Vlm} vlm");
        logger.Log($"resolved       : {scan.ResolvedFlat} flat, {scan.ResolvedNested} suite-nested, {scan.Unresolved} with no baseline");
        logger.Log($"rows           : {scan.Rows.Count}");

        foreach (var u in scan.UnparsableTests)
            logger.Log($"  WARN    unparsable test file, no rows from it — {u}");

        if (scan.Rows.Count != expectRows)
        {
            logger.Log(string.Empty);
            logger.Log($"Error: expected {expectRows} rows, produced {scan.Rows.Count}.");
            logger.Log("Refusing to write. A ledger that is short by N rows leaves N armed checkpoints");
            logger.Log("unprotected, and 'verify' would be GREEN on it because every row it does contain");
            logger.Log("resolves. The row count is the only thing that can tell you.");
            if (layout == LedgerLayout.Flat && scan.ResolvedNested == 0 && scan.Unresolved > 0)
                logger.Log("Hint: --layout flat cannot see suite-nested baselines. Lock with --layout dual first.");
            return 1;
        }

        var ledger = new BaselineLedger { Workload = workloadName, Rows = scan.Rows.ToList() };
        ledger.Save(root);
        logger.Log(string.Empty);
        logger.Log($"wrote {BaselineLedger.PathFor(root, workloadName)}");

        // Verify what we just wrote, by re-reading it. Writing and trusting the write is
        // how a guard ends up attesting to something that is not on disk.
        var check = BaselineLedger.LoadRequired(root, workloadName).Verify(root, layout);
        if (!check.IsSatisfied)
        {
            foreach (var m in check.Missing.Take(10)) logger.Log($"  ERROR   {m}");
            logger.Log("Error: the ledger just written does not verify. Not usable.");
            return 1;
        }

        logger.Log($"verified: {check.Ok} of {ledger.Rows.Count} rows resolve with a matching hash.");
        return 0;
    }

    /// <summary>Verifies a workload's ledger against disk.</summary>
    /// <param name="workloadName">Workload to verify.</param>
    /// <param name="workloadsDirOverride">Optional explicit workloads root.</param>
    /// <param name="layoutText">'dual' or 'flat'.</param>
    /// <param name="logger">Where to report.</param>
    /// <returns>0 when nothing is missing; 1 otherwise. Hash drift warns only.</returns>
    internal static int RunVerify(
        string workloadName, string? workloadsDirOverride, string layoutText, ConsoleTestLogger logger)
    {
        if (!TryLayout(layoutText, logger, out var layout)) return 1;
        if (!TryRoot(workloadsDirOverride, logger, out var root)) return 1;

        BaselineLedger ledger;
        try
        {
            ledger = BaselineLedger.LoadRequired(root, workloadName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            logger.Log($"Error: {ex.Message}");
            return 1;
        }

        var v = ledger.Verify(root, layout);
        logger.Log($"workload       : {workloadName}  (layout {layout.ToString().ToLowerInvariant()})");
        logger.Log($"rows           : {ledger.Rows.Count}");
        logger.Log($"resolve + match: {v.Ok}");

        foreach (var c in v.Changed) logger.Log($"  WARN    {c}");
        foreach (var m in v.Missing) logger.Log($"  ERROR   {m}");

        if (!v.IsSatisfied)
        {
            logger.Log(string.Empty);
            logger.Log($"baselines verify: {v.Missing.Count} ledgered baseline(s) do not resolve.");
            logger.Log("A run in this state reports New, and New is excluded from the exit code —");
            logger.Log("so it would print a pass while comparing nothing.");
            return 1;
        }

        logger.Log(v.Changed.Count == 0
            ? "baselines verify: OK."
            : $"baselines verify: OK with {v.Changed.Count} hash change(s) to review.");
        return 0;
    }

    /// <summary>Prints a workload's ledger rows.</summary>
    /// <param name="workloadName">Workload to list.</param>
    /// <param name="workloadsDirOverride">Optional explicit workloads root.</param>
    /// <param name="logger">Where to report.</param>
    /// <returns>0 on success; 1 when the ledger is unusable.</returns>
    internal static int RunList(string workloadName, string? workloadsDirOverride, ConsoleTestLogger logger)
    {
        if (!TryRoot(workloadsDirOverride, logger, out var root)) return 1;

        BaselineLedger ledger;
        try
        {
            ledger = BaselineLedger.LoadRequired(root, workloadName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            logger.Log($"Error: {ex.Message}");
            return 1;
        }

        foreach (var r in ledger.Rows)
            logger.Log($"{r.Test,-46} {r.Checkpoint,-18} {r.Mode,-10} {r.Sha256[..12]} {r.ApprovedUtc}");
        logger.Log($"{ledger.Rows.Count} row(s).");
        return 0;
    }
}
