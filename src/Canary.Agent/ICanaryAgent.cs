namespace Canary.Agent;

/// <summary>
/// Contract between the harness and a workload agent running inside a target application.
/// </summary>
public interface ICanaryAgent
{
    /// <summary>
    /// Execute a named action with parameters.
    /// Actions are app-specific: "OpenFile", "RunCommand", "SetViewport", etc.
    /// </summary>
    Task<AgentResponse> ExecuteAsync(string action, Dictionary<string, string> parameters);

    /// <summary>
    /// Capture a screenshot of the application's primary viewport.
    /// </summary>
    Task<ScreenshotResult> CaptureScreenshotAsync(CaptureSettings settings);

    /// <summary>
    /// Heartbeat check — returns ok:true if the agent is alive and responsive.
    /// </summary>
    Task<HeartbeatResult> HeartbeatAsync();

    /// <summary>
    /// Gracefully abort any in-progress operation.
    /// </summary>
    Task AbortAsync();
}

/// <summary>
/// Settings for a screenshot capture request.
/// </summary>
public class CaptureSettings
{
    /// <summary>Width of the captured image in pixels.</summary>
    public int Width { get; set; } = 800;

    /// <summary>Height of the captured image in pixels.</summary>
    public int Height { get; set; } = 600;

    /// <summary>File path where the captured PNG should be saved.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// When true, the agent ALSO captures a full-screen desktop screenshot alongside
    /// the viewport capture. The full-screen PNG is written next to <see cref="OutputPath"/>
    /// with a <c>.fullscreen.png</c> suffix (e.g. <c>foo.png</c> → <c>foo.fullscreen.png</c>).
    /// Useful for debugging warning balloons, modal dialogs, or off-viewport errors that
    /// the viewport-only capture misses.
    /// </summary>
    public bool IncludeFullScreen { get; set; } = false;

    /// <summary>
    /// When true, the agent ALSO captures <see cref="GifFrameCount"/> additional viewport
    /// frames at <see cref="GifFrameIntervalMs"/> intervals after the main PNG, saving each as
    /// a sibling PNG named <c>foo.frame{NN}.png</c>. The orchestrator (TestRunner) reads those
    /// frame PNGs from <see cref="ScreenshotResult.FramePaths"/> and encodes them into a single
    /// animated GIF (<c>foo.gif</c>) via the ImageSharp GifEncoder already present in
    /// Canary.Core.
    /// </summary>
    /// <remarks>
    /// Useful for kinematics + animated-render fixtures where the viewport changes over time
    /// (slider scrub, render progressive reveal, Grasshopper Animate-style timelines). For
    /// fixtures where the viewport is static, the GIF will contain N copies of the same frame
    /// — harmless but useless. Phase 4.6.F Session B (CPig.Kinematics animated-mechanism test
    /// coverage prompt, 2026-06-01).
    /// </remarks>
    public bool RecordGif { get; set; } = false;

    /// <summary>Number of additional frames to capture beyond the main static PNG. Default 30.</summary>
    public int GifFrameCount { get; set; } = 30;

    /// <summary>Sleep interval between consecutive frame captures in milliseconds. Default 100 ms.</summary>
    public int GifFrameIntervalMs { get; set; } = 100;
}

/// <summary>
/// Result of a screenshot capture.
/// </summary>
public class ScreenshotResult
{
    /// <summary>Path to the saved PNG file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Width of the captured image.</summary>
    public int Width { get; set; }

    /// <summary>Height of the captured image.</summary>
    public int Height { get; set; }

    /// <summary>When the screenshot was taken.</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>
    /// Path to the optional full-screen capture, if <see cref="CaptureSettings.IncludeFullScreen"/>
    /// was true. Null otherwise. Captures the whole desktop (all monitors of the primary
    /// device) at native resolution — useful for catching warning balloons / toasts.
    /// </summary>
    public string? FullScreenPath { get; set; }

    /// <summary>
    /// Ordered list of intermediate frame PNG paths captured when
    /// <see cref="CaptureSettings.RecordGif"/> was true. Empty otherwise. Each path is a sibling
    /// of <see cref="FilePath"/> named <c>{baseName}.frame{NN}.png</c>. The orchestrator
    /// (TestRunner) reads these and encodes them into <see cref="GifPath"/>.
    /// </summary>
    public List<string> FramePaths { get; set; } = new();

    /// <summary>
    /// Path to the encoded animated GIF, populated by the orchestrator after it consumes
    /// <see cref="FramePaths"/>. Null when GIF capture was off or encoding failed.
    /// </summary>
    public string? GifPath { get; set; }
}

/// <summary>
/// Result of a heartbeat check.
/// </summary>
public class HeartbeatResult
{
    /// <summary>True if the agent is alive and responsive.</summary>
    public bool Ok { get; set; }

    /// <summary>Optional application state information.</summary>
    public Dictionary<string, string> State { get; set; } = new();
}

/// <summary>
/// Result of an agent execute command.
/// </summary>
public class AgentResponse
{
    /// <summary>Whether the command succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Human-readable message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional data returned by the command.</summary>
    public Dictionary<string, string> Data { get; set; } = new();
}
