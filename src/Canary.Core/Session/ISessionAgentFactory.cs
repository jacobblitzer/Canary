using Canary.Agent;
using Canary.Telemetry;

namespace Canary.Session;

public interface ISessionAgentFactory
{
    Task<SessionAgentBundle> CreateAndInitializeAsync(
        string workloadConfigPath,
        ITelemetrySink telemetrySink,
        CancellationToken ct);
}

public sealed class SessionAgentBundle
{
    public required ICanaryAgent Agent { get; init; }
    public string? Url { get; init; }
}
