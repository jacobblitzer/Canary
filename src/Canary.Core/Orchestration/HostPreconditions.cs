using Canary.Config;

namespace Canary.Orchestration;

/// <summary>
/// Raised when the machine is missing something the content declared it needs.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 5. This is deliberately NOT a test failure and must never be
/// reported as one. A failing test says "the software is wrong"; an unmet precondition says
/// "this machine cannot answer the question at all". Conflating them is how a broken
/// install gets mistaken for a broken product, and vice versa.
/// </para>
/// <para>
/// It is thrown BEFORE any setup command runs, so nothing has been opened and no fixture
/// has been touched when it fires.
/// </para>
/// </remarks>
public sealed class PreconditionFailedException : Exception
{
    /// <summary>The unmet requirements, in declaration order.</summary>
    public IReadOnlyList<RequirementMiss> Misses { get; }

    /// <summary>What the host reported it did have, for the operator to compare against.</summary>
    public string LoadedSummary { get; }

    /// <summary>Load errors the host reported, if any.</summary>
    public string LoadErrors { get; }

    /// <summary>Constructs the exception.</summary>
    /// <param name="misses">Unmet requirements.</param>
    /// <param name="loadedSummary">What the host reported as loaded.</param>
    /// <param name="loadErrors">Any load errors the host reported.</param>
    public PreconditionFailedException(
        IReadOnlyList<RequirementMiss> misses, string loadedSummary, string loadErrors)
        : base($"{misses.Count} precondition(s) not met on this machine")
    {
        Misses = misses;
        LoadedSummary = loadedSummary;
        LoadErrors = loadErrors;
    }
}

/// <summary>
/// Asks a running host what it actually has loaded, and refuses the run when the content's
/// declared <c>plugin</c> requirements are not met.
/// </summary>
/// <remarks>
/// <para>
/// The failure this exists for, in full: on 2026-08-17 Grasshopper had silently not
/// registered <c>Slop.gha</c>. The fixture every cpig-kinematics test opens depends on
/// Grasshopper and Slop and nothing else, so opening it raised an "Unrecognized Objects"
/// modal that held Rhino's UI thread until the harness gave up <b>300 seconds later having
/// logged nothing at all</b>. Every fact needed to say "Slop is not loaded" was sitting in
/// the Grasshopper process the whole time; nobody asked.
/// </para>
/// <para>
/// A <c>plugin</c> requirement cannot be checked any other way. A file check on the
/// <c>.gha</c> is not a substitute: that file was present, on a scanned folder, unblocked,
/// and loadable by hand — and still did not register. <b>Presence is not loaded.</b>
/// </para>
/// </remarks>
public static class HostPreconditions
{
    /// <summary>The verb every agent answers with what it has loaded.</summary>
    public const string HostStateAction = "GetHostState";

    /// <summary>
    /// Parses the <c>loaded</c> field into the id namespace requirements are written in.
    /// </summary>
    /// <param name="loaded">Newline-delimited <c>id=detail</c> rows.</param>
    /// <returns>Map of id to detail.</returns>
    public static Dictionary<string, string> ParseLoaded(string? loaded)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(loaded)) return map;

        foreach (var line in loaded.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) { map[line.Trim()] = string.Empty; continue; }
            map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return map;
    }

    /// <summary>
    /// Compares declared plugin requirements against what the host reported.
    /// </summary>
    /// <param name="declared">Requirements with their declaring source.</param>
    /// <param name="loaded">Ids the host reported as loaded.</param>
    /// <returns>The unmet ones.</returns>
    public static IReadOnlyList<RequirementMiss> Diff(
        IEnumerable<(Requirement Requirement, string DeclaredBy)> declared,
        IReadOnlyDictionary<string, string> loaded)
    {
        var misses = new List<RequirementMiss>();
        foreach (var (req, who) in declared)
        {
            if (!string.Equals(req.Kind, Requirement.KindPlugin, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(req.Id))
            {
                misses.Add(new RequirementMiss(req, "declares kind 'plugin' with no 'id'", who));
                continue;
            }
            if (!loaded.ContainsKey(req.Id))
                misses.Add(new RequirementMiss(req, "not loaded in the running application", who));
        }
        return misses;
    }

    /// <summary>
    /// Renders the operator-facing block for an unmet-precondition abort.
    /// </summary>
    /// <param name="ex">The failure.</param>
    /// <param name="workloadName">Workload being run.</param>
    /// <param name="skippedTests">How many tests never ran.</param>
    /// <returns>Lines to print, in order.</returns>
    /// <remarks>
    /// The fix line is the product here. "gh:Slop not loaded" tells an operator what broke;
    /// the fix tells them what to do about it, which is the difference between a diagnosis
    /// and a repair.
    /// </remarks>
    public static IReadOnlyList<string> Format(
        PreconditionFailedException ex, string workloadName, int skippedTests)
    {
        var lines = new List<string>();
        foreach (var m in ex.Misses)
        {
            lines.Add($"PRECONDITION FAILED  workload={workloadName}  {m.Requirement.Describe()} — {m.Reason}");
            lines.Add($"    declared by {m.DeclaredBy}");
            if (!string.IsNullOrWhiteSpace(m.Requirement.Fix))
                lines.Add($"    fix: {m.Requirement.Fix}");
        }

        if (!string.IsNullOrWhiteSpace(ex.LoadErrors))
        {
            // A library that FAILED to load is invisible from the loaded list by
            // definition, so this is often the only place the actual reason appears.
            lines.Add("    the host reported load errors:");
            foreach (var e in ex.LoadErrors.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                lines.Add($"      {e.Trim()}");
        }

        lines.Add($"Aborted before opening anything. {skippedTests} test(s) skipped, not failed. (exit 3)");
        return lines;
    }
}
