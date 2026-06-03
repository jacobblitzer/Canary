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
            "rhino" => await CreateRhinoAsync(workload, telemetrySink, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Supervised sessions are not supported for agentType '{workload.AgentType}'. " +
                "Supported: qualia-cdp, penumbra-cdp, rhino."),
        };
    }

    private static async Task<SessionAgentBundle> CreateRhinoAsync(
        WorkloadConfig workload, ITelemetrySink telemetrySink, CancellationToken ct)
    {
        // v1 scope (2026-06-02): launch Rhino + connect the named-pipe agent.
        // Telemetry source for Rhino (command-line history + Slop log tail)
        // deferred to v2 — see docs/features/rhino-session.md.
        _ = telemetrySink;
        var agent = await RhinoSessionAgent.CreateAsync(workload, ct).ConfigureAwait(false);
        return new SessionAgentBundle
        {
            Agent = agent,
            Url = null, // Rhino has no URL; the SESSION_REPORT header omits this row.
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
