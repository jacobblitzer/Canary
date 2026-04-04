using Canary.Config;

namespace Canary.Orchestration;

/// <summary>
/// Discovers test definitions for a workload by scanning the tests directory.
/// </summary>
public static class TestDiscovery
{
    /// <summary>
    /// Discover all test definitions for a workload.
    /// </summary>
    public static async Task<List<TestDefinition>> DiscoverTestsAsync(
        string workloadsDir, string workloadName, ITestLogger? logger = null)
    {
        var testsDir = Path.Combine(workloadsDir, workloadName, "tests");
        if (!Directory.Exists(testsDir))
            return new List<TestDefinition>();

        var tests = new List<TestDefinition>();
        foreach (var file in Directory.GetFiles(testsDir, "*.json"))
        {
            try
            {
                tests.Add(await TestDefinition.LoadAsync(file).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                logger?.Log($"Warning: Failed to parse test '{file}': {ex.Message}");
            }
        }
        return tests;
    }
}
