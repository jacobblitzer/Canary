using System.Text;
using Canary.Agent;
using Canary.Telemetry;

namespace Canary.Session;

public sealed class SupervisedSession : IAsyncDisposable
{
    public string SessionId { get; }
    public string Workload { get; }
    public string Directory { get; }
    public string? Url { get; set; }

    private readonly ICanaryAgent _agent;
    private readonly NdjsonFileSink _telemetrySink;
    private readonly DateTime _startedAt;
    private readonly SessionLaunchInfo? _launchInfo;
    private DateTime? _endedAt;
    private readonly List<SessionCapture> _captures = new();
    private string? _closeoutNotes;
    private string? _openedFile;
    private string? _openedFileSha256;
    private bool _ended;
    private bool _disposed;
    private int _sequence;

    public IReadOnlyList<SessionCapture> Captures => _captures;
    public bool IsEnded => _ended;

    /// <summary>Non-null once the spawned app process has exited while the session is still open —
    /// the flight-recorder death notice the REPL prints (kills + native crashes fire no in-process
    /// hooks, so the survivor-side watch is the only reliable signal).</summary>
    public string? ProcessExitNotice =>
        !_ended && _agent is IProcessBackedAgent pb && pb.TryGetProcessExit(out var code, out var at)
            ? $"{Workload} app exited (code {code}) at {at:HH:mm:ss}Z — evidence preserved; press q to close out."
            : null;

    private bool IsRhino => string.Equals(Workload, "rhino", StringComparison.OrdinalIgnoreCase);

    private SupervisedSession(
        string sessionId,
        string workload,
        string directory,
        ICanaryAgent agent,
        NdjsonFileSink telemetrySink,
        DateTime startedAt,
        string? url,
        SessionLaunchInfo? launchInfo)
    {
        SessionId = sessionId;
        Workload = workload;
        Directory = directory;
        _agent = agent;
        _telemetrySink = telemetrySink;
        _startedAt = startedAt;
        Url = url;
        _launchInfo = launchInfo;
    }

    public static Task<SupervisedSession> StartAsync(
        string workloadsDir,
        string workload,
        string workloadConfigPath,
        ISessionAgentFactory agentFactory,
        CancellationToken ct = default)
        => StartAsync(workloadsDir, workload, workloadConfigPath, agentFactory, options: null, ct);

    public static async Task<SupervisedSession> StartAsync(
        string workloadsDir,
        string workload,
        string workloadConfigPath,
        ISessionAgentFactory agentFactory,
        SessionStartOptions? options,
        CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var sessionId = SessionPaths.GenerateSessionId(startedAt);
        var sessionDir = SessionPaths.SessionDir(workloadsDir, workload, sessionId);
        System.IO.Directory.CreateDirectory(sessionDir);
        System.IO.Directory.CreateDirectory(SessionPaths.CapturesDir(sessionDir));

        // Rescue the PREVIOUS session's global Penumbra telemetry into this session dir before
        // our Rhino spawn truncates it (the plug-in re-creates the file on every plugin load).
        if (string.Equals(workload, "rhino", StringComparison.OrdinalIgnoreCase))
        {
            PenumbraTelemetryRescue.TryRescueTo(SessionPaths.TelemetryPriorPath(sessionDir));
        }

        var telemetryPath = SessionPaths.TelemetryPath(sessionDir);
        var telemetrySink = new NdjsonFileSink(telemetryPath);

        SessionAgentBundle bundle;
        try
        {
            bundle = await agentFactory.CreateAndInitializeAsync(
                workloadConfigPath, telemetrySink,
                new SessionLaunchContext { SessionRef = sessionId }, ct).ConfigureAwait(false);
        }
        catch
        {
            telemetrySink.Dispose();
            throw;
        }

        var session = new SupervisedSession(
            sessionId, workload, sessionDir, bundle.Agent, telemetrySink, startedAt, bundle.Url, bundle.Launch);

        // Manifest v0 BEFORE the OpenFile dispatch — opening a heavy .3dm can take minutes and is
        // the highest-risk window; the session's identity must already be on disk if Rhino dies there.
        try { SessionManifestWriter.Write(sessionDir, session.BuildManifest(finalized: false)); } catch { }

        if (!string.IsNullOrEmpty(options?.OpenFilePath))
        {
            var file = options!.OpenFilePath!;
            AgentResponse resp;
            try
            {
                resp = await bundle.Agent.ExecuteAsync(
                    "OpenFile", new Dictionary<string, string> { ["path"] = file }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"OpenFile dispatch failed for '{file}': {ex.Message}", ex);
            }
            if (!resp.Success)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"Failed to open '{file}': {resp.Message}");
            }
            session._openedFile = file;
            session._openedFileSha256 = TryComputeSha256(file);
            try
            {
                telemetrySink.Write(new TelemetryRecord
                {
                    T = DateTime.UtcNow,
                    RunId = sessionId,
                    Kind = TelemetryKind.Log,
                    Source = "canary-session",
                    Data = new { text = "opened file", path = file, sha256 = session._openedFileSha256 },
                });
            }
            catch { }

