using Canary;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

/// <summary>
/// Deployment campaign Stage C1 — ruling 12's stamp: which machine, which build, which route.
/// </summary>
/// <remarks>
/// The campaign's central claim is that <b>the tier belongs to the machine, not the
/// artifact</b>. That only means anything if a machine can say which tier it is on, and say
/// it from evidence rather than from a config file somebody may have forgotten to update.
/// </remarks>
[Trait("Category", "Unit")]
public class MachineTierAndVersionTests
{
    // ---------------------------------------------------------------- version

    [Fact]
    public void Version_IsReadFromTheAssembly_AndIsNotAPlaceholder()
    {
        Assert.False(string.IsNullOrWhiteSpace(CanaryVersion.Version));
        // 0.0.0 is what the SDK yields with no <Version> declared - the state this replaced.
        Assert.NotEqual("0.0.0", CanaryVersion.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+", CanaryVersion.Version);
    }

    /// <summary>
    /// The stamp names the commit, so a QC finding is reproducible.
    /// </summary>
    /// <remarks>
    /// Not asserted as non-empty: a source drop with no <c>.git</c> legitimately has no commit,
    /// and the contract is that it is reported as ABSENT rather than faked. What is asserted is
    /// that when a commit exists it is a real sha and it shows up in the description.
    /// </remarks>
    [Fact]
    public void Commit_WhenPresent_IsAShaAndAppearsInTheDescription()
    {
        var commit = CanaryVersion.Commit;
        if (commit.Length == 0)
        {
            Assert.Equal(CanaryVersion.Version, CanaryVersion.Describe());
            return;
        }

        Assert.Matches("^[0-9a-fA-F]{7,40}$", commit);
        Assert.Contains(commit[..7], CanaryVersion.Describe(), StringComparison.Ordinal);
        Assert.StartsWith(CanaryVersion.Version, CanaryVersion.Informational, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------- tier

    [Theory]
    // repos AND a compiler: the documented DEV route.
    [InlineData(true, true, 5, Tier.Dev)]
    [InlineData(true, true, 0, Tier.Dev)]
    // Cannot build, but carries the operator's corpus.
    [InlineData(false, false, 5, Tier.Qc)]
    [InlineData(false, false, 1, Tier.Qc)]
    // Cannot build, commissioning content only.
    [InlineData(false, false, 0, Tier.User)]
    public void Derive_RecognisesTheThreeDocumentedRoutes(
        bool repos, bool compiler, int operatorWorkloads, Tier expected)
    {
        Assert.Equal(expected, MachineTier.Derive(new MachineTier.Evidence(repos, compiler, operatorWorkloads)));
    }

    /// <summary>
    /// Evidence that fits no route yields <see cref="Tier.Unknown"/>, never a guess.
    /// </summary>
    /// <remarks>
    /// A confidently wrong tier on a commissioning report is worse than an honest "cannot
    /// tell", because everything downstream would trust it. Repos without a compiler cannot
    /// build; a compiler without repos has nothing to build. Neither is DEV, and promoting
    /// either would overstate what the machine can do.
    /// </remarks>
    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 5)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 5)]
    public void Derive_OnEvidenceThatFitsNoRoute_IsUnknown(bool repos, bool compiler, int operatorWorkloads)
    {
        Assert.Equal(Tier.Unknown, MachineTier.Derive(new MachineTier.Evidence(repos, compiler, operatorWorkloads)));
    }

    /// <summary>The commissioning workload never counts as the operator's corpus.</summary>
    /// <remarks>
    /// It is exactly what a USER machine carries, so counting it would call every USER machine
    /// a QC one — collapsing the only distinction between those two tiers.
    /// </remarks>
    [Fact]
    public void Detect_DoesNotCountCommissioningAsOperatorContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-tier-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var w in new[] { MachineTier.CommissioningWorkload, "rhino" })
            {
                Directory.CreateDirectory(Path.Combine(root, w));
                File.WriteAllText(Path.Combine(root, w, "workload.json"), "{}");
            }
            // A directory with no workload.json is not a workload and must not be counted.
            Directory.CreateDirectory(Path.Combine(root, "not-a-workload"));

            var (_, evidence) = MachineTier.Detect(root, repoRoot: Path.Combine(root, "no-repos-here"));
            Assert.Equal(1, evidence.OperatorWorkloads);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Format_ShowsTheEvidence_NotJustTheVerdict()
    {
        var text = MachineTier.Format(Tier.Qc, new MachineTier.Evidence(false, false, 4));

        Assert.Contains("QC", text, StringComparison.Ordinal);
        Assert.Contains("repos: no", text, StringComparison.Ordinal);
        Assert.Contains("compiler: no", text, StringComparison.Ordinal);
        Assert.Contains("4", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------- the stamp itself

    [Fact]
    public void Describe_WithoutAWorkloadsRoot_CarriesTheBuildButNoTier()
    {
        var id = MachineIdentity.Describe();

        Assert.Equal(Environment.MachineName, id[MachineIdentity.MachineName]);
        Assert.True(id.ContainsKey(MachineIdentity.CanaryBuild));
        // No root means no honest way to derive a tier, so the field is absent rather than
        // guessed - the same rule as an absent commit.
        Assert.False(id.ContainsKey(MachineIdentity.MachineTierField));
    }

    [Fact]
    public void Describe_WithAWorkloadsRoot_CarriesTierAndItsEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-tier-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var id = MachineIdentity.Describe(root);

            Assert.True(id.ContainsKey(MachineIdentity.MachineTierField));
            Assert.True(id.ContainsKey(MachineIdentity.TierEvidence));
            Assert.Contains("repos:", id[MachineIdentity.TierEvidence], StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    /// <summary>A capture carries the whole stamp, because that is what makes it comparable.</summary>
    [Fact]
    public void ACapture_CarriesMachineBuildAndTier()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-tier-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var capture = EnvironmentCapture.Create(
                "rhino",
                new Dictionary<string, string>(),
                Array.Empty<EnvironmentClash>(),
                workloadsDir: root);

            Assert.Equal(Environment.MachineName, capture.Machine[MachineIdentity.MachineName]);
            Assert.True(capture.Machine.ContainsKey(MachineIdentity.CanaryBuild));
            Assert.True(capture.Machine.ContainsKey(MachineIdentity.MachineTierField));
            Assert.True(capture.IsFromThisMachine());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
