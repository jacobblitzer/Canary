using Canary.Session;
using Xunit;

namespace Canary.Tests.Session;

[Trait("Category", "Unit")]
public class SessionReportWriterTests
{
    [Fact]
    public void BuildMarkdown_FrontmatterContainsRequiredFields()
    {
        var session = new SessionData
        {
            SessionId = "20260527-143022-a3f1",
            Workload = "qualia",
            Url = "http://localhost:5173/",
            StartedAtUtc = new DateTime(2026, 5, 27, 14, 30, 22, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 5, 27, 15, 12, 8, DateTimeKind.Utc),
        };
        var md = SessionReportWriter.BuildMarkdown(session);
        Assert.Contains("sessionId: 20260527-143022-a3f1", md);
        Assert.Contains("workload: qualia", md);
        Assert.Contains("url: http://localhost:5173/", md);
        Assert.Contains("captureCount: 0", md);
        Assert.Contains("annotatedCount: 0", md);
        Assert.Contains("# Supervised session — qualia", md);
        Assert.Contains("_(no captures)_", md);
    }

    [Fact]
    public void BuildMarkdown_EmbedsCaptureRows()
    {
        var session = new SessionData
        {
            SessionId = "20260527-143022-a3f1",
            Workload = "qualia",
            StartedAtUtc = new DateTime(2026, 5, 27, 14, 30, 22, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 5, 27, 14, 35, 0, DateTimeKind.Utc),
            Captures =
            {
                new SessionCapture
                {
                    Sequence = 1,
                    CapturedAtUtc = new DateTime(2026, 5, 27, 14, 31, 45, DateTimeKind.Utc),
                    Slug = "landing-screen-overflow",
                    PngFile = "001-14-31-45-landing-screen-overflow.png",
                    AnnotatedPngFile = "001-14-31-45-landing-screen-overflow.annotated.png",
                    AnnotationsJsonFile = "001-14-31-45-landing-screen-overflow.annotations.json",
                    NoteBody = "broken layout on small viewport",
                },
                new SessionCapture
                {
                    Sequence = 2,
                    CapturedAtUtc = new DateTime(2026, 5, 27, 14, 33, 11, DateTimeKind.Utc),
                    PngFile = "002-14-33-11.png",
                },
            },
        };
        var md = SessionReportWriter.BuildMarkdown(session);
        Assert.Contains("### 001 — 14:31:45 — landing-screen-overflow", md);
        Assert.Contains("![](captures/001-14-31-45-landing-screen-overflow.annotated.png)", md);
        Assert.Contains("> broken layout on small viewport", md);
        Assert.Contains("[annotated PNG](captures/001-14-31-45-landing-screen-overflow.annotated.png)", md);
        Assert.Contains("[annotations.json](captures/001-14-31-45-landing-screen-overflow.annotations.json)", md);
        Assert.Contains("### 002 — 14:33:11 — (no annotation)", md);
        Assert.Contains("![](captures/002-14-33-11.png)", md);
        Assert.Contains("captureCount: 2", md);
        Assert.Contains("annotatedCount: 1", md);
    }

    [Fact]
    public void BuildMarkdown_IncludesCloseoutNotesWhenPresent()
    {
        var session = new SessionData
        {
            SessionId = "id",
            Workload = "qualia",
            StartedAtUtc = DateTime.UtcNow,
            EndedAtUtc = DateTime.UtcNow,
            CloseoutNotes = "Found regression in landing carousel",
        };
        var md = SessionReportWriter.BuildMarkdown(session);
        Assert.Contains("Found regression in landing carousel", md);
        Assert.DoesNotContain("_(none)_", md);
    }

    [Fact]
    public void Write_CreatesBothFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "canary-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new SessionData
            {
                SessionId = "test-id",
                Workload = "qualia",
                StartedAtUtc = DateTime.UtcNow,
            };
            SessionReportWriter.Write(dir, session);
            Assert.True(File.Exists(Path.Combine(dir, "SESSION_REPORT.md")));
            Assert.True(File.Exists(Path.Combine(dir, "session.json")));
            var json = File.ReadAllText(Path.Combine(dir, "session.json"));
            Assert.Contains("\"sessionId\"", json);
            Assert.Contains("\"workload\"", json);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryReadJson_RoundTripsSessionData()
    {
        var dir = Path.Combine(Path.GetTempPath(), "canary-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = new SessionData
            {
                SessionId = "20260527-143022-a3f1",
                Workload = "qualia",
                Url = "http://localhost:5173/",
                StartedAtUtc = new DateTime(2026, 5, 27, 14, 30, 22, DateTimeKind.Utc),
                EndedAtUtc = new DateTime(2026, 5, 27, 14, 35, 0, DateTimeKind.Utc),
                CloseoutNotes = "ok",
                Captures =
                {
                    new SessionCapture
                    {
                        Sequence = 1,
                        CapturedAtUtc = new DateTime(2026, 5, 27, 14, 31, 45, DateTimeKind.Utc),
                        Slug = "foo",
                        PngFile = "001-14-31-45-foo.png",
                    },
                },
            };
            SessionReportWriter.Write(dir, written);
            var read = SessionReportWriter.TryReadJson(dir);
            Assert.NotNull(read);
            Assert.Equal(written.SessionId, read!.SessionId);
            Assert.Equal(written.Workload, read.Workload);
            Assert.Equal(written.Url, read.Url);
            Assert.Single(read.Captures);
            Assert.Equal("foo", read.Captures[0].Slug);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