            // Re-write the manifest now that openedFile + sha256 are known.
            try { SessionManifestWriter.Write(sessionDir, session.BuildManifest(finalized: false)); } catch { }
        }

        return session;
    }

    private static string? TryComputeSha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch { return null; }
    }

    public async Task<CaptureResult> CaptureAsync(
        string? noteTitle = null,
        string? noteBody = null,
        CancellationToken ct = default)
    {
        if (_ended) throw new InvalidOperationException("Session already ended.");

        var now = DateTime.UtcNow;
        var seq = Interlocked.Increment(ref _sequence);
        var slug = CaptureSlugGenerator.FromTitle(noteTitle);
        var pngFile = SessionPaths.CapturePngFile(seq, now, slug);
        var pngPath = Path.Combine(SessionPaths.CapturesDir(Directory), pngFile);

        // Flight-recorder capture markers: sample the Penumbra frame state immediately BEFORE and
        // AFTER the pixel grab. A capture can race progressive refinement — the pair + changed
        // flag says whether the pixels belong to the sampled state (TOCTOU guard). Best-effort:
        // null when the agent predates GetPenumbraFrameState or the Bridge isn't loaded.
        Dictionary<string, string>? frameBefore = IsRhino ? await TryGetFrameStateAsync().ConfigureAwait(false) : null;

        await _agent.CaptureScreenshotAsync(new CaptureSettings { OutputPath = pngPath }).ConfigureAwait(false);

        Dictionary<string, string>? frameAfter = IsRhino ? await TryGetFrameStateAsync().ConfigureAwait(false) : null;
        bool? stateChanged = frameBefore != null && frameAfter != null
            ? FrameStateDiffers(frameBefore, frameAfter)
            : null;

        if (!string.IsNullOrWhiteSpace(noteTitle) || !string.IsNullOrWhiteSpace(noteBody))
        {
            var noteFile = SessionPaths.CaptureMarkdownFile(seq, now, slug);
            var notePath = Path.Combine(SessionPaths.CapturesDir(Directory), noteFile);
            var sb = new StringBuilder();
            sb.AppendLine($"# {noteTitle ?? "(untitled)"}");
            sb.AppendLine();
            sb.AppendLine(noteBody ?? string.Empty);
            File.WriteAllText(notePath, sb.ToString(), Encoding.UTF8);
        }

        var capture = new SessionCapture
        {
            Sequence = seq,
            CapturedAtUtc = now,
            Slug = slug,
            PngFile = pngFile,
            NoteTitle = noteTitle,
            NoteBody = noteBody,
            ActiveView = Get(frameAfter, "activeView") ?? Get(frameBefore, "activeView"),
            FrameStatus = Get(frameAfter, "status"),
        };
        _captures.Add(capture);

        _telemetrySink.Write(new TelemetryRecord
        {
            T = now,
            RunId = SessionId,
            Kind = TelemetryKind.Screenshot,
            Source = "canary-session",
            Data = new
            {
                path = pngPath,
                slug,
                sequence = seq,
                noteTitle,
                noteBody,
                frameStateBefore = frameBefore,
                frameStateAfter = frameAfter,
                stateChanged,
            },
        });

        return new CaptureResult { Sequence = seq, PngPath = pngPath, Slug = slug };
    }

    private async Task<Dictionary<string, string>?> TryGetFrameStateAsync()
    {
        try
        {
            var resp = await _agent.ExecuteAsync(
                "GetPenumbraFrameState", new Dictionary<string, string>()).ConfigureAwait(false);
            // An agent predating the action answers Success=false with empty Data — treat as absent.
            return resp.Data is { Count: > 0 } ? resp.Data : null;
        }
        catch { return null; }
    }

    private static string? Get(Dictionary<string, string>? d, string key)
        => d != null && d.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;

    private static bool FrameStateDiffers(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        foreach (var key in new[] { "realRevision", "presentedRevision", "status", "activeView" })
        {
            if (!string.Equals(Get2(a, key), Get2(b, key), StringComparison.Ordinal)) return true;
        }
        return false;

        static string? Get2(Dictionary<string, string> d, string k)
            => d.TryGetValue(k, out var v) ? v : null;
    }

    public void AttachAnnotation(int sequence, string annotatedPngFile, string annotationsJsonFile)
    {
        var c = _captures.FirstOrDefault(x => x.Sequence == sequence);
        if (c == null) return;
        var idx = _captures.IndexOf(c);
        _captures[idx] = new SessionCapture
        {
            Sequence = c.Sequence,
            CapturedAtUtc = c.CapturedAtUtc,
            Slug = c.Slug,
            PngFile = c.PngFile,
            AnnotatedPngFile = annotatedPngFile,
            AnnotationsJsonFile = annotationsJsonFile,
            NoteTitle = c.NoteTitle,
            NoteBody = c.NoteBody,
            ActiveView = c.ActiveView,
            FrameStatus = c.FrameStatus,
        };
    }

    public async Task EndAsync(string? closeoutNotes = null, CancellationToken ct = default)
    {
        if (_ended) return;
        _ended = true;
        _endedAt = DateTime.UtcNow;
        _closeoutNotes = closeoutNotes;

        try { await _agent.AbortAsync().ConfigureAwait(false); } catch { }

        try
        {
            _telemetrySink.Write(new TelemetryRecord
            {
                T = _endedAt.Value,
                RunId = SessionId,
                Kind = TelemetryKind.Log,
                Source = "canary-session",
                Data = new { text = "session ended", captureCount = _captures.Count },
            });
        }
        catch { }

        _telemetrySink.Dispose();
        WriteReport();
    }

    private void WriteReport()
    {
        var data = new SessionData
        {
            SessionId = SessionId,
            Workload = Workload,
            Url = Url,
            StartedAtUtc = _startedAt,
            EndedAtUtc = _endedAt,
            CloseoutNotes = _closeoutNotes,
            OpenedFile = _openedFile,
            Captures = _captures.ToList(),
        };
        SessionManifest? manifest = null;
        try { manifest = BuildManifest(finalized: true); } catch { }
        SessionReportWriter.Write(Directory, data, manifest);
    }

    private SessionManifest BuildManifest(bool finalized)
    {
        // SessionRef is only real when a launch actually injected it into the child env —
        // claiming it for CDP sessions would send a debugging agent grepping for a ref that
        // never reached any process.
        string? sessionRef = null;
        if (_launchInfo?.AppliedEnv != null &&
            _launchInfo.AppliedEnv.TryGetValue("PENUMBRA_SESSION_REF", out var injectedRef))
        {
            sessionRef = injectedRef;
        }
        var asm = typeof(SupervisedSession).Assembly;
        var infoVersion = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(asm)
            ?.InformationalVersion;
        var m = new SessionManifest
        {
            SessionId = SessionId,
            Workload = Workload,
            SessionRef = sessionRef,
            OpenedFile = _openedFile,
            OpenedFileSha256 = _openedFileSha256,
            MachineName = Environment.MachineName,
            CanaryVersion = infoVersion ?? asm.GetName().Version?.ToString(),
            AppPath = _launchInfo?.AppPath,
            ProcessId = _launchInfo?.ProcessId,
            AppliedEnv = _launchInfo?.AppliedEnv,
            StartedAtUtc = _startedAt,
        };
        if (!finalized) return m;

        m.EndedAtUtc = _endedAt;
        m.CaptureCount = _captures.Count;
        // Death certificate: EndAsync runs BEFORE DisposeAsync kills the process, so an exit
        // visible here means the app died (or was closed) while the session was still open.
        // Clean operator close = exit 0; kill/crash = non-zero → DiedUnexpectedly.
        if (_agent is IProcessBackedAgent pb && pb.TryGetProcessExit(out var exitCode, out var exitUtc))
        {
            m.ProcessExitCode = exitCode;
            m.ProcessExitAtUtc = exitUtc;
            m.ExitedBeforeCloseout = true;
            m.DiedUnexpectedly = exitCode != 0;
        }
        try
        {
            var harvested = SessionManifestWriter.HarvestFromTelemetry(SessionPaths.TelemetryPath(Directory));
            if (harvested.Count > 0) m.Harvested = harvested;
        }
        catch { }
        return m;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_ended)
        {
            try { await EndAsync().ConfigureAwait(false); } catch { }
        }
        // Async teardown FIRST: RhinoSessionAgent is IAsyncDisposable-only (kills Rhino + node
        // children + pipe client + telemetry tail) — the IDisposable-only check below never
        // reached it, leaking a live Rhino on session dispose (pre-existing; found by the
        // flight-recorder Phase A review when the OpenFile-failure path started relying on it).
        if (_agent is IAsyncDisposable ad)
        {
            try { await ad.DisposeAsync().ConfigureAwait(false); } catch { }
        }
        else if (_agent is IDisposable d)
        {
            try { d.Dispose(); } catch { }
        }
    }
}
