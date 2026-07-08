namespace Canary.Orchestration;

/// <summary>
/// Status of a test or checkpoint.
/// </summary>
public enum TestStatus
{
    Passed,
    Failed,
    Crashed,
    New  // No baseline exists yet
}

/// <summary>
/// Result of running a single checkpoint within a test.
/// </summary>
public sealed class CheckpointResult
{
    public required string Name { get; init; }
    public TestStatus Status { get; set; }
    public double DiffPercentage { get; set; }
    public double Tolerance { get; set; }
    public double SsimScore { get; set; }
    public string? BaselinePath { get; set; }
    public string? CandidatePath { get; set; }
    public string? DiffImagePath { get; set; }

    /// <summary>
    /// Path to the encoded animated GIF, when the checkpoint requested
    /// <c>capture.gif=true</c> and at least one frame was captured. Sibling of
    /// <see cref="CandidatePath"/>, e.g. <c>candidates/post-build.gif</c>.
    /// Phase 4.6.F Session B.
    /// </summary>
    public string? GifPath { get; set; }

    public string? ErrorMessage { get; set; }

    // VLM oracle fields — populated only for mode="vlm" checkpoints
    public string? VlmReasoning { get; set; }
    public double VlmConfidence { get; set; }
    public string? VlmDescription { get; set; }
}

/// <summary>
/// Result of running a single test (which may contain multiple checkpoints).
/// </summary>
public sealed class TestResult
{
    public required string TestName { get; init; }
    public required string Workload { get; init; }
    public TestStatus Status { get; set; }
    public List<CheckpointResult> CheckpointResults { get; set; } = new();
    public string? CompositeImagePath { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// GH diagnostic dump (component runtime messages + all panel text)
    /// captured when an assert fails. Null when the test passed or no dump
    /// was requested. Surfaces GH 'breakpoint'-dialog error text that
    /// otherwise only appears as a modal dialog invisible to the agent.
    /// R6.5 Phase G experiment (bug 0018).
    /// </summary>
    public string? DiagnosticDump { get; set; }
}

/// <summary>
/// Aggregated results from running a suite of tests.
/// </summary>
public sealed class SuiteResult
{
    public List<TestResult> TestResults { get; set; } = new();
    public int Passed => TestResults.Count(t => t.Status == TestStatus.Passed);
    public int Failed => TestResults.Count(t => t.Status == TestStatus.Failed);
    public int Crashed => TestResults.Count(t => t.Status == TestStatus.Crashed);
    public int New => TestResults.Count(t => t.Status == TestStatus.New);
    public TimeSpan TotalDuration => TimeSpan.FromTicks(TestResults.Sum(t => t.Duration.Ticks));
}
