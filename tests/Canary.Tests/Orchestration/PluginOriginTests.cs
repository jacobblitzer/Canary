using Canary.Agent;
using Canary.Config;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

/// <summary>
/// Deployment campaign Phase 5b — where a library loaded from, and when that is worth saying.
/// </summary>
/// <remarks>
/// <para>
/// Origin is what makes install and update honest: Grasshopper loads from a developer-settings
/// folder as readily as from a package directory, and a dev folder SHADOWS the installed copy,
/// so both operations report success while the old code keeps running.
/// </para>
/// <para>
/// It is also where a report can drown itself. Classifying the host's OWN bundled components as
/// developer-origin produced 21 unactionable notes per run on the operator's machine, burying
/// three real warnings; and raising a note for every developer-origin library at all is noise on
/// a dev machine, where that is the normal condition. Hence the operator's ruling: judge origin
/// ONLY against a declared expectation, and mark a deviation yellow.
/// </para>
/// </remarks>
public class PluginOriginTests
{
    // ---------------------------------------------------------------------------
    // Origin classification. The signal that makes install/update honest, and the
    // noise floor that decides whether anyone can read it.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Libraries shipped inside the host application are Bundled, not Developer.
    /// </summary>
    /// <remarks>
    /// A real capture of this machine classified 21 of Rhino's own bundled component libraries
    /// as developer-origin, which buried the single row that actually mattered.
    /// </remarks>
    [Trait("Category", "Unit")]
    [Fact]
    public void Classify_CallsTheHostsOwnInstallBundled_NotDeveloper()
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.False(string.IsNullOrWhiteSpace(programs), "no ProgramFiles on this machine");

        var bundled = Path.Combine(programs, "Rhino 8", "Plug-ins", "Grasshopper", "Components", "Curve.gha");

