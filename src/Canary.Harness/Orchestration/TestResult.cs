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
    public string? ErrorMessage { get; set; }
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
