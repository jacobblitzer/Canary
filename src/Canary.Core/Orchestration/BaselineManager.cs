namespace Canary.Orchestration;

/// <summary>
/// Manages baseline approval and rejection for visual regression tests.
/// </summary>
public static class BaselineManager
{
    /// <summary>
    /// Approve a test by copying candidates to baselines.
    /// When suiteName is provided, paths are scoped to results/{suiteName}/{testName}/.
    /// </summary>
    public static int ApproveTest(string workloadsDir, string workloadName, string testName, string? suiteName = null)
    {
        var testDir = GetTestDirectory(workloadsDir, workloadName, testName, suiteName);
        var candidatesDir = Path.Combine(testDir, "candidates");
        var baselinesDir = Path.Combine(testDir, "baselines");

        if (!Directory.Exists(candidatesDir))
            throw new DirectoryNotFoundException($"No candidates found for test '{testName}'. Run the test first.");

        Directory.CreateDirectory(baselinesDir);

        int count = 0;
        foreach (var file in Directory.GetFiles(candidatesDir, "*.png"))
        {
            var dest = Path.Combine(baselinesDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Approve a single checkpoint by copying its candidate to baseline.
    /// </summary>
    public static void ApproveCheckpoint(string workloadsDir, string workloadName, string testName, string checkpointName, string? suiteName = null)
    {
        var testDir = GetTestDirectory(workloadsDir, workloadName, testName, suiteName);
        var candidatePath = Path.Combine(testDir, "candidates", $"{checkpointName}.png");
        var baselinePath = Path.Combine(testDir, "baselines", $"{checkpointName}.png");

        if (!File.Exists(candidatePath))
            throw new FileNotFoundException($"No candidate found for checkpoint '{checkpointName}'.", candidatePath);

        Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
        File.Copy(candidatePath, baselinePath, overwrite: true);
    }

    /// <summary>
    /// Reject a checkpoint by deleting its candidate.
    /// </summary>
    public static void RejectCheckpoint(string workloadsDir, string workloadName, string testName, string checkpointName, string? suiteName = null)
    {
        var testDir = GetTestDirectory(workloadsDir, workloadName, testName, suiteName);
        var candidatePath = Path.Combine(testDir, "candidates", $"{checkpointName}.png");

        if (File.Exists(candidatePath))
            File.Delete(candidatePath);
    }

    private static string GetTestDirectory(string workloadsDir, string workloadName, string testName, string? suiteName)
    {
        if (suiteName != null)
            return Path.Combine(workloadsDir, workloadName, "results", suiteName, testName);
        return Path.Combine(workloadsDir, workloadName, "results", testName);
    }
}
