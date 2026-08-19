namespace Canary.Orchestration;

/// <summary>Which of the campaign's three routes a machine is on.</summary>
public enum Tier
{
    /// <summary>The evidence is mixed or absent. Deliberately not a guess.</summary>
    Unknown,

    /// <summary>Has the repos and a compiler; builds from source.</summary>
    Dev,

    /// <summary>No repos, no compiler, but carries the operator's test corpus.</summary>
    Qc,

    /// <summary>No repos, no compiler, commissioning content only.</summary>
    User,
}

/// <summary>
/// Works out which tier a machine is on, from what is actually on it.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Stage C1. The campaign's central ruling is that <b>the tier belongs to
/// the MACHINE, not the artifact</b> — QC and USER install byte-identical app artifacts, and
/// what separates them is what else is present. So the tier has to be derived from evidence
/// rather than declared in a config file, or it becomes one more thing that can be stale and
/// wrong.
/// </para>
/// <para>
/// The campaign's own definitions, which this implements:
/// DEV — "has repos, builds"; QC — "no repos, no compiler" plus the operator's workloads and
/// tests; USER — "no repos, no compiler", commissioning workload only.
/// </para>
/// <para>
/// <b>Returns <see cref="Tier.Unknown"/> rather than guessing.</b> A machine with repos but no
/// SDK, or a corpus and repos but no compiler, is a state nobody designed for — and a
/// confidently wrong tier on a commissioning report is worse than an honest "cannot tell",
/// because every reader downstream would trust it. This is the same rule that makes an absent
/// <c>hostReady</c> a failure rather than a pass.
/// </para>
/// </remarks>
public static class MachineTier
{
    /// <summary>The workload name reserved for commissioning content.</summary>
    /// <remarks>
    /// A USER machine carries this and nothing else, so it is the one workload that cannot
    /// count as "the operator's corpus" when deriving a tier.
    /// </remarks>
    public const string CommissioningWorkload = "commissioning";

    /// <summary>The evidence a tier is derived from.</summary>
    /// <param name="HasRepos">At least one git checkout is present.</param>
    /// <param name="HasCompiler">A .NET SDK is installed (not merely a runtime).</param>
    /// <param name="OperatorWorkloads">Workloads present other than commissioning.</param>
    public readonly record struct Evidence(bool HasRepos, bool HasCompiler, int OperatorWorkloads);

    /// <summary>Derives the tier.</summary>
    /// <param name="evidence">What the machine has.</param>
    /// <returns>The tier, or <see cref="Tier.Unknown"/> when the evidence does not fit.</returns>
    public static Tier Derive(Evidence evidence)
    {
        // DEV is repos AND a compiler together. Repos without a compiler cannot build, and a
        // compiler without repos has nothing to build - neither is the route the campaign
        // documents, so neither is silently promoted to DEV.
        if (evidence.HasRepos && evidence.HasCompiler) return Tier.Dev;

        // Below here the machine cannot build. What separates QC from USER is only whether the
        // operator's corpus travelled with it.
        if (!evidence.HasRepos && !evidence.HasCompiler)
            return evidence.OperatorWorkloads > 0 ? Tier.Qc : Tier.User;

        return Tier.Unknown;
    }

    /// <summary>Gathers the evidence from this machine and derives the tier.</summary>
    /// <param name="workloadsDir">Workloads root, or null when none is known.</param>
    /// <param name="repoRoot">Where sibling repos would live.</param>
    /// <returns>The tier and the evidence it rests on, so a report can show its working.</returns>
    /// <remarks>
    /// Deliberately takes its roots as parameters rather than reading globals: the same
    /// derivation has to be testable against fixture directories, and a tier that can only be
    /// computed on the machine it describes cannot be unit-tested at all.
    /// </remarks>
    public static (Tier Tier, Evidence Evidence) Detect(string? workloadsDir, string repoRoot = @"C:\Repos")
    {
        var hasRepos = false;
        try
        {
            hasRepos = Directory.Exists(repoRoot)
                       && Directory.EnumerateDirectories(repoRoot)
                           .Any(d => Directory.Exists(Path.Combine(d, ".git")));
        }
        catch
        {
            // Unreadable root: treated as absent, which can only ever move the answer toward
            // Unknown or USER - never toward a more capable tier than the machine has.
        }

        var operatorWorkloads = 0;
        try
        {
            if (workloadsDir != null && Directory.Exists(workloadsDir))
            {
                operatorWorkloads = Directory.EnumerateDirectories(workloadsDir)
                    .Select(Path.GetFileName)
                    .Count(n => !string.IsNullOrEmpty(n)
                                && !string.Equals(n, CommissioningWorkload, StringComparison.OrdinalIgnoreCase)
                                && File.Exists(Path.Combine(workloadsDir, n!, "workload.json")));
            }
        }
        catch { }

        var evidence = new Evidence(hasRepos, HasDotnetSdk(), operatorWorkloads);
        return (Derive(evidence), evidence);
    }

    /// <summary>Renders a tier and its evidence for a report.</summary>
    /// <param name="tier">The derived tier.</param>
    /// <param name="evidence">What it was derived from.</param>
    /// <returns>One line naming the tier and why.</returns>
    public static string Format(Tier tier, Evidence evidence)
        => $"{tier.ToString().ToUpperInvariant()} " +
           $"(repos: {(evidence.HasRepos ? "yes" : "no")}, " +
           $"compiler: {(evidence.HasCompiler ? "yes" : "no")}, " +
           $"operator workloads: {evidence.OperatorWorkloads})";

    // A RUNTIME is not a compiler. Every machine that can run Canary has a runtime, so testing
    // for one would call every machine DEV. The SDK directory is the thing that distinguishes
    // "can build from source" from "can execute a build someone else made".
    private static bool HasDotnetSdk()
    {
        foreach (var root in new[]
                 {
                     Environment.GetEnvironmentVariable("DOTNET_ROOT"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
                 })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            try
            {
                var sdk = Path.Combine(root, "sdk");
                if (Directory.Exists(sdk) && Directory.EnumerateDirectories(sdk).Any()) return true;
            }
            catch { }
        }
        return false;
    }
}
