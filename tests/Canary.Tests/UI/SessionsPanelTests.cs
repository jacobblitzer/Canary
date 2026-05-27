using System.Text.Json;
using Canary.Session;
using Canary.UI.Navigation;
using Canary.UI.Panels;
using Xunit;

namespace Canary.Tests.UI;

[Trait("Category", "Unit")]
public class SessionsPanelTests
{
    [Fact]
    public void SessionsNavMode_HasNameAndDescription()
    {
        var m = new SessionsNavMode();
        Assert.Equal("Sessions", m.Name);
        Assert.False(string.IsNullOrWhiteSpace(m.Description));
    }

    [Fact]
    public void SessionsNavMode_CreateContent_ReturnsSessionsPanel()
    {
        var m = new SessionsNavMode();
        var c = m.CreateContent();
        Assert.IsType<SessionsPanel>(c);
        Assert.Same(c, m.CreateContent());
    }

    [Fact]
    public void SessionsPanel_Construct_DoesNotThrow()
    {
        using var p = new SessionsPanel();
        Assert.NotNull(p);
    }

    [Fact]
    public void SessionsPastSubPanel_ScanRows_FromTempDirYieldsLoadedSessions()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-sessions-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var qualiaSessionsDir = Path.Combine(root, "qualia", SessionPaths.SessionsSubdir);
            var session1Dir = Path.Combine(qualiaSessionsDir, "20260527-100000-aaaa");
            var session2Dir = Path.Combine(qualiaSessionsDir, "20260527-150000-bbbb");
            Directory.CreateDirectory(session1Dir);
            Directory.CreateDirectory(session2Dir);

            WriteSessionJson(session1Dir, "20260527-100000-aaaa", "qualia", new DateTime(2026, 5, 27, 10, 0, 0, DateTimeKind.Utc), captures: 1);
            WriteSessionJson(session2Dir, "20260527-150000-bbbb", "qualia", new DateTime(2026, 5, 27, 15, 0, 0, DateTimeKind.Utc), captures: 3);

            var penumbraSessionDir = Path.Combine(root, "penumbra", SessionPaths.SessionsSubdir, "20260527-120000-cccc");
            Directory.CreateDirectory(penumbraSessionDir);
            WriteSessionJson(penumbraSessionDir, "20260527-120000-cccc", "penumbra", new DateTime(2026, 5, 27, 12, 0, 0, DateTimeKind.Utc), captures: 0);

            var rows = SessionsPastSubPanel.ScanRows(root);

            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, r => r.SessionId == "20260527-100000-aaaa" && r.Workload == "qualia" && r.Captures == 1);
            Assert.Contains(rows, r => r.SessionId == "20260527-150000-bbbb" && r.Workload == "qualia" && r.Captures == 3);
            Assert.Contains(rows, r => r.SessionId == "20260527-120000-cccc" && r.Workload == "penumbra" && r.Captures == 0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SessionsPastSubPanel_ScanRows_SkipsMissingSessionJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-sessions-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dirWithoutJson = Path.Combine(root, "qualia", SessionPaths.SessionsSubdir, "20260527-100000-aaaa");
            Directory.CreateDirectory(dirWithoutJson);
            var rows = SessionsPastSubPanel.ScanRows(root);
            Assert.Empty(rows);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SessionsPastSubPanel_ScanRows_NullOrMissingDirReturnsEmpty()
    {
        Assert.Empty(SessionsPastSubPanel.ScanRows(null));
        Assert.Empty(SessionsPastSubPanel.ScanRows(Path.Combine(Path.GetTempPath(), "definitely-not-a-real-dir-" + Guid.NewGuid().ToString("N"))));
    }

    private static void WriteSessionJson(string sessionDir, string id, string workload, DateTime startedUtc, int captures)
    {
        var data = new SessionData
        {
            SessionId = id,
            Workload = workload,
            StartedAtUtc = startedUtc,
        };
        for (var i = 1; i <= captures; i++)
        {
            data.Captures.Add(new SessionCapture
            {
                Sequence = i,
                CapturedAtUtc = startedUtc.AddSeconds(i * 30),
                PngFile = $"{i:D3}-{startedUtc:HH-mm-ss}.png",
            });
        }
        SessionReportWriter.Write(sessionDir, data);
    }
}
