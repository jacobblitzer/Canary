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

    /// <summary>
    /// Discover all suite definitions for a workload by scanning the suites/ directory.
    /// </summary>
    public static async Task<List<SuiteDefinition>> DiscoverSuitesAsync(
        string workloadsDir, string workloadName, ITestLogger? logger = null)
    {
        var suitesDir = Path.Combine(workloadsDir, workloadName, "suites");
        if (!Directory.Exists(suitesDir))
            return new List<SuiteDefinition>();

        var suites = new List<SuiteDefinition>();
        foreach (var file in Directory.GetFiles(suitesDir, "*.json"))
        {
            try
            {
                suites.Add(await SuiteDefinition.LoadAsync(file).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                logger?.Log($"Warning: Failed to parse suite '{file}': {ex.Message}");
            }
        }
        return suites;
    }

    /// <summary>
    /// Load a suite by name and resolve each test name to its TestDefinition from the tests/ directory.
    /// </summary>
    /// <returns>
    /// The suite, the tests that resolved, and <b>the names that did not</b>.
    /// </returns>
    /// <remarks>
    /// Deployment campaign Phase 3. This used to warn-and-continue, which meant a suite
    /// SILENTLY SHRANK: on a machine carrying 1 of 51 tests it would run that one, report
    /// it, and exit 0. Combined with a missing baseline yielding <c>New</c> - which the exit
    /// code excludes - a half-installed rig printed a pass. Returning the missing names
    /// lets the caller refuse to run rather than quietly running less.
    /// </remarks>
    public static async Task<(SuiteDefinition Suite, List<TestDefinition> Tests, List<string> MissingTests)> DiscoverTestsForSuiteAsync(
        string workloadsDir, string workloadName, string suiteName, ITestLogger? logger = null)
    {
        var suitePath = Path.Combine(workloadsDir, workloadName, "suites", $"{suiteName}.json");
        if (!File.Exists(suitePath))
            throw new FileNotFoundException($"Suite definition not found: {suitePath}", suitePath);

        var suite = await SuiteDefinition.LoadAsync(suitePath).ConfigureAwait(false);

        var tests = new List<TestDefinition>();
        var missing = new List<string>();
        foreach (var testName in suite.Tests)
        {
            var testPath = Path.Combine(workloadsDir, workloadName, "tests", $"{testName}.json");
            if (!File.Exists(testPath))
            {
                logger?.Log($"Warning: Suite '{suiteName}' references test '{testName}' but {testPath} not found.");
                missing.Add(testName);
                continue;
            }

            try
            {
                tests.Add(await TestDefinition.LoadAsync(testPath).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                // An UNPARSABLE test is missing too. It was previously only warned about,
                // which is how eleven qualia tests stayed broken without anyone noticing.
                logger?.Log($"Warning: Failed to parse test '{testPath}': {ex.Message}");
                missing.Add(testName);
            }
        }

        return (suite, tests, missing);
    }
}
