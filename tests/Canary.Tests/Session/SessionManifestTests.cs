using Canary.Agent;
using Canary.Session;
using Canary.Telemetry;
using Xunit;

namespace Canary.Tests.Session;

[Trait("Category", "Unit")]
public class SessionManifestTests
{
    [Fact]
    public void SessionPaths_ManifestAndPriorTelemetry_ComposeUnderSessionDir()
    {
        Assert.Equal(Path.Combine(@"C:\x\s1", "manifest.json"), SessionPaths.ManifestPath(@"C:\x\s1"));
        Assert.Equal(Path.Combine(@"C:\x\s1", "telemetry-prior.ndjson"), SessionPaths.TelemetryPriorPath(@"C:\x\s1"));
    }

    [Fact]
    public void ManifestWriter_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "canary-manifest-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manifest = new SessionManifest
            {
                SessionId = "20260702-120000-ab12",
                Workload = "rhino",
                SessionRef = "20260702-120000-ab12",
                OpenedFile = @"C:\models\phase6-explorer.3dm",
                OpenedFileSha256 = "deadbeef",
                MachineName = "DESKTOP",
                AppPath = @"C:\Program Files\Rhino 8\System\Rhino.exe",
                ProcessId = 4242,
                AppliedEnv = new Dictionary<string, string> { ["PENUMBRA_SESSION_REF"] = "20260702-120000-ab12" },
                StartedAtUtc = new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc),
                EndedAtUtc = new DateTime(2026, 7, 2, 12, 30, 0, DateTimeKind.Utc),
                CaptureCount = 2,
                ProcessExitCode = 1,
                ExitedBeforeCloseout = true,
                DiedUnexpectedly = true,
                Harvested = new Dictionary<string, string> { ["pluginGitSha"] = "f78f358" },
            };
            SessionManifestWriter.Write(dir, manifest);
            Assert.True(File.Exists(SessionPaths.ManifestPath(dir)));

            var read = SessionManifestWriter.TryRead(dir);
            Assert.NotNull(read);
            Assert.Equal("20260702-120000-ab12", read!.SessionId);
            Assert.Equal(@"C:\models\phase6-explorer.3dm", read.OpenedFile);
            Assert.Equal(4242, read.ProcessId);
            Assert.True(read.DiedUnexpectedly);
            Assert.Equal("f78f358", read.Harvested!["pluginGitSha"]);
            Assert.Equal("20260702-120000-ab12", read.AppliedEnv!["PENUMBRA_SESSION_REF"]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void HarvestFromTelemetry_LiftsPenumbraIdentityFields_LaterRecordsWin()
    {
        var path = Path.Combine(Path.GetTempPath(), "canary-harvest-test-" + Guid.NewGuid().ToString("N") + ".ndjson");
        try
        {
            // Shape matches PenumbraPreviewTelemetryTail's wrapping: source=penumbra,
            // data.event = domain kind, data.payload = the original Penumbra payload.
            File.WriteAllLines(path, new[]
            {
                """{"t":"2026-07-02T12:00:00Z","kind":"Log","source":"penumbra","data":{"event":"gl.scene.loaded","payload":{"atoms":3}}}""",
                """{"t":"2026-07-02T12:00:01Z","kind":"Log","source":"penumbra","data":{"event":"penumbra.startup-diagnostics","payload":{"pluginGitSha":"aaaa111","bundleGitSha":"bbbb222","skewVerdict":"clean","unrelated":"x"}}}""",
                "this is not json at all",
                """{"t":"2026-07-02T12:00:02Z","kind":"Log","source":"canary-session","data":{"event":"penumbra.startup-diagnostics","payload":{"pluginGitSha":"WRONG-SOURCE"}}}""",
                """{"t":"2026-07-02T12:00:03Z","kind":"Log","source":"penumbra","data":{"event":"penumbra.startup-diagnostics","payload":{"pluginGitSha":"cccc333"}}}""",
                """{"t":"2026-07-02T12:00:04Z","kind":"Log","source":"penumbra","data":{"event":"gl.build.probe","payload":{"renderer":"Intel(R) Arc(TM) 140V GPU","durationMs":12}}}""",
            });

            var harvested = SessionManifestWriter.HarvestFromTelemetry(path);

            Assert.Equal("cccc333", harvested["pluginGitSha"]);   // later record won
            Assert.Equal("bbbb222", harvested["bundleGitSha"]);
            Assert.Equal("clean", harvested["skewVerdict"]);
            Assert.Equal("Intel(R) Arc(TM) 140V GPU", harvested["renderer"]);
            Assert.False(harvested.ContainsKey("unrelated"));
            Assert.False(harvested.ContainsKey("atoms"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void HarvestFromTelemetry_MissingFile_ReturnsEmpty()
    {
        var harvested = SessionManifestWriter.HarvestFromTelemetry(
            Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".ndjson"));
        Assert.Empty(harvested);
    }

    [Fact]
    public void TelemetryRescue_CopiesPrimaryAndPrev_AndPruneKeepsNewest()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-rescue-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var src = Path.Combine(root, "telemetry.ndjson");
            File.WriteAllText(src, "{\"kind\":\"x\"}\n");
            File.WriteAllText(src + ".prev", "{\"kind\":\"old\"}\n");

            var destDir = Path.Combine(root, "rescued");
            var dest = Path.Combine(destDir, "telemetry-prior.ndjson");
            Assert.True(PenumbraTelemetryRescue.TryRescueTo(dest, sourcePath: src));
            Assert.True(File.Exists(dest));
            Assert.True(File.Exists(Path.Combine(destDir, "telemetry-prior.prev.ndjson")));

            // Prune: create 13 primaries (+1 prev sibling each), keep 10.
            var pruneDir = Path.Combine(root, "prune");
            Directory.CreateDirectory(pruneDir);
            for (int i = 0; i < 13; i++)
            {
                var f = Path.Combine(pruneDir, $"file{i:D2}.ndjson");
                File.WriteAllText(f, "x");
                File.WriteAllText(Path.Combine(pruneDir, $"file{i:D2}.prev.ndjson"), "y");
                File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(-100 + i));
            }
            PenumbraTelemetryRescue.Prune(pruneDir, keep: 10);
            var primaries = Directory.GetFiles(pruneDir, "*.ndjson")
                .Where(f => !f.EndsWith(".prev.ndjson", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Equal(10, primaries.Count);
            // The three OLDEST were pruned (file00..file02), together with their .prev siblings.
            Assert.DoesNotContain(primaries, f => f.EndsWith("file00.ndjson"));
            Assert.False(File.Exists(Path.Combine(pruneDir, "file00.prev.ndjson")));
            Assert.Contains(primaries, f => f.EndsWith("file12.ndjson"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ExitingStubAgent : ICanaryAgent, IProcessBackedAgent
    {
        public Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters)
            => Task.FromResult(action == "GetPenumbraFrameState"
                ? new AgentResponse
                {
                    Success = true,
                    Data = new Dictionary<string, string>
                    {
                        ["bridge"] = "ok",
                        ["realRevision"] = "7",
                        ["status"] = "scene steady q=100% steps=192",
                        ["activeView"] = "Perspective",
                        ["views"] = "Perspective;Top;Front;Right",
                    },
                }
                : new AgentResponse { Success = true });

        public Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settings.OutputPath)!);
            File.WriteAllBytes(settings.OutputPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            return Task.FromResult(new ScreenshotResult { FilePath = settings.OutputPath, Width = 8, Height = 8, CapturedAt = DateTime.UtcNow });
        }

        public Task<HeartbeatResult> HeartbeatAsync() => Task.FromResult(new HeartbeatResult { Ok = true });
        public Task AbortAsync() => Task.CompletedTask;

        public int ProcessId => 999;
        public bool ProcessHasExited => true;
        public bool TryGetProcessExit(out int exitCode, out DateTime exitUtc)
        {
            exitCode = 1;
            exitUtc = DateTime.UtcNow;
            return true;
        }
    }

    private sealed class ExitingStubFactory : ISessionAgentFactory
    {
        public Task<SessionAgentBundle> CreateAndInitializeAsync(string _, Canary.Telemetry.ITelemetrySink __, CancellationToken ___)
            => Task.FromResult(new SessionAgentBundle
            {
                Agent = new ExitingStubAgent(),
                Url = null,
                Launch = new SessionLaunchInfo
                {
                    AppPath = @"C:\fake\rhino.exe",
                    ProcessId = 999,
                    AppliedEnv = new Dictionary<string, string> { ["PENUMBRA_SESSION_REF"] = "stamped" },
                },
            });
    }

    [Fact]
    public async Task RhinoSession_WritesManifest_WithDeathCertificate_AndCaptureFrameState()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-session-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var openMe = Path.Combine(root, "model.3dm");
            File.WriteAllText(openMe, "fake 3dm bytes");

            var session = await SupervisedSession.StartAsync(
                root, "rhino", "(unused)", new ExitingStubFactory(),
                new SessionStartOptions { OpenFilePath = openMe });

            // Manifest v0 exists at start.
            Assert.True(File.Exists(SessionPaths.ManifestPath(session.Directory)));

            // The process "died" before close-out → REPL notice is non-null pre-End.
            Assert.NotNull(session.ProcessExitNotice);

            var cap = await session.CaptureAsync();
            // Annotation rebuild must PRESERVE the flight-recorder fields (review finding).
            session.AttachAnnotation(cap.Sequence, "001-x.annotated.png", "001-x.annotations.json");
            Assert.Equal("Perspective", session.Captures[0].ActiveView);
            Assert.NotNull(session.Captures[0].FrameStatus);
            await session.EndAsync("done");
            await session.DisposeAsync();

            var manifest = SessionManifestWriter.TryRead(session.Directory);
            Assert.NotNull(manifest);
            // SessionRef records what the launcher ACTUALLY injected (from AppliedEnv), not an
            // assumed value — for real rhino sessions these coincide (ref = session id).
            Assert.Equal("stamped", manifest!.SessionRef);
            Assert.Equal(openMe, manifest.OpenedFile);
            Assert.NotNull(manifest.OpenedFileSha256);
            Assert.Equal(999, manifest.ProcessId);
            Assert.True(manifest.ExitedBeforeCloseout);
            Assert.True(manifest.DiedUnexpectedly);   // exit code 1
            Assert.Equal(1, manifest.CaptureCount);

            // Capture carried the frame state into the session data + report.
            Assert.Equal("Perspective", session.Captures[0].ActiveView);
            Assert.Contains("steady", session.Captures[0].FrameStatus);
            var report = File.ReadAllText(SessionPaths.ReportPath(session.Directory));
            Assert.Contains("view=Perspective", report);
            Assert.Contains("Session manifest", report);
            Assert.Contains("Debug pointers", report);
            var ndjson = File.ReadAllText(SessionPaths.TelemetryPath(session.Directory));
            Assert.Contains("frameStateBefore", ndjson);
            Assert.Contains("opened file", ndjson);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
