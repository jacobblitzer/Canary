using System.Net.Http;
using System.Text.Json;
using Canary.Cdp;

namespace Canary.Agent.Penumbra;

/// <summary>
/// Detects whether Penumbra's Vite dev server and Chrome/CDP are already running,
/// so the UI can reuse an existing instance instead of launching fresh processes.
/// </summary>
public static class PenumbraInstanceProbe
{
    /// <summary>
    /// Result of probing for an existing Penumbra instance.
    /// </summary>
    public sealed class ProbeResult
    {
        public bool ViteRunning { get; init; }
        public bool CdpAvailable { get; init; }
        public bool PenumbraReady { get; init; }
        public string? PageWebSocketUrl { get; init; }
        public string? ViteUrl { get; init; }
        public string? RendererBackend { get; init; }
    }

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    /// <summary>
    /// Check if a Vite dev server is responding on the given port.
    /// </summary>
    public static async Task<bool> IsViteRunningAsync(int port)
    {
        try
        {
            var response = await HttpClient.GetAsync($"http://localhost:{port}").ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Query the CDP /json/list endpoint to find the first page target.
    /// Returns its webSocketDebuggerUrl, or null if unavailable.
    /// </summary>
    public static async Task<string?> FindCdpPageTargetAsync(int port)
    {
        try
        {
            var json = await HttpClient.GetStringAsync($"http://localhost:{port}/json/list").ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            foreach (var target in doc.RootElement.EnumerateArray())
            {
                if (target.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "page" &&
                    target.TryGetProperty("webSocketDebuggerUrl", out var wsProp))
                {
                    return wsProp.GetString();
                }
            }
        }
        catch
        {
            // CDP not available
        }

        return null;
    }

    /// <summary>
    /// Connect to a CDP page via WebSocket and check if Penumbra's test harness is ready.
    /// Returns (ready, backend) — backend is null if not ready.
    /// </summary>
    public static async Task<(bool Ready, string? Backend)> IsPenumbraReadyAsync(string wsUrl)
    {
        CdpClient? cdp = null;
        try
        {
            cdp = new CdpClient(TimeSpan.FromSeconds(5));
            await cdp.ConnectAsync(wsUrl).ConfigureAwait(false);
            await cdp.EnableDomainAsync("Runtime").ConfigureAwait(false);

            var result = await cdp.EvaluateAsync<JsonElement?>(
                "window.__canaryGetRendererInfo ? window.__canaryGetRendererInfo() : null"
            ).ConfigureAwait(false);

            if (result.HasValue && result.Value.ValueKind == JsonValueKind.Object)
            {
                string? backend = null;
                if (result.Value.TryGetProperty("backend", out var backendProp))
                    backend = backendProp.GetString();
                return (true, backend);
            }

            return (false, null);
        }
        catch
        {
            return (false, null);
        }
        finally
        {
            cdp?.Dispose();
        }
    }

    /// <summary>
    /// Orchestrate all probe checks: Vite, CDP page target, Penumbra readiness.
    /// </summary>
    public static async Task<ProbeResult> ProbeAsync(int vitePort, int cdpPort)
    {
        var viteRunning = await IsViteRunningAsync(vitePort).ConfigureAwait(false);
        var viteUrl = viteRunning ? $"http://localhost:{vitePort}" : null;

        var wsUrl = await FindCdpPageTargetAsync(cdpPort).ConfigureAwait(false);
        var cdpAvailable = wsUrl != null;

        bool penumbraReady = false;
        string? backend = null;

        if (cdpAvailable)
        {
            (penumbraReady, backend) = await IsPenumbraReadyAsync(wsUrl!).ConfigureAwait(false);
        }

        return new ProbeResult
        {
            ViteRunning = viteRunning,
            CdpAvailable = cdpAvailable,
            PenumbraReady = penumbraReady,
            PageWebSocketUrl = wsUrl,
            ViteUrl = viteUrl,
            RendererBackend = backend
        };
    }
}
