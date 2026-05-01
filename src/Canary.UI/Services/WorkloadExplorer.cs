using Canary.Config;
using Canary.Orchestration;

namespace Canary.UI.Services;

/// <summary>
/// Scans a workloads directory and loads all workload configs, suites, test definitions, and recordings.
/// </summary>
public sealed class WorkloadExplorer
{
    /// <summary>
    /// A discovered workload with its config, suites, test definitions, and recordings.
    /// </summary>
    public sealed class WorkloadEntry
    {
        public required WorkloadConfig Config { get; init; }
        public required List<SuiteDefinition> Suites { get; init; }
        public required List<TestDefinition> Tests { get; init; }
        public required List<string> Recordings { get; init; }
        public required string Directory { get; init; }
    }

    /// <summary>Event raised when a workload directory has an invalid config.</summary>
    public event Action<string>? LoadWarning;

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

                // Discover suites
                List<SuiteDefinition> suites;
                try
                {
                    suites = await TestDiscovery.DiscoverSuitesAsync(workloadsDir, workloadName).ConfigureAwait(false);
                }
                catch
                {
                    suites = new List<SuiteDefinition>();
                }

                // Discover recordings
                var recordings = new List<string>();
                var recordingsDir = Path.Combine(dir, "recordings");
                if (Directory.Exists(recordingsDir))
                {
                    foreach (var file in Directory.GetFiles(recordingsDir, "*.input.json"))
                        recordings.Add(file);
                }

                entries.Add(new WorkloadEntry
                {
                    Config = config,
                    Directory = dir,
                    Suites = suites,
                    Tests = tests,
                    Recordings = recordings
                });
            }
            catch (Exception ex)
            {
                var msg = $"Skipped '{Path.GetFileName(dir)}': {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"WorkloadExplorer: {msg}");
                LoadWarning?.Invoke(msg);
            }
        }

        return entries;
    }
}
