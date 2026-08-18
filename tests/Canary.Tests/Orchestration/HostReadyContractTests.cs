using Canary.Agent;
using Canary.Config;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Orchestration;

/// <summary>
/// Deployment campaign Phase 5 — guards for the host-state contract and the plug-in
/// precondition gate.
/// </summary>
/// <remarks>
/// <para>
/// These exist because the gate was dead for its entire life and nothing noticed. The Rhino
/// agent wrote <c>data["grasshopperReady"]</c>; <c>EnsureHostPreconditionsAsync</c> read
/// <c>HostStateFields.HostReady</c> (<c>"hostReady"</c>). The field was therefore always
/// absent on Rhino, the gate took its "I cannot tell yet" early-return on every single run,
/// and <c>HostPreconditions.Diff</c> was never once reached. It logged one warning line and
/// passed every machine.
/// </para>
/// <para>
/// That is the campaign's central defect shape: <b>a guard that reports nothing wrong because
/// it never ran</b>. The unit tests below cannot load the Rhino agent — it needs Rhino — so
/// the contract half is enforced as a source-corpus guard, which is the only mechanism that
/// can see an agent and its reader disagree.
/// </para>
/// </remarks>
public class HostReadyContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Canary.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ---------------------------------------------------------------------------
    // The contract: every agent that answers GetHostState must report readiness,
    // and must do it through the shared constant.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Every agent that answers <c>GetHostState</c> assigns
    /// <see cref="HostStateFields.HostReady"/>.
    /// </summary>
    /// <remarks>
    /// This is the test that would have caught the dead gate. It is deliberately a source
    /// scan: the failure was two assemblies holding different opinions about a string, which
    /// no single-assembly test can observe, and the Rhino agent cannot be instantiated
    /// without Rhino loaded.
    /// </remarks>
    [Trait("Category", "Unit")]
    [Fact]
    public void EveryGetHostStateAgent_ReportsReadiness_ViaTheSharedConstant()
    {
        // The ASSIGNMENT form, not a mention. A bare substring check is defeated by any
        // comment naming the constant — including the one on the fix that created this test,
        // which is how that weakness was found: the mutation run stayed green.
        var assigns = new System.Text.RegularExpressions.Regex(
            @"\[\s*HostStateFields\.HostReady\s*\]\s*=",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var offenders = new List<string>();

        foreach (var file in GetHostStateSources())
        {
            if (!assigns.IsMatch(File.ReadAllText(file)))
                offenders.Add($"{Path.GetFileName(file)} answers {HostStateFields.Action} " +
                              "but never assigns [HostStateFields.HostReady] =");
        }

        Assert.True(offenders.Count == 0,
            "An agent that does not report readiness cannot be told apart from a host that has " +
            "nothing loaded, and the precondition gate disables itself rather than failing:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// No agent ASSIGNS a contract field under a hand-written key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original bug was not a missing field but a MISSPELLED one, which a presence-only
    /// check cannot see. So this matches the assignment form — <c>["field"] =</c> — rather
    /// than the bare word. The distinction is load-bearing in both directions: <c>"=loaded"</c>
    /// appears legitimately as a VALUE in the Rhino agent's row builder, and the fix's own
    /// comment quotes the old spelling to explain itself. Neither is a violation, and a
    /// substring scan calls both one.
    /// </para>
    /// <para>
    /// <c>"hostReady"</c> is included alongside the old spelling: a literal that happens to
    /// match the constant today is still one typo away from repeating this.
    /// </para>
    /// </remarks>
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("grasshopperReady")]
    [InlineData("hostReady")]
    [InlineData("loaded")]
    [InlineData("scanFolders")]
    [InlineData("discovered")]
    [InlineData("loadErrors")]
    [InlineData("framework")]
    public void NoAgent_AssignsAContractFieldUnderAHandWrittenKey(string field)
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            "\\[\\s*\"" + System.Text.RegularExpressions.Regex.Escape(field) + "\"\\s*\\]\\s*=",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var offenders = GetHostStateSources()
            .Where(f => pattern.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"[\"{field}\"] = is assigned by hand in: {string.Join(", ", offenders)}. " +
            "Contract fields go through HostStateFields so an agent/reader mismatch is a " +
            "compile error rather than an empty dictionary — which is how the plug-in " +
            "precondition gate came to never run at all.");
    }

    private static IReadOnlyList<string> GetHostStateSources()
    {
        var agentsDir = Path.Combine(RepoRoot(), "src");
        var files = Directory
            .GetDirectories(agentsDir, "Canary.Agent*")
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            // The handler, not the constants file that necessarily contains every name.
            .Where(f => !string.Equals(Path.GetFileName(f), "HostStateFields.cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("HandleGetHostState", StringComparison.Ordinal)
                     || File.ReadAllText(f).Contains("GetHostStateAsync", StringComparison.Ordinal))
            .ToList();

        // An empty corpus would make every test above vacuously green — the same
        // silent-shrink failure the campaign is built around. Assert we found the agents.
        Assert.True(files.Count >= 3,
            $"expected at least 3 GetHostState agents (rhino, penumbra, qualia); found {files.Count}. " +
            "A scan that matches nothing passes every assertion.");
        return files;
    }

    // ---------------------------------------------------------------------------
    // A check that cannot run must be as loud as a check that fails.
    // ---------------------------------------------------------------------------

    [Trait("Category", "Unit")]
    [Fact]
    public void AMessageOnlyPreconditionFailure_CarriesNoMisses_ButStillFormatsActionably()
    {
        var ex = new PreconditionFailedException(
            "the host agent answered GetHostState without a 'hostReady' field");

        Assert.Empty(ex.Misses);

        var lines = HostPreconditions.Format(ex, "rhino", skippedTests: 13);
        var text = string.Join(Environment.NewLine, lines);

        // Without the Misses-empty branch this rendered as a bare "aborted" line with no
        // reason at all — a failure the operator cannot act on.
        Assert.Contains("hostReady", text, StringComparison.Ordinal);
        Assert.Contains("rhino", text, StringComparison.Ordinal);
        Assert.Contains("13 test(s) skipped", text, StringComparison.Ordinal);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ARequirementBackedFailure_StillListsEachMiss()
    {
        var req = new Requirement { Kind = "plugin", Id = "gh:Slop", Fix = "install the Slop package" };
        var ex = new PreconditionFailedException(
            new[] { new RequirementMiss(req, "not loaded", "tests/slop-01.json") },
            loadedSummary: "gh:Kangaroo=5.0@C:/x",
            loadErrors: "Slop.gha: bad image format");

        var text = string.Join(Environment.NewLine, HostPreconditions.Format(ex, "rhino", 1));

        Assert.Contains("gh:Slop", text, StringComparison.Ordinal);
        Assert.Contains("install the Slop package", text, StringComparison.Ordinal);
        Assert.Contains("tests/slop-01.json", text, StringComparison.Ordinal);
        // A library that FAILED to load is invisible from the loaded list by definition, so
        // this is often the only place the real reason appears.
        Assert.Contains("bad image format", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // absent != false. Conflating them is what killed the gate.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// <see cref="EnvironmentReport.Format"/> distinguishes an unreported readiness from a
    /// reported <c>false</c>.
    /// </summary>
    [Trait("Category", "Unit")]
    [Fact]
    public void ReadinessFormatting_TellsAbsentApartFromFalse()
    {
        var absent = new Dictionary<string, string> { [HostStateFields.Host] = "rhino" };
        var no = new Dictionary<string, string>
        {
            [HostStateFields.Host] = "rhino",
            [HostStateFields.HostReady] = "false",
        };

        var absentText = string.Join("|", EnvironmentReport.Format(absent, EnvironmentReport.Analyse(absent)));
        var noText = string.Join("|", EnvironmentReport.Format(no, EnvironmentReport.Analyse(no)));

        Assert.NotEqual(absentText, noText);
        Assert.Contains("ready          : ?", absentText, StringComparison.Ordinal);
        Assert.Contains("ready          : false", noText, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // ParseLoaded, against the row shape the agents actually build.
    // ---------------------------------------------------------------------------

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseLoaded_SplitsIdVersionAndLocation()
    {
        var rows = string.Join(Environment.NewLine, new[]
        {
            @"gh:Slop=1.2.3@C:\Repos\Slop\bin\Release\net48\Slop.gha",
            @"rhino:CPig=loaded",
            @"js:__canaryReady=function",
        });

        var items = EnvironmentReport.ParseLoaded(rows);

        Assert.Equal(3, items.Count);
        Assert.Equal("gh:Slop", items[0].Id);
        Assert.Equal("1.2.3", items[0].Version);
        Assert.EndsWith("Slop.gha", items[0].Location, StringComparison.Ordinal);
        Assert.Equal(PluginOrigin.Developer, items[0].Origin);

        // No '@' means no location — must not swallow the version as a path.
        Assert.Equal("rhino:CPig", items[1].Id);
        Assert.Equal("loaded", items[1].Version);
        Assert.Equal(string.Empty, items[1].Location);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseLoaded_OnNothing_IsEmptyRatherThanThrowing()
    {
        Assert.Empty(EnvironmentReport.ParseLoaded(null));
        Assert.Empty(EnvironmentReport.ParseLoaded(string.Empty));
        Assert.Empty(EnvironmentReport.ParseLoaded("   "));
    }

    // ---------------------------------------------------------------------------
    // Analyse — the findings that explain a bad install.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Present on a scanned folder but unregistered — the finding that cost an afternoon.
    /// </summary>
    [Trait("Category", "Unit")]
    [Fact]
    public void Analyse_FlagsAFileThatIsPresentButNotLoaded()
    {
        var data = new Dictionary<string, string>
        {
            [HostStateFields.HostReady] = "true",
            [HostStateFields.Loaded] = @"gh:Kangaroo=5.0@C:\GH\Libraries\Kangaroo.gha",
            [HostStateFields.Discovered] = string.Join(Environment.NewLine, new[]
            {
                @"C:\GH\Libraries\Kangaroo.gha",
                @"C:\GH\Libraries\Slop.gha",
            }),
        };

        var findings = EnvironmentReport.Analyse(data);

        var miss = Assert.Single(findings, f => f.Kind == "present-but-not-loaded");
        Assert.Contains("Slop.gha", miss.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Kangaroo", miss.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The loaded path and the discovered path are compared with separators normalised.
    /// </summary>
    /// <remarks>
    /// Without this, the host's spelling and the filesystem walk's spelling differ and EVERY
    /// discovered file becomes a present-but-not-loaded finding — a false-positive storm that
    /// buries the one real finding. This already happened once during Phase 5b.
    /// </remarks>
    [Trait("Category", "Unit")]
    [Fact]
    public void Analyse_DoesNotInventFindings_WhenSeparatorsOrCaseDiffer()
    {
        var data = new Dictionary<string, string>
        {
            [HostStateFields.HostReady] = "true",
            [HostStateFields.Loaded] = "gh:Slop=1.0@C:/GH/Libraries/Slop.gha",
            [HostStateFields.Discovered] = @"c:\gh\libraries\slop.gha",
        };

        Assert.DoesNotContain(EnvironmentReport.Analyse(data), f => f.Kind == "present-but-not-loaded");
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Analyse_TreatsOneIdLoadedTwice_AsAnError()
    {
        var data = new Dictionary<string, string>
        {
            [HostStateFields.Loaded] = string.Join(Environment.NewLine, new[]
            {
                @"gh:Slop=1.0@C:\GH\Libraries\Slop.gha",
                @"gh:Slop=2.0@C:\Repos\Slop\bin\Slop.gha",
            }),
        };

        var dup = Assert.Single(EnvironmentReport.Analyse(data), f => f.Kind == "duplicate-id");
        Assert.Equal(ClashSeverity.Error, dup.Severity);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Analyse_ReportsAConfiguredFolderThatDoesNotExist()
    {
        var data = new Dictionary<string, string>
        {
            [HostStateFields.ScanFolders] = string.Join(Environment.NewLine, new[]
            {
                @"C:\GH\Libraries|OK",
                @"D:\gone|MISSING",
            }),
        };

        var dead = Assert.Single(EnvironmentReport.Analyse(data), f => f.Kind == "dead-scan-folder");
        Assert.Contains(@"D:\gone", dead.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A load failure is invisible from the loaded list by definition, so it must be surfaced
    /// from the errors channel.
    /// </summary>
    [Trait("Category", "Unit")]
    [Fact]
    public void Analyse_SurfacesLoadErrors()
    {
        var data = new Dictionary<string, string>
        {
            [HostStateFields.LoadErrors] = "Slop.gha: Could not load file or assembly",
        };

        var err = Assert.Single(EnvironmentReport.Analyse(data), f => f.Kind == "load-error");
        Assert.Contains("Slop.gha", err.Detail, StringComparison.Ordinal);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Analyse_OnACleanHost_FindsNothing()
    {
        var data = new Dictionary<string, string>
        {
            [HostStateFields.HostReady] = "true",
            [HostStateFields.Loaded] = @"gh:Kangaroo=5.0@C:\Users\x\AppData\Roaming\Grasshopper\Libraries\Kangaroo.gha",
            [HostStateFields.Discovered] = @"C:\Users\x\AppData\Roaming\Grasshopper\Libraries\Kangaroo.gha",
            [HostStateFields.ScanFolders] = @"C:\Users\x\AppData\Roaming\Grasshopper\Libraries|OK",
        };

        Assert.Empty(EnvironmentReport.Analyse(data));
    }
}
