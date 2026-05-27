using Canary.Session;
using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class SessionsPastViewModelTests
{
    [Fact]
    public void ScanRows_ReturnsEmpty_WhenWorkloadsDirMissing()
    {
        var rows = SessionsPastViewModel.ScanRows(null);
        Assert.Empty(rows);
        rows = SessionsPastViewModel.ScanRows("/this/path/does/not/exist");
        Assert.Empty(rows);
    }

    [Fact]
    public void ScanRows_FindsSessionsAcrossWorkloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-avalonia-past-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteSessionFixture(root, "qualia", "20260527-120000-aaaa", DateTime.UtcNow.AddHours(-2), captures: 2);
            WriteSessionFixture(root, "penumbra", "20260527-130000-bbbb", DateTime.UtcNow.AddHours(-1), captures: 0);

            var rows = SessionsPastViewModel.ScanRows(root);
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.Workload == "qualia" && r.SessionId == "20260527-120000-aaaa" && r.Captures == 2);
            Assert.Contains(rows, r => r.Workload == "penumbra" && r.SessionId == "20260527-130000-bbbb" && r.Captures == 0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Filter_NarrowsRowsByWorkloadOrSessionId()
    {
        var vm = new SessionsPastViewModel();
        var root = Path.Combine(Path.GetTempPath(), "canary-avalonia-past-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteSessionFixture(root, "qualia", "20260527-120000-aaaa", DateTime.UtcNow.AddHours(-2), captures: 1);
            WriteSessionFixture(root, "penumbra", "20260527-130000-bbbb", DateTime.UtcNow.AddHours(-1), captures: 1);

            vm.SetWorkloadsDir(root);
            Assert.Equal(2, vm.Rows.Count);

            vm.Filter = "qualia";
            Assert.Single(vm.Rows);
            Assert.Equal("qualia", vm.Rows[0].Workload);

            vm.Filter = "bbbb";
            Assert.Single(vm.Rows);
            Assert.Equal("penumbra", vm.Rows[0].Workload);

            vm.Filter = "";
            Assert.Equal(2, vm.Rows.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteSessionFixture(string root, string workload, string sessionId, DateTime startedUtc, int captures)
    {
        var sessionDir = SessionPaths.SessionDir(root, workload, sessionId);
        Directory.CreateDirectory(sessionDir);
        var data = new SessionData
        {
            SessionId = sessionId,
            Workload = workload,
            StartedAtUtc = startedUtc,
            EndedAtUtc = startedUtc.AddMinutes(5),
            Captures = Enumerable.Range(1, captures).Select(i => new SessionCapture
            {
                Sequence = i,
                CapturedAtUtc = startedUtc.AddMinutes(i),
                PngFile = $"{i:D3}.png",
            }).ToList(),
        };
        SessionReportWriter.Write(sessionDir, data);
    }
}
