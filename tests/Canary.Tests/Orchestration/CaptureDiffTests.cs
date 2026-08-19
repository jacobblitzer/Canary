using Canary.Agent;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

/// <summary>
/// Deployment campaign Stage A2 — comparing two machines' captures.
/// </summary>
/// <remarks>
/// "Did this install correctly" reduces, in practice, to diffing a capture from a known-good
/// machine against one from the target. Until this existed that meant reading two JSON
/// documents side by side while holding 96 loaded libraries in your head — a comparison
/// performed by eye, on the machine where it matters least to get it wrong.
/// </remarks>
[Trait("Category", "Unit")]
public class CaptureDiffTests
{
    private static EnvironmentCapture Cap(string machine, string loaded, string? folders = null,
                                          string host = "rhino", string hostVersion = "8.34")
        => new()
        {
            CapturedUtc = "2026-08-18T12:00:00Z",
            Workload = "rhino",
            Machine = new Dictionary<string, string> { [MachineIdentity.MachineName] = machine },
            Host = new Dictionary<string, string>
            {
                [HostStateFields.Host] = host,
                [HostStateFields.HostVersion] = hostVersion,
                [HostStateFields.Loaded] = loaded,
                [HostStateFields.ScanFolders] = folders ?? string.Empty,
            },
        };

    private const string Deployed = @"gh:Slop=1.0@C:\Users\x\AppData\Roaming\Grasshopper\Libraries\Slop.gha";
    private const string DevBuild = @"gh:Slop=1.0@C:\Repos\Slop\bin\Release\net48\Slop.gha";

    [Fact]
    public void TwoIdenticalCaptures_DifferInNothing()
    {
        var a = Cap("A", Deployed);
        var b = Cap("B", Deployed);
        Assert.Empty(a.DiffAgainst(b));
    }

    [Fact]
    public void ALibraryPresentOnOnlyOneMachine_IsReportedFromBothSides()
    {
        var mine = Cap("HERE", Deployed + "\n" + @"gh:CPig=2.0@C:\p\CPig.gha");
        var theirs = Cap("THERE", Deployed);

        var here = mine.DiffAgainst(theirs, "HERE", "THERE");
        Assert.Contains(here, d => d.Kind == "only-here" && d.Detail.Contains("gh:CPig"));

        // Reversing the comparison must reverse the direction, not lose the finding.
        var there = theirs.DiffAgainst(mine, "THERE", "HERE");
        Assert.Contains(there, d => d.Kind == "only-there" && d.Detail.Contains("gh:CPig"));
    }

    /// <summary>
    /// The same library from a different KIND of location is the headline QC failure.
    /// </summary>
    /// <remarks>
    /// A version match hides it: identical id, identical version, and one machine is running
    /// somebody's build output while the other runs the shipped package. That is a different
    /// install, not a different build, so it must outrank version skew in the output.
    /// </remarks>
    [Fact]
    public void SameLibraryFromADifferentOrigin_IsReported_AndOutranksVersion()
    {
        var mine = Cap("HERE", DevBuild + "\n" + @"gh:CPig=2.0@C:\Users\x\AppData\Roaming\Grasshopper\Libraries\CPig.gha");
        var theirs = Cap("THERE", Deployed + "\n" + @"gh:CPig=9.9@C:\Users\x\AppData\Roaming\Grasshopper\Libraries\CPig.gha");

        var diffs = mine.DiffAgainst(theirs, "HERE", "THERE");

        var origin = Assert.Single(diffs, d => d.Kind == "origin");
        Assert.Contains("gh:Slop", origin.Detail, StringComparison.Ordinal);
        Assert.Contains("developer", origin.Detail, StringComparison.Ordinal);
        Assert.Contains("libraries", origin.Detail, StringComparison.Ordinal);

        var version = Assert.Single(diffs, d => d.Kind == "version");
        Assert.Contains("gh:CPig", version.Detail, StringComparison.Ordinal);

        var order = diffs.ToList();
        Assert.True(order.IndexOf(origin) < order.IndexOf(version),
            "origin differences must be listed before version skew - a different install " +
            "outranks a different build.");
    }

    [Fact]
    public void AHostVersionDifference_IsReported()
    {
        var diffs = Cap("HERE", Deployed, hostVersion: "8.34")
            .DiffAgainst(Cap("THERE", Deployed, hostVersion: "8.20"), "HERE", "THERE");

        var host = Assert.Single(diffs, d => d.Kind == "host");
        Assert.Contains("8.34", host.Detail, StringComparison.Ordinal);
        Assert.Contains("8.20", host.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scan folder on only one machine explains findings that otherwise look inexplicable.
    /// </summary>
    [Fact]
    public void AScanFolderOnOnlyOneMachine_IsReported()
    {
        var mine = Cap("HERE", Deployed, folders: @"C:\Repos\Slop\bin|OK" + "\n" + @"C:\GH\Libraries|OK");
        var theirs = Cap("THERE", Deployed, folders: @"C:\GH\Libraries|OK");

        var diffs = mine.DiffAgainst(theirs, "HERE", "THERE");

        var folder = Assert.Single(diffs, d => d.Kind == "scan-folder");
        Assert.Contains(@"C:\Repos\Slop\bin", folder.Detail, StringComparison.Ordinal);
        Assert.Contains("HERE", folder.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ordering puts "missing on the target" first, because that is what fails an install.
    /// </summary>
    [Fact]
    public void OnlyThere_LeadsTheOutput()
    {
        var mine = Cap("HERE", DevBuild);
        var theirs = Cap("THERE", Deployed + "\n" + @"gh:Kangaroo=5.0@C:\GH\Libraries\Kangaroo.gha",
                         hostVersion: "8.20");

        var diffs = mine.DiffAgainst(theirs, "HERE", "THERE");

        Assert.Equal("only-there", diffs[0].Kind);
        Assert.Contains("gh:Kangaroo", diffs[0].Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Comparing a capture with itself finds nothing — the diff must not invent differences.
    /// </summary>
    [Fact]
    public void ACaptureComparedWithItself_IsClean()
    {
        var c = Cap("SAME", Deployed + "\n" + @"gh:CPig=2.0@C:\GH\Libraries\CPig.gha",
                    folders: @"C:\GH\Libraries|OK");
        Assert.Empty(c.DiffAgainst(c, "SAME", "SAME"));
    }
}
