namespace Canary.Orchestration;

/// <summary>
/// The one place a result path is derived.
/// </summary>
/// <remarks>
/// <para>
/// Deployment campaign Phase 2b. <b>A test's evidence directory is a pure function of
/// (workload, test). It never contains a suite segment. A suite owns only its rollups.</b>
/// </para>
/// <para>
/// Before this, the derivation was split. <c>RunSharedSuiteAsync</c> had no
/// <c>suiteName</c> parameter at all, so it read <c>results/&lt;test&gt;/</c>, while
/// <c>BaselineManager</c> wrote <c>results/&lt;suite&gt;/&lt;test&gt;/</c> whenever
/// approval had been given a suite. Approval and execution could therefore disagree about
/// where the pixels live — and because a missing baseline yields <c>New</c>, which the
/// exit code excludes, the disagreement printed a pass. Six suites were in that state:
/// 32 tests, 59 approved images, none of them ever compared.
/// </para>
/// <para>
/// <b>Why no suite segment, rather than a suite segment everywhere.</b> Nothing can make
/// a per-suite baseline mean anything: <c>SuiteDefinition</c> carries only
/// <c>name</c>, <c>description</c>, <c>tests</c> and <c>keepOpen</c> — no capture
/// geometry, tolerance or mode — while capture geometry is per-<i>test</i>. And
/// <c>--test</c> and <c>--suite</c> are mutually exclusive, so a suite-scoped contract
/// would have no home at all for a solo run and would need an invented sentinel. Two
/// spellings under one name is the defect, not the fix.
/// </para>
/// <para>
/// <b>There is no nullable suite parameter here, deliberately.</b> The old helpers keyed
/// off <c>suiteName != null</c>, and because <see cref="Path.Combine(string, string)"/>
/// drops empty segments, <c>""</c> read as "a suite was supplied" and silently produced
/// the unscoped path. Any normalisation of "no suite" to <c>""</c> would look applied and
/// behave as before. Removing the parameter removes the trap, and makes the compiler
/// enumerate every site that used to depend on it.
/// </para>
/// </remarks>
public static class ResultPaths
{
    /// <summary>Directory name holding approved images.</summary>
    public const string BaselinesDir = "baselines";

    /// <summary>Directory name holding the images a run just captured.</summary>
    public const string CandidatesDir = "candidates";

    /// <summary>Directory name holding rendered pixel differences.</summary>
    public const string DiffsDir = "diffs";

    /// <summary>Directory name holding one subdirectory per run.</summary>
    public const string RunsDir = "runs";

    /// <summary>Directory name holding manually saved snapshots.</summary>
    public const string ArchivedDir = "archived";

    /// <summary>File name of the per-test baseline|candidate|diff strip.</summary>
    public const string CompositeFile = "composite.png";

    /// <summary>The workload's results root: <c>&lt;workloads&gt;/&lt;workload&gt;/results</c>.</summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <returns>The results root.</returns>
    public static string ResultsRoot(string workloadsDir, string workload)
        => Path.Combine(workloadsDir, workload, "results");

    /// <summary>
    /// A test's evidence directory: <c>results/&lt;test&gt;</c>, with no suite segment.
    /// </summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <param name="test">Test name.</param>
    /// <returns>The test directory.</returns>
    public static string TestDir(string workloadsDir, string workload, string test)
        => Path.Combine(ResultsRoot(workloadsDir, workload), test);

    /// <summary>Approved image for one checkpoint.</summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <param name="test">Test name.</param>
    /// <param name="checkpoint">Checkpoint name.</param>
    /// <returns>The baseline PNG path.</returns>
    public static string BaselinePath(string workloadsDir, string workload, string test, string checkpoint)
        => Path.Combine(TestDir(workloadsDir, workload, test), BaselinesDir, $"{checkpoint}.png");

    /// <summary>Approved image for one checkpoint, from an already-resolved test directory.</summary>
    /// <param name="testDir">A directory produced by <see cref="TestDir"/>.</param>
    /// <param name="checkpoint">Checkpoint name.</param>
    /// <returns>The baseline PNG path.</returns>
    public static string BaselineIn(string testDir, string checkpoint)
        => Path.Combine(testDir, BaselinesDir, $"{checkpoint}.png");

    /// <summary>Just-captured image for one checkpoint.</summary>
    /// <param name="testDir">A directory produced by <see cref="TestDir"/>.</param>
    /// <param name="checkpoint">Checkpoint name.</param>
    /// <returns>The candidate PNG path.</returns>
    public static string CandidateIn(string testDir, string checkpoint)
        => Path.Combine(testDir, CandidatesDir, $"{checkpoint}.png");

    /// <summary>Rendered difference for one checkpoint.</summary>
    /// <param name="testDir">A directory produced by <see cref="TestDir"/>.</param>
    /// <param name="checkpoint">Checkpoint name.</param>
    /// <returns>The diff PNG path.</returns>
    public static string DiffIn(string testDir, string checkpoint)
        => Path.Combine(testDir, DiffsDir, $"{checkpoint}.png");

    /// <summary>The baseline|candidate|diff strip for a test.</summary>
    /// <param name="testDir">A directory produced by <see cref="TestDir"/>.</param>
    /// <returns>The composite PNG path.</returns>
    public static string CompositeIn(string testDir)
        => Path.Combine(testDir, CompositeFile);

    /// <summary>The directory holding a test's run history.</summary>
    /// <param name="testDir">A directory produced by <see cref="TestDir"/>.</param>
    /// <returns>The runs directory.</returns>
    public static string RunsIn(string testDir)
        => Path.Combine(testDir, RunsDir);

    /// <summary>One run's directory.</summary>
    /// <param name="testDir">A directory produced by <see cref="TestDir"/>.</param>
    /// <param name="runId">Run identifier (timestamp + short hash).</param>
    /// <returns>The run directory.</returns>
    public static string RunIn(string testDir, string runId)
        => Path.Combine(testDir, RunsDir, runId);

    /// <summary>One snapshot slot's directory.</summary>
    /// <param name="testDir">A directory produced by <see cref="TestDir"/>.</param>
    /// <param name="slot">Slot name.</param>
    /// <returns>The snapshot directory.</returns>
    public static string SnapshotIn(string testDir, string slot)
        => Path.Combine(testDir, ArchivedDir, slot);

    /// <summary>
    /// Where a run's rollups go: <c>results/&lt;suite&gt;</c> for a suite run,
    /// <c>results/</c> otherwise.
    /// </summary>
    /// <param name="workloadsDir">Workloads root.</param>
    /// <param name="workload">Workload name.</param>
    /// <param name="suite">Suite name, or null for a single test / whole workload.</param>
    /// <returns>The directory for <c>report.html</c>, <c>junit.xml</c> and <c>telemetry.ndjson</c>.</returns>
    /// <remarks>
    /// This is the ONLY place a suite name may appear in a result path, and it never
    /// contains a test's evidence. Suite rollups and test directories are siblings under
    /// <c>results/</c>, which is why <c>canary doctor</c> refuses a suite whose name
    /// collides with a test's.
    /// </remarks>
    public static string RollupDir(string workloadsDir, string workload, string? suite)
        => string.IsNullOrWhiteSpace(suite)
            ? ResultsRoot(workloadsDir, workload)
            : Path.Combine(ResultsRoot(workloadsDir, workload), suite);
}
