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
        // Launch Rhino + connect the named-pipe agent, then register the session telemetry sink so the agent
        // tails Penumbra's in-Rhino preview NDJSON into telemetry.ndjson (v2 telemetry source — Penumbra
        // scene/move/rep/frame/error events; the Rhino-command-history + Slop-log-tail sources remain a follow-up).
        var agent = await RhinoSessionAgent.CreateAsync(workload, ct).ConfigureAwait(false);
        agent.RegisterTelemetrySink(telemetrySink);
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
