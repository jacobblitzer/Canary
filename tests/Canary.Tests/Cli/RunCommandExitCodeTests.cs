using Canary.Cli;
using Canary.Orchestration;
using Xunit;

namespace Canary.Tests.Cli;

// Regression tests for bug 0007 — CLI `canary run` returned 0 even on failure.
// Verifies RunCommand.RunAsync now returns Task<int> and that the helper
// ExitCodeFromSuiteResult maps SuiteResult to the documented exit-code shape:
//   0 = no failures, 1 = any test failed or crashed.
public class RunCommandExitCodeTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void ExitCode_NoTests_IsZero()
    {
        var result = new SuiteResult();

        Assert.Equal(0, RunCommand.ExitCodeFromSuiteResult(result));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ExitCode_AllPassed_IsZero()
    {
        var result = new SuiteResult
        {
            TestResults =
            {
                new TestResult { TestName = "a", Workload = "w", Status = TestStatus.Passed },
                new TestResult { TestName = "b", Workload = "w", Status = TestStatus.Passed },
            }
        };

        Assert.Equal(0, RunCommand.ExitCodeFromSuiteResult(result));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ExitCode_OneFailed_IsOne()
    {
        var result = new SuiteResult
        {
            TestResults =
            {
                new TestResult { TestName = "a", Workload = "w", Status = TestStatus.Passed },
                new TestResult { TestName = "b", Workload = "w", Status = TestStatus.Failed },
            }
        };

        Assert.Equal(1, RunCommand.ExitCodeFromSuiteResult(result));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ExitCode_OneCrashed_IsOne()
    {
        var result = new SuiteResult
        {
            TestResults =
            {
                new TestResult { TestName = "a", Workload = "w", Status = TestStatus.Crashed },
            }
        };

        Assert.Equal(1, RunCommand.ExitCodeFromSuiteResult(result));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ExitCode_NewBaselineOnly_IsZero()
    {
        // `New` status means a test ran and produced a first baseline; not a failure.
        var result = new SuiteResult
        {
            TestResults =
            {
                new TestResult { TestName = "a", Workload = "w", Status = TestStatus.New },
            }
        };

        Assert.Equal(0, RunCommand.ExitCodeFromSuiteResult(result));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task RunAsync_MissingWorkloadName_ReturnsOne()
    {
        var logger = new ConsoleTestLogger(verbose: false, quiet: true);

        var exit = await RunCommand.RunAsync(
            workloadName: null,
            testName: null,
            suiteName: null,
            logger: logger,
            ct: CancellationToken.None);

        Assert.Equal(1, exit);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task RunAsync_TestAndSuiteMutuallyExclusive_ReturnsOne()
    {
        var logger = new ConsoleTestLogger(verbose: false, quiet: true);

        var exit = await RunCommand.RunAsync(
            workloadName: "anything",
            testName: "a-test",
            suiteName: "a-suite",
            logger: logger,
            ct: CancellationToken.None);

        Assert.Equal(1, exit);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task RunAsync_NonexistentWorkload_ReturnsOne()
    {
        var logger = new ConsoleTestLogger(verbose: false, quiet: true);

        var exit = await RunCommand.RunAsync(
            workloadName: "this-workload-definitely-does-not-exist-xyz",
            testName: null,
            suiteName: null,
            logger: logger,
            ct: CancellationToken.None);

        Assert.Equal(1, exit);
    }
}
