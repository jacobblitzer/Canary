using Canary.Localhost;
using Xunit;

namespace Canary.Tests.Localhost;

// Phase 8 / design §C7 Tier 3 — opt-in name-heuristic process listing.
// Real-machine smoke (calls Process.GetProcesses); contract tests only.
public class Tier3HeuristicTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void DefaultProcessNames_IncludesCommonDevServerShells()
    {
        Assert.Contains("node", HeuristicProcessLister.DefaultProcessNames);
        Assert.Contains("python", HeuristicProcessLister.DefaultProcessNames);
        Assert.Contains("dotnet", HeuristicProcessLister.DefaultProcessNames);
        Assert.Contains("cargo", HeuristicProcessLister.DefaultProcessNames);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Enumerate_ReturnsListWithoutThrowing()
    {
        var entries = HeuristicProcessLister.Enumerate();
        Assert.NotNull(entries);
        Assert.All(entries, e =>
        {
            Assert.True(e.Pid > 0, "pid should be positive");
            Assert.False(string.IsNullOrEmpty(e.Name), "name should be populated");
        });
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Enumerate_HonorsCustomNamesFilter()
    {
        // Filter to a name that definitely matches the current test runner
        // process (dotnet on dev machines).
        var entries = HeuristicProcessLister.Enumerate(new[] { "dotnet" });
        Assert.All(entries, e => Assert.Equal("dotnet", e.Name, ignoreCase: true));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Enumerate_EmptyFilter_ReturnsEmpty()
    {
        var entries = HeuristicProcessLister.Enumerate(Array.Empty<string>());
        Assert.Empty(entries);
    }
}
