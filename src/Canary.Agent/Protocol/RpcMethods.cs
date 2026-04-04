namespace Canary.Agent.Protocol;

/// <summary>
/// JSON-RPC method name constants for the Canary agent protocol.
/// </summary>
public static class RpcMethods
{
    /// <summary>Heartbeat check — verifies the agent is alive.</summary>
    public const string Heartbeat = "Heartbeat";

    /// <summary>Execute a named action with parameters.</summary>
    public const string Execute = "Execute";

    /// <summary>Capture a screenshot of the application viewport.</summary>
    public const string CaptureScreenshot = "CaptureScreenshot";

    /// <summary>Abort any in-progress operation.</summary>
    public const string Abort = "Abort";
}
