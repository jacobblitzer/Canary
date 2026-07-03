using Canary.Agent;
using Canary.Telemetry;

namespace Canary.Session;

public interface ISessionAgentFactory
{
    Task<SessionAgentBundle> CreateAndInitializeAsync(
        string workloadConfigPath,
        ITelemetrySink telemetrySink,
        CancellationToken ct);

    /// <summary>
    /// Context-aware overload (flight-recorder Phase A): carries the session's correlation ref so
    /// the factory can stamp the spawned process env (PENUMBRA_SESSION_REF). Default implementation
    /// ignores the context so existing factories/stubs keep working unchanged.
    /// </summary>
    Task<SessionAgentBundle> CreateAndInitializeAsync(
        string workloadConfigPath,
        ITelemetrySink telemetrySink,
        SessionLaunchContext? context,
        CancellationToken ct)
        => CreateAndInitializeAsync(workloadConfigPath, telemetrySink, ct);
}

public sealed class SessionAgentBundle
{
    public required ICanaryAgent Agent { get; init; }
    public string? Url { get; init; }
    /// <summary>Launch facts (pid, app path, applied env) when the factory spawned a process; null for CDP agents.</summary>
    public SessionLaunchInfo? Launch { get; init; }
}
