using System.Reflection;

namespace Canary;

/// <summary>
/// What build of Canary this is — the single source, read from the assembly.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Stage C1, ruling 12: a commissioning report is stamped with machine
/// id, Canary version and tier. Before this, Canary could not report its own version at all —
/// no <c>&lt;Version&gt;</c> in any project, nothing in the CLI, nothing in a capture. A QC
/// report that cannot say which build produced it has the same defect that made environment
/// captures undiffable before <see cref="Orchestration.MachineIdentity"/> existed: the
/// evidence is real but unattributable.
/// </para>
/// <para>
/// <b>Read from the assembly, not from a constant.</b> A hand-maintained version string is a
/// second source of truth that drifts from the build the moment someone forgets to bump it,
/// and it cannot know the commit at all. <c>Directory.Build.props</c> declares the version;
/// the SDK stamps <see cref="AssemblyInformationalVersionAttribute"/> as
/// <c>0.9.0+&lt;full git sha&gt;</c>; this reads it back. So the stamp on a report names the
/// exact commit that produced the binary, which is what makes a QC finding reproducible.
/// </para>
/// </remarks>
public static class CanaryVersion
{
    private static readonly Lazy<(string Version, string Commit, string Informational)> Resolved =
        new(Resolve, isThreadSafe: true);

    /// <summary>The declared version, e.g. <c>0.9.0</c>.</summary>
    public static string Version => Resolved.Value.Version;

    /// <summary>
    /// The commit the binary was built from, or empty when the build had no git metadata.
    /// </summary>
    /// <remarks>
    /// Empty is honest and expected: a source drop with no <c>.git</c> produces a version with
    /// no commit. It is reported as absent rather than faked, because a report claiming a
    /// commit it cannot know is worse than one admitting it does not.
    /// </remarks>
    public static string Commit => Resolved.Value.Commit;

    /// <summary>The raw informational version, <c>version+commit</c> when git metadata exists.</summary>
    public static string Informational => Resolved.Value.Informational;

    /// <summary>Short, human-facing: <c>0.9.0 (a1b2c3d)</c>, or just the version.</summary>
    /// <returns>One line, never empty.</returns>
    public static string Describe()
    {
        var c = Commit;
        return c.Length >= 7 ? $"{Version} ({c[..7]})" : Version;
    }

    private static (string, string, string) Resolve()
    {
        // This assembly, not the entry assembly: Canary.Core is what every consumer shares,
        // and the entry point differs between canary.exe, Canary.UI.exe and the test host.
        var asm = typeof(CanaryVersion).Assembly;

        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? string.Empty;

        if (informational.Length == 0)
        {
            var fallback = asm.GetName().Version?.ToString() ?? "0.0.0";
            return (fallback, string.Empty, fallback);
        }

        // "0.9.0+<sha>" — split on the FIRST '+', because SemVer build metadata may itself
        // contain '+' in principle and the version half never does.
        var plus = informational.IndexOf('+');
        return plus < 0
            ? (informational, string.Empty, informational)
            : (informational[..plus], informational[(plus + 1)..], informational);
    }
}
