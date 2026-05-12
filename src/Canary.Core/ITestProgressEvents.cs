using Canary.Orchestration;

namespace Canary;

/// <summary>
/// Structured progress event sink for the GUI (Canary.UI) and any other
/// consumer that wants more than the text log. Fired by <see cref="Canary.Orchestration.TestRunner"/>
/// at semantically meaningful points — checkpoint boundaries, screenshot
/// capture, VLM round-trip — so callers don't have to parse log strings.
///
/// Implementations MUST be thread-safe and non-blocking; the runner fires
/// these on the same thread that drives the test, so a slow handler
/// stalls the suite.
/// </summary>
public interface ITestProgressEvents
{
    /// <summary>Fires when a new test in the suite begins.</summary>
    void OnTestStarted(string testName);

    /// <summary>
    /// Fires when a checkpoint begins — before screenshot capture, before
    /// any VLM eval. Used by the GUI to add a "pending" card.
    /// </summary>
    void OnCheckpointStarted(string testName, string checkpointName, string? vlmDescription);

    /// <summary>
    /// Fires immediately after the candidate screenshot is written to disk.
    /// <paramref name="imagePath"/> is absolute. The GUI loads + thumbnails.
    /// </summary>
    void OnScreenshotCaptured(string testName, string checkpointName, string imagePath);

    /// <summary>
    /// Fires when the VLM round-trip is dispatched. <paramref name="prompt"/>
    /// is exactly what's being sent to the model (the test's
    /// <c>description</c> field). The GUI shows it next to the screenshot
    /// while the eval is in flight.
    /// </summary>
    void OnVlmEvaluating(string testName, string checkpointName, string prompt);

    /// <summary>
    /// Fires when the VLM verdict comes back. Reasoning may be empty if
    /// the model didn't include one. Confidence is 0..1.
    /// </summary>
    void OnVlmVerdict(string testName, string checkpointName, bool passed, double confidence, string reasoning);

    /// <summary>Fires when a test (all its checkpoints) finishes.</summary>
    void OnTestCompleted(string testName, TestStatus status, double durationSeconds);
}

/// <summary>
/// No-op implementation. Default for CLI runs that don't want the event surface.
/// </summary>
public sealed class NullTestProgressEvents : ITestProgressEvents
{
    public static readonly NullTestProgressEvents Instance = new();
    public void OnTestStarted(string testName) { }
    public void OnCheckpointStarted(string testName, string checkpointName, string? vlmDescription) { }
    public void OnScreenshotCaptured(string testName, string checkpointName, string imagePath) { }
    public void OnVlmEvaluating(string testName, string checkpointName, string prompt) { }
    public void OnVlmVerdict(string testName, string checkpointName, bool passed, double confidence, string reasoning) { }
    public void OnTestCompleted(string testName, TestStatus status, double durationSeconds) { }
}
