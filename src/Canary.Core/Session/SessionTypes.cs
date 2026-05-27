namespace Canary.Session;

public sealed class SessionCapture
{
    public required int Sequence { get; init; }
    public required DateTime CapturedAtUtc { get; init; }
    public string? Slug { get; init; }
    public required string PngFile { get; init; }
    public string? AnnotatedPngFile { get; init; }
    public string? AnnotationsJsonFile { get; init; }
    public string? NoteTitle { get; init; }
    public string? NoteBody { get; init; }
}

public sealed class SessionData
{
    public required string SessionId { get; init; }
    public required string Workload { get; init; }
    public string? Url { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public DateTime? EndedAtUtc { get; init; }
    public string? CloseoutNotes { get; init; }
    public List<SessionCapture> Captures { get; init; } = new();
}

public sealed class CaptureResult
{
    public required int Sequence { get; init; }
    public required string PngPath { get; init; }
    public string? Slug { get; init; }
}
