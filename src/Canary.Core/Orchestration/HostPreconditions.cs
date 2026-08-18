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

    /// <summary>
    /// Constructs a failure that is not about any individual requirement — the check itself
    /// could not be performed.
    /// </summary>
    /// <param name="message">What made the check impossible.</param>
    /// <remarks>
    /// A check that cannot run must be as loud as a check that fails. The alternative, which
    /// this codebase actually shipped, is a guard that logs one warning line and passes
    /// everything: the Rhino agent never emitted the readiness field the gate reads, so the
    /// gate excused itself on every run. <see cref="Misses"/> is empty here because there is
    /// nothing machine-specific to list — the fault is in the agent, not the machine.
    /// </remarks>
    public PreconditionFailedException(string message)
        : base(message)
    {
        Misses = Array.Empty<RequirementMiss>();
        LoadedSummary = string.Empty;
        LoadErrors = string.Empty;
    }
}

/// <summary>Where a loaded plug-in actually came from.</summary>
/// <remarks>
/// This distinction is what makes "install" and "update" honest. Grasshopper loads from
/// developer-settings folders as happily as from a package directory, and a dev folder
/// SHADOWS the installed copy: you install or update the yak package, Grasshopper keeps
/// loading the build output, and the operation appears to have worked while the old code is
/// still running. Nothing in the install reports that, because from the installer's point of
/// view it succeeded.
/// </remarks>
public enum PluginOrigin
{
    /// <summary>Could not be determined from the reported location.</summary>
    Unknown,

    /// <summary>A yak package directory — the deployed, versioned install.</summary>
    Package,

    /// <summary>Grasshopper's Libraries folder — a manual but conventional install.</summary>
    Libraries,

    /// <summary>
    /// A build output or hand-added developer folder. Expected on DEV; on QC or USER it means
    /// the deployed package is NOT what is running.
    /// </summary>
    Developer,

    /// <summary>
    /// Shipped inside the host application's own installation, under a machine-wide program
    /// directory — Rhino's bundled Grasshopper components, for instance.
    /// </summary>
    /// <remarks>
    /// Deployed, and never the operator's doing. Without this case the <c>Developer</c> default
    /// swallowed all of it: a real capture of this machine classified <b>21</b> of Rhino's own
    /// bundled component libraries as developer-origin, producing 21 notes that buried the one
    /// row that mattered. A report nobody can read is not observability, and the same
    /// false-positive storm already cost this campaign one pass over the present-but-not-loaded
    /// class.
    /// </remarks>
    Bundled,
}

/// <summary>Classifies where a plug-in loaded from.</summary>
public static class PluginOrigins
{
    /// <summary>Classifies a reported location.</summary>
    /// <param name="location">Full path the host reported.</param>
    /// <returns>The origin.</returns>
    public static PluginOrigin Classify(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return PluginOrigin.Unknown;
        // Normalised through Path.DirectorySeparatorChar so this file contains no escaped
        // separators at all - the paths being matched are full of them and every hand-written
        // escape here is a chance to get one wrong silently.
        var p = location.Replace(Path.DirectorySeparatorChar, '/');

        // Yak installs land under <roaming>/McNeel/Rhinoceros/packages/<rhino>/<name>/<ver>/
        if (p.IndexOf("/McNeel/Rhinoceros/packages/", StringComparison.OrdinalIgnoreCase) >= 0)
            return PluginOrigin.Package;

        if (p.IndexOf("/Grasshopper/Libraries", StringComparison.OrdinalIgnoreCase) >= 0)
            return PluginOrigin.Libraries;

        // The host's own installation. Narrow on purpose: a machine-wide program directory is
        // not somewhere the operator builds to, so this cannot absorb a shadowing developer
        // folder under C:/Repos or a Drive-synced payload folder - those stay Developer below.
        foreach (var programs in ProgramRoots())
        {
            if (p.StartsWith(programs, StringComparison.OrdinalIgnoreCase))
                return PluginOrigin.Bundled;
        }

        // Everything else is a build output or a hand-added developer folder. Deliberately the
        // DEFAULT rather than a pattern match on bin/ or Repos/ - an unrecognised location is
        // not a deployed one, and assuming otherwise is exactly how a shadowed install passes.
        return PluginOrigin.Developer;
    }

