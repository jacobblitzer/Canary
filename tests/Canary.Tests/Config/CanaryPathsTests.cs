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
}
