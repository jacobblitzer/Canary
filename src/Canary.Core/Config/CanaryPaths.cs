namespace Canary.Config;

/// <summary>
/// Single resolver for the workloads content root.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 1. Before this existed, seven CLI sites each did
/// <c>Path.Combine(Directory.GetCurrentDirectory(), "workloads")</c> and the UI had a
/// separate candidate list ending in a hard-coded <c>C:\Repos\Canary\workloads</c>.
/// Two consequences: <c>canary</c> could only be run from the repo root, and on a
/// machine that happened to have any Canary checkout the UI would silently bind to it
/// rather than to the installed content.
/// </para>
/// <para>
/// Resolution order, and why:
/// <list type="number">
/// <item>an explicit path (the <c>--workloads-dir</c> flag) — the caller said so;</item>
/// <item><c>CANARY_WORKLOADS_DIR</c> — how an installer or a test rig points at content
///   it did not ship with. Already honoured by the MCP server, which is where this
///   logic was promoted from;</item>
/// <item><c>&lt;cwd&gt;/workloads</c> — <b>preserves the previous CLI behaviour exactly</b>,
///   so running from a repo root keeps resolving as it always did;</item>
/// <item>walking up from the executable — finds the repo tree when the exe is run from
///   <c>bin/Release/...</c>, which is what the UI relied on;</item>
/// <item><c>&lt;exe&gt;/workloads</c> — the shape an installed layout will have.</item>
/// </list>
/// </para>
/// <para>
/// A resolution is only accepted if the directory <b>exists</b>, except for the final
/// fallback, which is returned so the caller can produce a useful "not found" naming a
/// real path instead of failing on an empty string.
/// </para>
/// </remarks>
public static class CanaryPaths
{
    /// <summary>Environment variable that overrides workloads-root discovery.</summary>
    public const string WorkloadsDirEnvVar = "CANARY_WORKLOADS_DIR";

    /// <summary>How a workloads root was resolved. Reported by diagnostics.</summary>
    public enum WorkloadsSource
    {
        /// <summary>An explicit path was supplied by the caller.</summary>
        Explicit,
        /// <summary>Taken from <see cref="WorkloadsDirEnvVar"/>.</summary>
        Environment,
        /// <summary>Found under the current working directory.</summary>
        CurrentDirectory,
        /// <summary>Found by walking up from the executable's directory.</summary>
        ExecutableWalkUp,
        /// <summary>Nothing existed; the executable-relative path is returned as a guess.</summary>
        FallbackNotFound,
    }

    /// <summary>The resolved workloads root and the rule that produced it.</summary>
    /// <param name="Path">Absolute path to the workloads root.</param>
    /// <param name="Source">Which rule matched.</param>
    /// <param name="Exists">Whether <paramref name="Path"/> is a directory that exists.</param>
    public readonly record struct WorkloadsResolution(string Path, WorkloadsSource Source, bool Exists);

    /// <summary>
    /// Resolves the workloads content root.
    /// </summary>
    /// <param name="explicitDir">Optional caller-supplied path; wins over everything.</param>
    /// <returns>An absolute path. Callers that require the directory to exist should use
    /// <see cref="ResolveWorkloadsRootDetailed"/> and check <c>Exists</c>.</returns>
    public static string ResolveWorkloadsRoot(string? explicitDir = null)
        => ResolveWorkloadsRootDetailed(explicitDir).Path;

    /// <summary>
    /// Resolves the workloads content root, reporting which rule matched.
    /// </summary>
    /// <param name="explicitDir">Optional caller-supplied path; wins over everything.</param>
    /// <returns>The resolution, including whether the path exists.</returns>
    public static WorkloadsResolution ResolveWorkloadsRootDetailed(string? explicitDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            var full = System.IO.Path.GetFullPath(explicitDir);
            return new WorkloadsResolution(full, WorkloadsSource.Explicit, Directory.Exists(full));
        }

        var env = Environment.GetEnvironmentVariable(WorkloadsDirEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            var full = System.IO.Path.GetFullPath(env);
            // Reported even when absent: a set-but-wrong override is a configuration
            // error worth naming, not something to silently fall through.
            return new WorkloadsResolution(full, WorkloadsSource.Environment, Directory.Exists(full));
        }

        var cwd = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "workloads"));
        if (Directory.Exists(cwd))
            return new WorkloadsResolution(cwd, WorkloadsSource.CurrentDirectory, true);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "workloads");
            if (Directory.Exists(candidate))
                return new WorkloadsResolution(candidate, WorkloadsSource.ExecutableWalkUp, true);
            dir = dir.Parent;
        }

        var fallback = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, "workloads"));
        return new WorkloadsResolution(fallback, WorkloadsSource.FallbackNotFound, Directory.Exists(fallback));
    }

    /// <summary>
    /// Expands <c>%NAME%</c> tokens in test content.
    /// </summary>
    /// <param name="value">Raw text from a test definition.</param>
    /// <returns>The text with known environment variables substituted.</returns>
    /// <remarks>
    /// <para>
    /// The single seam through which test content becomes machine-specific. Phase 2 of
    /// the deployment campaign retires absolute path literals by replacing them with
    /// tokens; this is where those tokens resolve, so there is one behaviour to reason
    /// about rather than one per call site.
    /// </para>
    /// <para>
    /// Substitution happens <b>anywhere in the string</b>, not just at the start - that
    /// is what lets a Rhino macro or a Grasshopper panel value carry a token mid-text.
    /// An <b>unknown</b> token is left exactly as written, so ordinary text containing
    /// percent signs passes through unharmed and a mistyped token is visible in the
    /// failure rather than silently becoming an empty string.
    /// </para>
    /// </remarks>
    public static string Expand(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : Environment.ExpandEnvironmentVariables(value);

    /// <summary>
    /// Human-readable explanation of a resolution, for diagnostics and error messages.
    /// </summary>
    /// <param name="r">The resolution to describe.</param>
    /// <returns>A single line naming the path and the rule that produced it.</returns>
    public static string Describe(WorkloadsResolution r) => r.Source switch
    {
        WorkloadsSource.Explicit => $"{r.Path} (--workloads-dir)",
        WorkloadsSource.Environment => $"{r.Path} ({WorkloadsDirEnvVar})",
        WorkloadsSource.CurrentDirectory => $"{r.Path} (current directory)",
        WorkloadsSource.ExecutableWalkUp => $"{r.Path} (found above {AppContext.BaseDirectory})",
        _ => $"{r.Path} (no workloads directory found)",
    };
}
