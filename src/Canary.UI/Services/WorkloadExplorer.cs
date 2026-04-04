using Canary.Config;
using Canary.Orchestration;

namespace Canary.UI.Services;

/// <summary>
/// Scans a workloads directory and loads all workload configs and test definitions.
/// </summary>
public sealed class WorkloadExplorer
{
    /// <summary>
    /// A discovered workload with its config and test definitions.
    /// </summary>
    public sealed class WorkloadEntry
    {
        public required WorkloadConfig Config { get; init; }
        public required List<TestDefinition> Tests { get; init; }
        public required string Directory { get; init; }
    }

    /// <summary>
    /// Discover all workloads in the given directory.
    /// Each subdirectory with a workload.json is treated as a workload.
    /// </summary>
    public async Task<List<WorkloadEntry>> LoadWorkloadsAsync(string workloadsDir)
    {
        var entries = new List<WorkloadEntry>();

        if (!Directory.Exists(workloadsDir))
            return entries;

        foreach (var dir in Directory.GetDirectories(workloadsDir))
        {
            var configPath = Path.Combine(dir, "workload.json");
            if (!File.Exists(configPath))
                continue;

            try
            {
                var config = await WorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
                var workloadName = Path.GetFileName(dir);
                var tests = await TestDiscovery.DiscoverTestsAsync(workloadsDir, workloadName).ConfigureAwait(false);

                entries.Add(new WorkloadEntry
                {
                    Config = config,
                    Directory = dir,
                    Tests = tests
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WorkloadExplorer error: {ex}");
                // Skip workloads with invalid configs
            }
        }

        return entries;
    }
}
