using Canary.Config;
using Xunit;

namespace Canary.Tests.Config;

// Deployment campaign Phase 1. Before CanaryPaths, seven CLI sites each did
// Path.Combine(Directory.GetCurrentDirectory(), "workloads") and the UI kept its own
// candidate list ending in a hard-coded C:\Repos\Canary\workloads — and a re-verification
// pass found that NOTHING in tests/ exercised discovery at all. These tests exist so the
// resolution ORDER is pinned: each rule is proven to win over the one below it, because
// a precedence regression is silent (the wrong tree resolves, nothing errors).
//
// Every test restores cwd and the environment variable in a finally block: leaking either
// would corrupt unrelated tests in the same assembly.
public class CanaryPathsTests
{
    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "canarypaths-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(d);
        return d;
    }

    private static T Isolated<T>(Func<T> body)
    {
        var cwd = Directory.GetCurrentDirectory();
        var env = Environment.GetEnvironmentVariable(CanaryPaths.WorkloadsDirEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(CanaryPaths.WorkloadsDirEnvVar, null);
            return body();
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
            Environment.SetEnvironmentVariable(CanaryPaths.WorkloadsDirEnvVar, env);
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ExplicitPath_Wins_OverEverything()
    {
        var root = NewTempDir();
        var explicitDir = Path.Combine(root, "explicit");
        var envDir = Path.Combine(root, "env");
        var cwdDir = Path.Combine(root, "cwd");
        Directory.CreateDirectory(explicitDir);
        Directory.CreateDirectory(envDir);
        Directory.CreateDirectory(Path.Combine(cwdDir, "workloads"));

        Isolated(() =>
        {
            Environment.SetEnvironmentVariable(CanaryPaths.WorkloadsDirEnvVar, envDir);
            Directory.SetCurrentDirectory(cwdDir);

            var r = CanaryPaths.ResolveWorkloadsRootDetailed(explicitDir);

            Assert.Equal(CanaryPaths.WorkloadsSource.Explicit, r.Source);
            Assert.Equal(Path.GetFullPath(explicitDir), r.Path);
            Assert.True(r.Exists);
            return 0;
        });
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void EnvironmentVariable_Wins_OverCurrentDirectory()
    {
        var root = NewTempDir();
        var envDir = Path.Combine(root, "env");
        var cwdDir = Path.Combine(root, "cwd");
        Directory.CreateDirectory(envDir);
        Directory.CreateDirectory(Path.Combine(cwdDir, "workloads"));

        Isolated(() =>
        {
            Environment.SetEnvironmentVariable(CanaryPaths.WorkloadsDirEnvVar, envDir);
            Directory.SetCurrentDirectory(cwdDir);

            var r = CanaryPaths.ResolveWorkloadsRootDetailed();

            Assert.Equal(CanaryPaths.WorkloadsSource.Environment, r.Source);
            Assert.Equal(Path.GetFullPath(envDir), r.Path);
            return 0;
        });
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void CurrentDirectory_Used_WhenNoOverride()
    {
        var cwdDir = NewTempDir();
        var expected = Path.Combine(cwdDir, "workloads");
        Directory.CreateDirectory(expected);

        Isolated(() =>
        {
            Directory.SetCurrentDirectory(cwdDir);

            var r = CanaryPaths.ResolveWorkloadsRootDetailed();

            Assert.Equal(CanaryPaths.WorkloadsSource.CurrentDirectory, r.Source);
            Assert.Equal(Path.GetFullPath(expected), r.Path);
            Assert.True(r.Exists);
            return 0;
        });
    }

    // The regression this whole class exists for: a set-but-wrong override must be
    // REPORTED, not silently skipped. Falling through would bind the caller to some
    // other tree while the operator believes the override took effect.
    [Trait("Category", "Unit")]
    [Fact]
    public void MissingExplicitPath_IsReported_NotSilentlySkipped()
    {
        var cwdDir = NewTempDir();
        Directory.CreateDirectory(Path.Combine(cwdDir, "workloads"));
        var bogus = Path.Combine(NewTempDir(), "does-not-exist");

        Isolated(() =>
        {
            Directory.SetCurrentDirectory(cwdDir);

            var r = CanaryPaths.ResolveWorkloadsRootDetailed(bogus);

            Assert.Equal(CanaryPaths.WorkloadsSource.Explicit, r.Source);
            Assert.False(r.Exists);
            Assert.Equal(Path.GetFullPath(bogus), r.Path);
            return 0;
        });
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void MissingEnvironmentPath_IsReported_NotSilentlySkipped()
    {
        var cwdDir = NewTempDir();
        Directory.CreateDirectory(Path.Combine(cwdDir, "workloads"));
        var bogus = Path.Combine(NewTempDir(), "does-not-exist");

        Isolated(() =>
        {
            Environment.SetEnvironmentVariable(CanaryPaths.WorkloadsDirEnvVar, bogus);
            Directory.SetCurrentDirectory(cwdDir);

            var r = CanaryPaths.ResolveWorkloadsRootDetailed();

            Assert.Equal(CanaryPaths.WorkloadsSource.Environment, r.Source);
            Assert.False(r.Exists);
            return 0;
        });
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Describe_NamesThePathAndTheRule()
    {
        var r = new CanaryPaths.WorkloadsResolution(@"C:\x\workloads", CanaryPaths.WorkloadsSource.Explicit, true);
        var text = CanaryPaths.Describe(r);

        Assert.Contains(@"C:\x\workloads", text);
        Assert.Contains("--workloads-dir", text);
    }

    // Guards the literal deleted in Phase 1. Note what this does NOT assert: resolving
    // to C:\Repos\Canary\workloads is perfectly correct when the executable lives inside
    // that repo - the walk-up earned it. The defect was a path arriving from a rule the
    // caller could not see or override. So the invariant is on the SOURCE: with no
    // explicit path and no environment variable, every resolution must be attributable
    // to cwd, the walk-up, or the honest not-found fallback.
    [Trait("Category", "Unit")]
    [Fact]
    public void EveryResolution_IsAttributableToAVisibleRule()
    {
        var cwdDir = NewTempDir();

        Isolated(() =>
        {
            Directory.SetCurrentDirectory(cwdDir);
            var r = CanaryPaths.ResolveWorkloadsRootDetailed();

            Assert.Contains(r.Source, new[]
            {
                CanaryPaths.WorkloadsSource.CurrentDirectory,
                CanaryPaths.WorkloadsSource.ExecutableWalkUp,
                CanaryPaths.WorkloadsSource.FallbackNotFound,
            });
            // And whatever matched, Describe must name it, so an operator can tell which.
            Assert.Contains(r.Path, CanaryPaths.Describe(r));
            return 0;
        });
    }

    // ---- Expand: the token seam Phase 2 will build on -----------------------

    [Trait("Category", "Unit")]
    [Fact]
    public void Expand_SubstitutesAKnownVariable()
    {
        var expected = Environment.GetEnvironmentVariable("TEMP");
        Assert.False(string.IsNullOrEmpty(expected), "TEMP must be set for this test to mean anything");

        Assert.Equal(expected, CanaryPaths.Expand("%TEMP%"));
    }

    // The safety property the whole change rests on. Test content is full of ordinary
    // text; if an unknown token collapsed to an empty string, a mistyped token would
    // silently produce a valid-looking but wrong value instead of a visible failure.
    [Trait("Category", "Unit")]
    [Fact]
    public void Expand_LeavesUnknownTokensExactlyAsWritten()
    {
        const string s = "%CANARY_NO_SUCH_VARIABLE_XYZ%";
        Assert.Equal(s, CanaryPaths.Expand(s));
    }

    // Percent signs appear in ordinary prose and in Grasshopper panel values. Nothing
    // that is not a real variable may be disturbed.
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("scale to 50% then 75%")]
    [InlineData("100%")]
    [InlineData("a % b")]
    [InlineData("_Zoom _Selected _Enter")]
    public void Expand_DoesNotDisturbOrdinaryText(string text)
    {
        Assert.Equal(text, CanaryPaths.Expand(text));
    }

    // Mid-string substitution is what lets a Rhino macro or a panel value carry a token.
    [Trait("Category", "Unit")]
    [Fact]
    public void Expand_SubstitutesMidString()
    {
        var temp = Environment.GetEnvironmentVariable("TEMP");
        var result = CanaryPaths.Expand("prefix|%TEMP%|suffix");

        Assert.Equal($"prefix|{temp}|suffix", result);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Expand_HandlesNullAndEmpty(string? input)
    {
        Assert.Equal(string.Empty, CanaryPaths.Expand(input));
    }

    // ---- WIRING: proves AsParameters actually CALLS Expand -------------------
    // Without these, everything above would pass while the seam sat unconnected -
    // precisely the "guard that never fires" failure this campaign warns about.

    [Trait("Category", "Unit")]
    [Fact]
    public void AsParameters_ExpandsTokensInActionValues()
    {
        var temp = Environment.GetEnvironmentVariable("TEMP");
        var json = "{ \"type\": \"GrasshopperSetPanelText\", \"nickname\": \"JsonPath\", \"text\": \"%TEMP%/defs/x.json\" }";

        var action = System.Text.Json.JsonSerializer.Deserialize<TestAction>(json);
        Assert.NotNull(action);

        var p = action!.AsParameters();

        Assert.True(p.ContainsKey("text"), "extension-data capture must carry 'text'");
        Assert.Equal(temp + "/defs/x.json", p["text"]);
        Assert.DoesNotContain("%TEMP%", p["text"]);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void AsParameters_LeavesNonTokenTextAlone()
    {
        var json = "{ \"type\": \"RunCommand\", \"text\": \"_Zoom _Selected _Enter\", \"ratio\": \"50% of it\" }";

        var action = System.Text.Json.JsonSerializer.Deserialize<TestAction>(json);
        var p = action!.AsParameters();

        Assert.Equal("_Zoom _Selected _Enter", p["text"]);
        Assert.Equal("50% of it", p["ratio"]);
    }
}
