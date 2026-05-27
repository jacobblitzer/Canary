using Canary.Agent.Penumbra;
using Canary.Agent.Qualia;
using Canary.Config;
using Canary.Session;
using Canary.Telemetry;

namespace Canary.Harness.Session;

public sealed class SessionAgentFactory : ISessionAgentFactory
{
    public async Task<SessionAgentBundle> CreateAndInitializeAsync(
        string workloadConfigPath,
        ITelemetrySink telemetrySink,
        CancellationToken ct)
    {
        var workload = await WorkloadConfig.LoadAsync(workloadConfigPath).ConfigureAwait(false);
        return workload.AgentType switch
        {
            "qualia-cdp" => await CreateQualiaAsync(workloadConfigPath, telemetrySink, ct).ConfigureAwait(false),
            "penumbra-cdp" => await CreatePenumbraAsync(workloadConfigPath, telemetrySink, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Supervised sessions are not supported for agentType '{workload.AgentType}'. " +
                "Only qualia-cdp and penumbra-cdp are supported in v1."),
        };
    }

    private static async Task<SessionAgentBundle> CreateQualiaAsync(string path, ITelemetrySink sink, CancellationToken ct)
    {
        var cfg = await QualiaWorkloadConfig.LoadAsync(path).ConfigureAwait(false);
        var agent = new QualiaBridgeAgent(cfg.QualiaConfig);
        agent.RegisterTelemetrySink(sink);
        await agent.InitializeAsync(ct).ConfigureAwait(false);
        return new SessionAgentBundle
        {
            Agent = agent,
            Url = $"http://localhost:{cfg.QualiaConfig.VitePort}/",
        };
    }

    private static async Task<SessionAgentBundle> CreatePenumbraAsync(string path, ITelemetrySink sink, CancellationToken ct)
    {
        var cfg = await PenumbraWorkloadConfig.LoadAsync(path).ConfigureAwait(false);
        var agent = new PenumbraBridgeAgent(cfg.PenumbraConfig);
        agent.RegisterTelemetrySink(sink);
        await agent.InitializeAsync(ct).ConfigureAwait(false);
        return new SessionAgentBundle
        {
            Agent = agent,
            Url = $"http://localhost:{cfg.PenumbraConfig.VitePort}/",
        };
    }
}
