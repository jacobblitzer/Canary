namespace Canary.Telemetry;

// One enum per record category, per design §C1.
public enum TelemetryKind
{
    // console.log / console.warn / console.error from page JS (CDP
    // Runtime.consoleAPICalled) or RhinoApp.WriteLine.
    Console,

    // CDP Network.responseReceived + Network.loadingFailed (browser side).
    Network,

    // Mouse / keyboard dispatch from CDP Input.dispatchMouseEvent or the
    // harness-side InputReplayer. Emitted at injection time.
    Input,

    // The existing HeartbeatResult.State dict, captured at each poll.
    AgentState,

    // Every ICanaryAgent.ExecuteAsync round-trip (params + outcome).
    AgentAction,

    // The existing ITestLogger.Log text. Captured so all human-readable
    // progress lives in one place.
    Log,

    // Emitted after CaptureScreenshotAsync writes a file (path + dimensions).
    Screenshot,
}
