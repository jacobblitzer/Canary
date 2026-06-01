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
