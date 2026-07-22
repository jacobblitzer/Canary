using Canary.Agent.Penumbra;
using Canary.Agent.Qualia;
using Canary.Config;
using Canary.Session;
using Canary.Telemetry;

namespace Canary.Harness.Session;

public sealed class SessionAgentFactory : ISessionAgentFactory
{
    public Task<SessionAgentBundle> CreateAndInitializeAsync(
        string workloadConfigPath,
        ITelemetrySink telemetrySink,
        CancellationToken ct)
        => CreateAndInitializeAsync(workloadConfigPath, telemetrySink, context: null, ct);

    public async Task<SessionAgentBundle> CreateAndInitializeAsync(
        string workloadConfigPath,
        ITelemetrySink telemetrySink,
        SessionLaunchContext? context,
        CancellationToken ct)
    {
        var workload = await WorkloadConfig.LoadAsync(workloadConfigPath).ConfigureAwait(false);
        return workload.AgentType switch
        {
            "qualia-cdp" => await CreateQualiaAsync(workloadConfigPath, telemetrySink, ct).ConfigureAwait(false),
            "penumbra-cdp" => await CreatePenumbraAsync(workloadConfigPath, telemetrySink, ct).ConfigureAwait(false),
            "rhino" => await CreateRhinoAsync(workload, telemetrySink, context, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Supervised sessions are not supported for agentType '{workload.AgentType}'. " +
                "Supported: qualia-cdp, penumbra-cdp, rhino."),
        };
    }

    private static async Task<SessionAgentBundle> CreateRhinoAsync(
        WorkloadConfig workload, ITelemetrySink telemetrySink, SessionLaunchContext? context, CancellationToken ct)
    {
        // Launch Rhino + connect the named-pipe agent, then register the session telemetry sink so the agent
        // tails Penumbra's in-Rhino preview NDJSON into telemetry.ndjson (v2 telemetry source — Penumbra
        // scene/move/rep/frame/error events; the Rhino-command-history + Slop-log-tail sources remain a follow-up).
        var agent = await RhinoSessionAgent.CreateAsync(workload, context, ct).ConfigureAwait(false);
        agent.RegisterTelemetrySink(telemetrySink);
        return new SessionAgentBundle
        {
            Agent = agent,
            Url = null, // Rhino has no URL; the SESSION_REPORT header omits this row.
            Launch = agent.LaunchInfo,
        };
    }

    private static async Task<SessionAgentBundle> CreateQualiaAsync(string path, ITelemetrySink sink, CancellationToken ct)
    {
        var cfg = await QualiaWorkloadConfig.LoadAsync(path).ConfigureAwait(false);
        var agent = new QualiaBridgeAgent(cfg.QualiaConfig);
        agent.RegisterTelemetrySink(sink);
        // Dispose on init failure — a half-initialized DESKTOP agent
        // otherwise orphans the launched Qualia exe (which then holds
        // the single-instance lock against the operator's own app).
        try
        {
            await agent.InitializeAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            agent.Dispose();
            throw;
        }
        return new SessionAgentBundle
        {
            Agent = agent,
            Url = cfg.QualiaConfig.Desktop
                ? "http://tauri.localhost/"
                : $"http://localhost:{cfg.QualiaConfig.VitePort}/",
        };
    }

    private static async Task<SessionAgentBundle> CreatePenumbraAsync(string path, ITelemetrySink sink, CancellationToken ct)
    {
        var cfg = await PenumbraWorkloadConfig.LoadAsync(path).ConfigureAwait(false);
        var agent = new PenumbraBridgeAgent(cfg.PenumbraConfig);
        agent.RegisterTelemetrySink(sink);
        // Same dispose-on-failure shape as CreateQualiaAsync (Chrome/Vite
        // orphans are less operator-hostile than a Tauri exe, but still
        // leak processes + a temp profile).
        try
        {
            await agent.InitializeAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            agent.Dispose();
            throw;
        }
        return new SessionAgentBundle
        {
            Agent = agent,
            Url = $"http://localhost:{cfg.PenumbraConfig.VitePort}/",
        };
    }
}
