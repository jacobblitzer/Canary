using System.IO;
using Canary.Cli;
using Xunit;

namespace Canary.Tests.Cli;

// BUG-0007 follow-up — ApproveCommand + ReportCommand were using the void-handler
// pattern that swallowed exit codes; their handlers now propagate via ctx.ExitCode.
// These tests pin the new behaviour so future refactors don't silently regress
// CI consumers.
[Trait("Category", "Unit")]
public class ApproveReportExitCodeTests
{
    [Fact]
    public void Approve_NonexistentWorkload_ReturnsOne()
    {
        // Save cwd, swap to a temp dir so the "workloads/" lookup hits nothing.
        var prevCwd = Directory.GetCurrentDirectory();
        var tmp = Path.Combine(Path.GetTempPath(), "canary-approve-test-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        Directory.SetCurrentDirectory(tmp);
        try
        {
            var exit = ApproveCommand.ApproveInner(
                workload: "this-workload-does-not-exist-xyz",
                test: "this-test-also-does-not-exist",
                suite: null);
            Assert.Equal(1, exit);
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Report_NoWorkloadAndNoReport_ReturnsOne()
    {
        var prevCwd = Directory.GetCurrentDirectory();
        var tmp = Path.Combine(Path.GetTempPath(), "canary-report-test-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        Directory.SetCurrentDirectory(tmp);
        try
        {
            var exit = ReportCommand.ReportInner(workload: null);
            Assert.Equal(1, exit);
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Report_NonexistentWorkload_ReturnsOne()
    {
        var prevCwd = Directory.GetCurrentDirectory();
        var tmp = Path.Combine(Path.GetTempPath(), "canary-report-test-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        Directory.SetCurrentDirectory(tmp);
        try
        {
            var exit = ReportCommand.ReportInner(workload: "this-workload-does-not-exist-xyz");
            Assert.Equal(1, exit);
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }
}
