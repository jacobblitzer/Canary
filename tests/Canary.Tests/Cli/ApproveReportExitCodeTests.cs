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
    public void Approve_NoTestAndNoSuite_ReturnsOne()
    {
        var exit = ApproveCommand.ApproveInner(workload: "rhino", test: null, suite: null);
        Assert.Equal(1, exit);
    }

    [Fact]
    public void Approve_SuiteBulk_MissingSuiteJson_ReturnsOne()
    {
        var prevCwd = Directory.GetCurrentDirectory();
        var tmp = Path.Combine(Path.GetTempPath(), "canary-approve-test-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        Directory.SetCurrentDirectory(tmp);
        try
        {
            var exit = ApproveCommand.ApproveInner(workload: "rhino", test: null, suite: "no-such-suite");
            Assert.Equal(1, exit);
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    // R1.3: bulk-suite approval must fall back to the SHARED layout (results/<test>/ with no
    // suite dir) — that is where every shared-runMode rhino suite writes per-test artifacts.
    [Fact]
    public void Approve_SuiteBulk_SharedLayoutFallback_ApprovesAndReturnsZero()
    {
        var prevCwd = Directory.GetCurrentDirectory();
        var tmp = Path.Combine(Path.GetTempPath(), "canary-approve-test-" + System.Guid.NewGuid().ToString("N"));
        Directory.SetCurrentDirectory(CreateTree(tmp));
        try
        {
            var suitesDir = Path.Combine(tmp, "workloads", "rhino", "suites");
            Directory.CreateDirectory(suitesDir);
            File.WriteAllText(Path.Combine(suitesDir, "mini.json"),
                "{ \"name\": \"mini\", \"tests\": [\"t-alpha\", \"t-beta\", \"t-never-ran\"] }");

            // t-alpha + t-beta candidates in the SHARED layout; t-never-ran has none.
            foreach (var t in new[] { "t-alpha", "t-beta" })
            {
                var cand = Path.Combine(tmp, "workloads", "rhino", "results", t, "candidates");
                Directory.CreateDirectory(cand);
                File.WriteAllBytes(Path.Combine(cand, "shot.png"), new byte[] { 1, 2, 3 });
            }

            var exit = ApproveCommand.ApproveInner(workload: "rhino", test: null, suite: "mini");
            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(tmp, "workloads", "rhino", "results", "t-alpha", "baselines", "shot.png")));
            Assert.True(File.Exists(Path.Combine(tmp, "workloads", "rhino", "results", "t-beta", "baselines", "shot.png")));
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }

        static string CreateTree(string root)
        {
            Directory.CreateDirectory(root);
            return root;
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
