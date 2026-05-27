using Canary.Session;
using Xunit;

namespace Canary.Tests.Session;

[Trait("Category", "Unit")]
public class SessionPathsTests
{
    [Fact]
    public void GenerateSessionId_HasExpectedShape()
    {
        var id = SessionPaths.GenerateSessionId(new DateTime(2026, 5, 27, 14, 30, 22, DateTimeKind.Utc));
        Assert.Matches("^[0-9]{8}-[0-9]{6}-[0-9a-f]{4}$", id);
        Assert.StartsWith("20260527-143022-", id);
    }

    [Fact]
    public void GenerateSessionId_TwoCallsDiffer()
    {
        var a = SessionPaths.GenerateSessionId(DateTime.UtcNow);
        var b = SessionPaths.GenerateSessionId(DateTime.UtcNow);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SessionDir_ComposesWorkloadsWorkloadSessionsId()
    {
        var dir = SessionPaths.SessionDir(@"C:\w", "qualia", "20260527-143022-a3f1");
        Assert.Equal(Path.Combine(@"C:\w", "qualia", "sessions", "20260527-143022-a3f1"), dir);
    }

    [Theory]
    [InlineData(1, "landing", "001-14-30-22-landing.png")]
    [InlineData(42, null, "042-14-30-22.png")]
    [InlineData(7, "", "007-14-30-22.png")]
    public void CapturePngFile_FormatsCorrectly(int seq, string? slug, string expected)
    {
        var ts = new DateTime(2026, 5, 27, 14, 30, 22, DateTimeKind.Utc);
        Assert.Equal(expected, SessionPaths.CapturePngFile(seq, ts, slug));
    }

    [Fact]
    public void CaptureAnnotatedPngFile_HasAnnotatedSuffix()
    {
        var ts = new DateTime(2026, 5, 27, 14, 30, 22, DateTimeKind.Utc);
        Assert.Equal("003-14-30-22-foo.annotated.png", SessionPaths.CaptureAnnotatedPngFile(3, ts, "foo"));
    }

    [Fact]
    public void TelemetryPath_LivesInsideSessionDir()
    {
        var t = SessionPaths.TelemetryPath(@"C:\w\qualia\sessions\abc");
        Assert.Equal(Path.Combine(@"C:\w\qualia\sessions\abc", "telemetry.ndjson"), t);
    }
}
