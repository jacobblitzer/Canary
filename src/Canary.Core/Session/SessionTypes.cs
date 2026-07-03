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
    /// <summary>Rhino viewport that was active at capture time (flight-recorder; null pre-feature / non-Rhino).</summary>
    public string? ActiveView { get; init; }
    /// <summary>Penumbra FrameState.Status sampled right AFTER the pixel grab (e.g. "scene steady q=100% steps=192").</summary>
    public string? FrameStatus { get; init; }
}

public sealed class SessionData
{
    public required string SessionId { get; init; }
    public required string Workload { get; init; }
    public string? Url { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public DateTime? EndedAtUtc { get; init; }
    public string? CloseoutNotes { get; init; }
    public string? OpenedFile { get; init; }
    public List<SessionCapture> Captures { get; init; } = new();
}

/// <summary>Options for starting a supervised session (flight-recorder Phase A).</summary>
public sealed class SessionStartOptions
{
    /// <summary>Absolute path to a document the agent should open right after launch (rhino workload only).</summary>
    public string? OpenFilePath { get; init; }
}

/// <summary>Per-launch context threaded from the session into the agent factory so the spawned
/// process can be stamped with a correlation ref (PENUMBRA_SESSION_REF).</summary>
public sealed class SessionLaunchContext
{
    public required string SessionRef { get; init; }
}

/// <summary>What the launcher actually did for this session's spawn — recorded into manifest.json.</summary>
public sealed class SessionLaunchInfo
{
    public string? AppPath { get; init; }
    public int? ProcessId { get; init; }
    public IReadOnlyDictionary<string, string>? AppliedEnv { get; init; }
}

/// <summary>
/// The machine-readable per-session manifest (manifest.json) — the superset of session.json.
/// One file that tells a debugging agent WHAT ran: file, versions, env, exit status; plus fields
/// harvested from the Penumbra telemetry stream when available (SHAs arrive on the first scene
/// push until Penumbra's banner-ordering fix lands — flight-recorder Phase B).
/// </summary>
public sealed class SessionManifest
{
    public required string SessionId { get; init; }
    public required string Workload { get; init; }
    /// <summary>The correlation ref handed to the spawned process as PENUMBRA_SESSION_REF.</summary>
    public string? SessionRef { get; init; }
    public string? OpenedFile { get; init; }
    public string? OpenedFileSha256 { get; init; }
    public string? MachineName { get; init; }
    public string? CanaryVersion { get; init; }
    public string? AppPath { get; init; }
    public int? ProcessId { get; init; }
    public IReadOnlyDictionary<string, string>? AppliedEnv { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public DateTime? EndedAtUtc { get; set; }
    public int CaptureCount { get; set; }
    /// <summary>Exit code of the spawned app process, when it exited BEFORE session close-out.</summary>
    public int? ProcessExitCode { get; set; }
    public DateTime? ProcessExitAtUtc { get; set; }
    /// <summary>True when the app process exited before the operator ended the session (clean close OR crash).</summary>
    public bool ExitedBeforeCloseout { get; set; }
    /// <summary>ExitedBeforeCloseout with a non-zero exit code — a kill or crash, not an operator close.</summary>
    public bool DiedUnexpectedly { get; set; }
    /// <summary>Fields lifted from the session telemetry (penumbra.startup-diagnostics / gl.build.probe payloads).</summary>
    public Dictionary<string, string>? Harvested { get; set; }
}

public sealed class CaptureResult
{
    public required int Sequence { get; init; }
    public required string PngPath { get; init; }
    public string? Slug { get; init; }
}