    // Empty entries are dropped: a zero-length prefix makes StartsWith true for EVERY path,
    // which would silently classify the whole machine as Bundled and disable the shadowing
    // signal entirely.
    private static IEnumerable<string> ProgramRoots()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var path = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(path)) continue;
            yield return path.Replace(Path.DirectorySeparatorChar, '/').TrimEnd('/') + "/";
        }
    }

    /// <summary>True when this origin is a deployed install rather than a build output.</summary>
    /// <param name="origin">Origin to test.</param>
    /// <returns>True for package, Libraries, and host-bundled installs.</returns>
    /// <remarks>
    /// <see cref="PluginOrigin.Bundled"/> counts as deployed: a library shipped inside the host
    /// application is as installed as anything can be, so an <c>origin: "deployed"</c> pin must
    /// accept it rather than reporting the application's own components as unsatisfied.
    /// </remarks>
    public static bool IsDeployed(PluginOrigin origin)
        => origin is PluginOrigin.Package or PluginOrigin.Libraries or PluginOrigin.Bundled;

    /// <summary>Whether an actual origin satisfies a declared <c>origin</c> pin.</summary>
    /// <param name="pin">The requirement's pin: package / libraries / deployed / any.</param>
    /// <param name="actual">Where the library actually loaded from.</param>
    /// <returns>True when the pin is absent, <c>any</c>, unrecognised, or satisfied.</returns>
    /// <remarks>
    /// <para>
    /// Extracted so the run-path gate and the environment report cannot form different opinions
    /// about the same pin. They had a copy each for about an hour, which is exactly how bug 0022
    /// started.
    /// </para>
    /// <para>
    /// An UNRECOGNISED pin returns true. A typo in content must not silently fail every machine
    /// — <c>canary doctor</c> is where a bad declaration is reported, and a gate that rejects
    /// what it cannot parse produces false reds, which block healthy installs.
    /// </para>
    /// </remarks>
    public static bool Satisfies(string? pin, PluginOrigin actual)
        => (pin ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "any" => true,
            "package" => actual == PluginOrigin.Package,
            "libraries" => actual == PluginOrigin.Libraries,
            "deployed" => IsDeployed(actual),
            _ => true,
        };
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
            if (!loaded.TryGetValue(req.Id, out var detail))
            {
                misses.Add(new RequirementMiss(req, "not loaded in the running application", who));
                continue;
            }

            // Loaded, but from where? An install or update that a developer folder shadows
            // reports success while the old code keeps running, so a pinned origin is the
            // only way that becomes visible.
            if (string.IsNullOrWhiteSpace(req.Origin)
                || string.Equals(req.Origin, "any", StringComparison.OrdinalIgnoreCase))
                continue;

            var at = detail.LastIndexOf('@');
            var location = at >= 0 ? detail.Substring(at + 1) : detail;
            var origin = PluginOrigins.Classify(location);
            if (!PluginOrigins.Satisfies(req.Origin, origin))
            {
                misses.Add(new RequirementMiss(req,
                    $"loaded from a {origin.ToString().ToLowerInvariant()} location, not '{req.Origin}' — " +
                    $"at {location}. An install or update here would appear to succeed while this copy keeps running",
                    who));
            }
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

        // No per-requirement rows means the check could not be performed at all (an agent
        // that does not implement the contract). Print the reason, or this renders as a bare
        // "aborted" with nothing to act on.
        if (ex.Misses.Count == 0)
            lines.Add($"PRECONDITION CHECK FAILED  workload={workloadName}  {ex.Message}");

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
