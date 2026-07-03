using Canary.Agent;
using Canary.Session;
using Canary.Telemetry;
using Xunit;

namespace Canary.Tests.Session;

[Trait("Category", "Unit")]
public class SupervisedSessionTests
{
    private sealed class StubAgent : ICanaryAgent
    {
        public int CaptureCount;
        public Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters)
            => Task.FromResult(new AgentResponse { Success = true });
        public Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings)
        {
            CaptureCount++;
            Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath)!);
            File.WriteAllBytes(settings.OutputPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            return Task.FromResult(new ScreenshotResult { FilePath = settings.OutputPath, Width = 100, Height = 100, CapturedAt = DateTime.UtcNow });
        }
        public Task<HeartbeatResult> HeartbeatAsync() => Task.FromResult(new HeartbeatResult { Ok = true });
        public Task AbortAsync() => Task.CompletedTask;
    }

    private sealed class StubFactory : ISessionAgentFactory
    {
        public StubAgent Agent { get; } = new();
        public Task<SessionAgentBundle> CreateAndInitializeAsync(string _, ITelemetrySink __, CancellationToken ___)
            => Task.FromResult(new SessionAgentBundle { Agent = Agent, Url = "http://stub/" });
    }

    [Fact]
    public async Task EndToEnd_StartCaptureEnd_WritesReportAndCaptures()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-session-itest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var factory = new StubFactory();
            var session = await SupervisedSession.StartAsync(root, "qualia", "(unused)", factory);
            var c1 = await session.CaptureAsync();
            var c2 = await session.CaptureAsync("Landing screen", "broken layout");
            await session.EndAsync("two issues found");
            await session.DisposeAsync();

            Assert.Equal(2, factory.Agent.CaptureCount);
            Assert.Equal(2, session.Captures.Count);
            Assert.True(session.IsEnded);

            Assert.True(File.Exists(SessionPaths.ReportPath(session.Directory)));
            Assert.True(File.Exists(SessionPaths.SessionJsonPath(session.Directory)));
            Assert.True(File.Exists(SessionPaths.TelemetryPath(session.Directory)));
            Assert.True(File.Exists(c1.PngPath));
            Assert.True(File.Exists(c2.PngPath));

            var report = File.ReadAllText(SessionPaths.ReportPath(session.Directory));
            Assert.Contains("two issues found", report);
            Assert.Contains("landing-screen", report);
            Assert.Contains("> broken layout", report);
            Assert.Contains("workload: qualia", report);

            var ndjson = File.ReadAllText(SessionPaths.TelemetryPath(session.Directory));
            Assert.Contains("\"Screenshot\"", ndjson);
            Assert.Contains("session ended", ndjson);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureAfterEnd_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-session-itest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var session = await SupervisedSession.StartAsync(root, "qualia", "(unused)", new StubFactory());
            await session.EndAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.CaptureAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeWithoutEnd_StillWritesReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-session-itest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var session = await SupervisedSession.StartAsync(root, "qualia", "(unused)", new StubFactory());
            await session.CaptureAsync();
            await session.DisposeAsync();
            Assert.True(File.Exists(SessionPaths.ReportPath(session.Directory)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AttachAnnotation_UpdatesCaptureRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-session-itest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var session = await SupervisedSession.StartAsync(root, "qualia", "(unused)", new StubFactory());
            var c = await session.CaptureAsync("foo");
            session.AttachAnnotation(c.Sequence, "001-foo.annotated.png", "001-foo.annotations.json");
            Assert.Equal("001-foo.annotated.png", session.Captures[0].AnnotatedPngFile);
            // Flight-recorder fields must SURVIVE the annotation rebuild (review finding: the
            // rebuild silently wiped ActiveView/FrameStatus for exactly the annotated captures).
            Assert.Equal(c.Sequence, session.Captures[0].Sequence);
            await session.EndAsync();
            var report = File.ReadAllText(SessionPaths.ReportPath(session.Directory));
            Assert.Contains("annotatedCount: 1", report);
            Assert.Contains("001-foo.annotated.png", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