        Assert.Equal(PluginOrigin.Bundled, PluginOrigins.Classify(bundled));
        // Deployed: an origin: "deployed" pin must not reject the application's own components.
        Assert.True(PluginOrigins.IsDeployed(PluginOrigin.Bundled));
    }

    /// <summary>
    /// A build output or Drive payload folder stays Developer — the shadowing signal survives.
    /// </summary>
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData(@"C:\Repos\Slop\bin\Release\net48\Slop.gha")]
    [InlineData(@"G:\My Drive\GrasshopperPlugins\Slop.gha")]
    [InlineData(@"C:\Users\x\Desktop\Slop.gha")]
    public void Classify_StillCallsABuildOutputOrHandAddedFolderDeveloper(string location)
    {
        Assert.Equal(PluginOrigin.Developer, PluginOrigins.Classify(location));
        Assert.False(PluginOrigins.IsDeployed(PluginOrigin.Developer));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Classify_StillRecognisesPackageAndLibrariesInstalls()
    {
        Assert.Equal(PluginOrigin.Package, PluginOrigins.Classify(
            @"C:\Users\x\AppData\Roaming\McNeel\Rhinoceros\packages\8.0\Slop\1.2.3\Slop.gha"));
        Assert.Equal(PluginOrigin.Libraries, PluginOrigins.Classify(
            @"C:\Users\x\AppData\Roaming\Grasshopper\Libraries\Slop.gha"));
    }

    // ---------------------------------------------------------------------------
    // Origin is judged ONLY against a declared expectation.
    // Operator ruling 2026-08-18: "dont worry about the developer origin rows..
    // mark as 'yellow' if they deviate from whats expected."
    // ---------------------------------------------------------------------------

    private static Dictionary<string, string> ThreeLoaded()
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return new Dictionary<string, string>
        {
            [HostStateFields.HostReady] = "true",
            [HostStateFields.Loaded] = string.Join(Environment.NewLine, new[]
            {
                $"gh:Curve Components=8.34@{Path.Combine(programs, "Rhino 8", "Plug-ins", "Grasshopper", "Components", "Curve.gha")}",
                @"gh:Slop=1.0@C:\Repos\Slop\bin\Release\net48\Slop.gha",
                @"gh:Kangaroo=5.0@C:\Users\x\AppData\Roaming\Grasshopper\Libraries\Kangaroo.gha",
            }),
        };
    }

    /// <summary>
    /// With nothing declared, origin raises no findings at all.
    /// </summary>
    /// <remarks>
    /// The previous behaviour raised a Note per developer-origin library. On a development
    /// machine that is the normal condition — seven notes about the operator's own repos, every
    /// run, none of them actionable. A report is only read if everything in it means something.
    /// The origin of every library is still reported; it is a column in the loaded list.
    /// </remarks>
    [Trait("Category", "Unit")]
    [Fact]
    public void Analyse_WithNoDeclaredExpectation_SaysNothingAboutOrigin()
    {
        var findings = EnvironmentReport.Analyse(ThreeLoaded());

        Assert.DoesNotContain(findings, f => f.Kind == "developer-origin");
        Assert.DoesNotContain(findings, f => f.Kind == "origin-deviates");
    }

    /// <summary>A declared origin the machine does not honour is yellow.</summary>
    [Trait("Category", "Unit")]
    [Fact]
    public void Analyse_WhenOriginDeviatesFromWhatWasDeclared_IsAWarning()
    {
        var expected = new Dictionary<string, string> { ["gh:Slop"] = "deployed" };

        var findings = EnvironmentReport.Analyse(ThreeLoaded(), expected);

        var deviation = Assert.Single(findings, f => f.Kind == "origin-deviates");
        Assert.Equal(ClashSeverity.Warning, deviation.Severity);
        Assert.Contains("gh:Slop", deviation.Detail, StringComparison.Ordinal);
        Assert.Contains("deployed", deviation.Detail, StringComparison.Ordinal);
        Assert.Contains("developer", deviation.Detail, StringComparison.Ordinal);
    }

    /// <summary>A declared origin the machine does honour raises nothing.</summary>
    [Trait("Category", "Unit")]
    [Fact]
    public void Analyse_WhenOriginMatchesWhatWasDeclared_IsSilent()
    {
        var expected = new Dictionary<string, string>
        {
            ["gh:Kangaroo"] = "deployed",
            // The host's own bundled components count as deployed — an origin pin must not
            // reject the application's own libraries.
            ["gh:Curve Components"] = "deployed",
        };

        var findings = EnvironmentReport.Analyse(ThreeLoaded(), expected);

        Assert.DoesNotContain(findings, f => f.Kind == "origin-deviates");
    }

    /// <summary>An expectation for something not loaded is not an origin finding.</summary>
    /// <remarks>
    /// That case is "not loaded", which the precondition gate reports. Reporting it here as an
    /// origin deviation as well would double-count one fault under two names.
    /// </remarks>
    [Trait("Category", "Unit")]
    [Fact]
    public void Analyse_DoesNotReportOriginForSomethingThatIsNotLoaded()
    {
        var expected = new Dictionary<string, string> { ["gh:NotHere"] = "package" };

        Assert.DoesNotContain(EnvironmentReport.Analyse(ThreeLoaded(), expected),
            f => f.Kind == "origin-deviates");
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData(null, PluginOrigin.Developer, true)]
    [InlineData("", PluginOrigin.Developer, true)]
    [InlineData("any", PluginOrigin.Developer, true)]
    [InlineData("deployed", PluginOrigin.Developer, false)]
    [InlineData("deployed", PluginOrigin.Package, true)]
    [InlineData("deployed", PluginOrigin.Libraries, true)]
    [InlineData("deployed", PluginOrigin.Bundled, true)]
    [InlineData("package", PluginOrigin.Libraries, false)]
    [InlineData("libraries", PluginOrigin.Package, false)]
    // An unrecognised pin passes: a typo in content must not fail every machine. doctor is
    // where a bad declaration is reported, and a gate that rejects what it cannot parse
    // produces false reds, which block healthy installs.
    [InlineData("depolyed", PluginOrigin.Developer, true)]
    public void Satisfies_JudgesAPinAgainstAnActualOrigin(string? pin, PluginOrigin actual, bool expected)
    {
        Assert.Equal(expected, PluginOrigins.Satisfies(pin, actual));
    }

    /// <summary>
    /// Only pins that express an expectation become expectations.
    /// </summary>
    [Trait("Category", "Unit")]
    [Fact]
    public void ExpectedOrigins_OmitsUnpinnedAndAnyAndNonPlugins()
    {
        var declared = new (Requirement, string)[]
        {
            (new Requirement { Kind = "plugin", Id = "gh:Pinned", Origin = "deployed" }, "w"),
            (new Requirement { Kind = "plugin", Id = "gh:Unpinned" }, "w"),
            (new Requirement { Kind = "plugin", Id = "gh:Any", Origin = "any" }, "w"),
            (new Requirement { Kind = "file", Path = "x", Origin = "deployed" }, "w"),
        };

        var map = RequirementChecker.ExpectedOrigins(declared);

        var only = Assert.Single(map);
        Assert.Equal("gh:Pinned", only.Key);
        Assert.Equal("deployed", only.Value);
    }
}
