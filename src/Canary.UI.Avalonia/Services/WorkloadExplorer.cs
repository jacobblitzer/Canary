using Canary.Config;
using Canary.Orchestration;

namespace Canary.UI.Avalonia.Services;

// Ported verbatim from src/Canary.UI/Services/WorkloadExplorer.cs.
// Phase 6 cutover: when the WinForms project goes away, the WinForms
// copy of this file goes with it; this file remains. The two are
// equivalent until then.
public sealed class WorkloadExplorer
{
    public sealed class WorkloadEntry
    {
        public required WorkloadConfig Config { get; init; }
        public required List<SuiteDefinition> Suites { get; init; }
        public required List<TestDefinition> Tests { get; init; }
        public required List<string> Recordings { get; init; }
        public required string Directory { get; init; }
    }

    public event Action<string>? LoadWarning;

    public async Task<List<WorkloadEntry>> LoadWorkloadsAsync(string workloadsDir)
    {
        var entries = new List<WorkloadEntry>();

        if (!System.IO.Directory.Exists(workloadsDir))
            return entries;

        foreach (var dir in System.IO.Directory.GetDirectories(workloadsDir))
        {
            var configPath = Path.Combine(dir, "workload.json");
            if (!File.Exists(configPath))
                continue;

            try
            {
                var config = await WorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
                var workloadName = Path.GetFileName(dir);
                var tests = await TestDiscovery.DiscoverTestsAsync(workloadsDir, workloadName).ConfigureAwait(false);

                List<SuiteDefinition> suites;
                try
                {
                    suites = await TestDiscovery.DiscoverSuitesAsync(workloadsDir, workloadName).ConfigureAwait(false);
                }
                catch
                {
                    suites = new List<SuiteDefinition>();
                }

                var recordings = new List<string>();
                var recordingsDir = Path.Combine(dir, "recordings");
                if (System.IO.Directory.Exists(recordingsDir))
                {
                    foreach (var file in System.IO.Directory.GetFiles(recordingsDir, "*.input.json"))
                        recordings.Add(file);
                }

                entries.Add(new WorkloadEntry
                {
                    Config = config,
                    Directory = dir,
                    Suites = suites,
                    Tests = tests,
                    Recordings = recordings,
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
