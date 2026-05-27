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
    private DateTime? _endedAt;
    private readonly List<SessionCapture> _captures = new();
    private string? _closeoutNotes;
    private bool _ended;
    private bool _disposed;
    private int _sequence;

    public IReadOnlyList<SessionCapture> Captures => _captures;
    public bool IsEnded => _ended;

    private SupervisedSession(
        string sessionId,
        string workload,
        string directory,
        ICanaryAgent agent,
        NdjsonFileSink telemetrySink,
        DateTime startedAt,
        string? url)
    {
        SessionId = sessionId;
        Workload = workload;
        Directory = directory;
        _agent = agent;
        _telemetrySink = telemetrySink;
        _startedAt = startedAt;
        Url = url;
    }

    public static async Task<SupervisedSession> StartAsync(
        string workloadsDir,
        string workload,
        string workloadConfigPath,
        ISessionAgentFactory agentFactory,
        CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var sessionId = SessionPaths.GenerateSessionId(startedAt);
        var sessionDir = SessionPaths.SessionDir(workloadsDir, workload, sessionId);
        System.IO.Directory.CreateDirectory(sessionDir);
        System.IO.Directory.CreateDirectory(SessionPaths.CapturesDir(sessionDir));

        var telemetryPath = SessionPaths.TelemetryPath(sessionDir);
        var telemetrySink = new NdjsonFileSink(telemetryPath);

        SessionAgentBundle bundle;
        try
        {
            bundle = await agentFactory.CreateAndInitializeAsync(workloadConfigPath, telemetrySink, ct).ConfigureAwait(false);
        }
        catch
        {
            telemetrySink.Dispose();
            throw;
        }

        return new SupervisedSession(sessionId, workload, sessionDir, bundle.Agent, telemetrySink, startedAt, bundle.Url);
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

        await _agent.CaptureScreenshotAsync(new CaptureSettings { OutputPath = pngPath }).ConfigureAwait(false);

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
        };
        _captures.Add(capture);

        _telemetrySink.Write(new TelemetryRecord
        {
            T = now,
            RunId = SessionId,
            Kind = TelemetryKind.Screenshot,
            Source = "canary-session",
            Data = new { path = pngPath, slug, sequence = seq, noteTitle, noteBody },
        });

        return new CaptureResult { Sequence = seq, PngPath = pngPath, Slug = slug };
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
            Captures = _captures.ToList(),
        };
        SessionReportWriter.Write(Directory, data);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_ended)
        {
            try { await EndAsync().ConfigureAwait(false); } catch { }
        }
        if (_agent is IDisposable d)
        {
            try { d.Dispose(); } catch { }
        }
    }
}
