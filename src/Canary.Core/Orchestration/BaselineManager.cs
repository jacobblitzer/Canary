namespace Canary.Orchestration;

/// <summary>
/// Manages baseline approval and rejection for visual regression tests.
/// </summary>
/// <remarks>
/// <para>
/// Paths come from <see cref="ResultPaths"/> and nowhere else. This class used to carry
/// its own byte-identical copy of the derivation plus an optional <c>suiteName</c>, and
/// that pair was half of Phase 2b's defect: approval wrote
/// <c>results/&lt;suite&gt;/&lt;test&gt;/</c> while the shared run path read
/// <c>results/&lt;test&gt;/</c>, so 59 approved images were never compared by anything.
/// </para>
/// <para>
/// The parameter is <b>removed</b> rather than ignored, so the compiler names every caller
/// that relied on it instead of leaving a silently-unused argument for someone to
/// repurpose — which is exactly how <c>PastRunsViewModel</c> came to pass a run timestamp
/// as a suite name.
/// </para>
/// </remarks>
public static class BaselineManager
{
    /// <summary>
    /// Approve a test by copying candidates to baselines.
    /// </summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workloadName">Workload name.</param>
    /// <param name="testName">Test name.</param>
    /// <returns>Number of checkpoint images approved.</returns>
    public static int ApproveTest(string workloadsDir, string workloadName, string testName)
        => ApproveTestFiles(workloadsDir, workloadName, testName).Length;

    /// <summary>
    /// Approve a test by copying candidates to baselines, returning the checkpoint file names
    /// that were blessed (R1.3: `canary approve` prints WHAT it approved, not just a count).
    /// </summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workloadName">Workload name.</param>
    /// <param name="testName">Test name.</param>
    /// <returns>The blessed file names.</returns>
    /// <exception cref="DirectoryNotFoundException">The test has no candidates.</exception>
    public static string[] ApproveTestFiles(string workloadsDir, string workloadName, string testName)
    {
        var testDir = ResultPaths.TestDir(workloadsDir, workloadName, testName);
        var candidatesDir = Path.Combine(testDir, ResultPaths.CandidatesDir);
        var baselinesDir = Path.Combine(testDir, ResultPaths.BaselinesDir);

        if (!Directory.Exists(candidatesDir))
            throw new DirectoryNotFoundException($"No candidates found for test '{testName}'. Run the test first.");

        Directory.CreateDirectory(baselinesDir);

        var approved = new List<string>();
        foreach (var file in Directory.GetFiles(candidatesDir, "*.png"))
        {
            var name = Path.GetFileName(file);

            // A `<checkpoint>.fullscreen.png` is a COMPANION image that RhinoScreenCapture
            // writes next to the candidate; no comparison ever reads one, because the
            // runner only ever looks for `baselines/<checkpoint>.png`. Blessing them
            // indiscriminately put 92 files into baselines/ that are unreadable by
            // anything, and every "how many baselines are at risk" count since has been an
            // over-count because of it. Verified safe to skip: zero of 828 declared
            // checkpoints have a name containing ".fullscreen".
            if (name.EndsWith(".fullscreen.png", StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(file, Path.Combine(baselinesDir, name), overwrite: true);
            approved.Add(name);
        }

        return approved.ToArray();
    }

    /// <summary>
    /// Approve a single checkpoint by copying its candidate to baseline.
    /// </summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workloadName">Workload name.</param>
    /// <param name="testName">Test name.</param>
    /// <param name="checkpointName">Checkpoint name.</param>
    /// <exception cref="FileNotFoundException">No candidate for that checkpoint.</exception>
    public static void ApproveCheckpoint(
        string workloadsDir, string workloadName, string testName, string checkpointName)
    {
        var testDir = ResultPaths.TestDir(workloadsDir, workloadName, testName);
        var candidatePath = ResultPaths.CandidateIn(testDir, checkpointName);
        var baselinePath = ResultPaths.BaselineIn(testDir, checkpointName);

        if (!File.Exists(candidatePath))
            throw new FileNotFoundException($"No candidate found for checkpoint '{checkpointName}'.", candidatePath);

        Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
        File.Copy(candidatePath, baselinePath, overwrite: true);
    }

    /// <summary>
    /// Reject a checkpoint by deleting its candidate.
    /// </summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workloadName">Workload name.</param>
    /// <param name="testName">Test name.</param>
    /// <param name="checkpointName">Checkpoint name.</param>
    public static void RejectCheckpoint(
        string workloadsDir, string workloadName, string testName, string checkpointName)
    {
        var candidatePath = ResultPaths.CandidateIn(
            ResultPaths.TestDir(workloadsDir, workloadName, testName), checkpointName);

        if (File.Exists(candidatePath))
            File.Delete(candidatePath);
    }
}
