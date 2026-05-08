using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Agent.Qualia;

/// <summary>
/// Qualia-specific configuration, stored inside <c>workload.json</c>
/// under the <c>qualiaConfig</c> key. Mirrors <see cref="Canary.Agent.Penumbra.PenumbraConfig"/>;
/// the two are kept parallel rather than extracted to a shared base because
/// (a) only two browser-based workloads exist today, (b) Penumbra and Qualia
/// will likely diverge on different axes (Qualia adds graph-data hooks,
/// Penumbra adds renderer-internal hooks), and (c) the configs are pure data —
/// duplication cost is bounded.
/// </summary>
public sealed class QualiaConfig
{
    /// <summary>Absolute path to the Qualia repo root (where <c>package.json</c> + <c>vite.config.ts</c> live).</summary>
    [JsonPropertyName("projectDir")]
    public string ProjectDir { get; set; } = string.Empty;

    /// <summary>Path to Chrome/Edge executable. Empty = auto-detect.</summary>
    [JsonPropertyName("chromePath")]
    public string ChromePath { get; set; } = string.Empty;

    /// <summary>Chrome DevTools Protocol port (default 9223 to avoid clashing with Penumbra's 9222).</summary>
    [JsonPropertyName("cdpPort")]
    public int CdpPort { get; set; } = 9223;

    /// <summary>Vite dev server port (default 5173 — Vite's standard default).</summary>
    [JsonPropertyName("vitePort")]
    public int VitePort { get; set; } = 5173;

    /// <summary>Viewport pixel width for captures (default 1280).</summary>
    [JsonPropertyName("defaultCanvasWidth")]
    public int DefaultCanvasWidth { get; set; } = 1280;

    /// <summary>Viewport pixel height for captures (default 720).</summary>
    [JsonPropertyName("defaultCanvasHeight")]
    public int DefaultCanvasHeight { get; set; } = 720;

    /// <summary>Additional Chrome command-line flags for launch.</summary>
    [JsonPropertyName("chromeFlags")]
    public List<string> ChromeFlags { get; set; } = new();

    /// <summary>Milliseconds to wait after a state change before capturing (render stabilization).</summary>
    [JsonPropertyName("defaultStabilizeMs")]
    public int DefaultStabilizeMs { get; set; } = 500;

    /// <summary>
    /// Maximum time (seconds) to wait for the Qualia app to reach "ready" state
    /// (initial demo data loaded + renderer attached). Default 30s.
    /// </summary>
    [JsonPropertyName("readyTimeoutSec")]
    public int ReadyTimeoutSec { get; set; } = 30;

    /// <summary>
    /// If true, the bridge clears <c>localStorage</c> for the Vite origin before
    /// each test so first-launch behavior (LandingScreen visible, no persisted
    /// module config) is reproducible. Defaults true; set false for tests that
    /// explicitly assert post-restart state.
    /// </summary>
    [JsonPropertyName("clearLocalStorageOnInit")]
    public bool ClearLocalStorageOnInit { get; set; } = true;
}

/// <summary>
/// Extended workload config for Qualia — same shape as <see cref="Canary.Agent.Penumbra.PenumbraWorkloadConfig"/>
/// but reading a <c>qualiaConfig</c> section.
/// </summary>
public sealed class QualiaWorkloadConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("agentType")]
    public string AgentType { get; set; } = string.Empty;

    [JsonPropertyName("pipeName")]
    public string PipeName { get; set; } = string.Empty;

    [JsonPropertyName("startupTimeoutMs")]
    public int StartupTimeoutMs { get; set; } = 30000;

    [JsonPropertyName("qualiaConfig")]
    public QualiaConfig QualiaConfig { get; set; } = new();

    public static async Task<QualiaWorkloadConfig> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        return JsonSerializer.Deserialize<QualiaWorkloadConfig>(json)
            ?? throw new JsonException("Failed to deserialize Qualia workload config.");
    }
}
