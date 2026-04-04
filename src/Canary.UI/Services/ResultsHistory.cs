using Canary.Orchestration;

namespace Canary.UI.Services;

/// <summary>
/// Scans results directories for saved test result JSON files.
/// </summary>
public sealed class ResultsHistory
{
    /// <summary>
    /// A history entry for a single test run.
    /// </summary>
    public sealed class HistoryEntry
    {
        public required string FilePath { get; init; }
        public required TestResult Result { get; init; }
        public required DateTime Timestamp { get; init; }
    }

    /// <summary>
    /// Scan results directories under a workload for saved result JSON files.
    /// </summary>
    public async Task<List<HistoryEntry>> ScanAsync(string workloadsDir, string workloadName)
    {
        var entries = new List<HistoryEntry>();
        var resultsDir = Path.Combine(workloadsDir, workloadName, "results");

        if (!Directory.Exists(resultsDir))
            return entries;

        foreach (var file in Directory.GetFiles(resultsDir, "result.json", SearchOption.AllDirectories))
        {
            try
            {
                var result = await TestResultSerializer.LoadAsync(file).ConfigureAwait(false);
                entries.Add(new HistoryEntry
                {
                    FilePath = file,
                    Result = result,
                    Timestamp = File.GetLastWriteTimeUtc(file)
                });
            }
            catch
            {
                // Skip invalid result files
            }
        }

        return entries.OrderByDescending(e => e.Timestamp).ToList();
    }
}
