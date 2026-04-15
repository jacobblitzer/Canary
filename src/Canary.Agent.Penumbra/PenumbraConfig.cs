using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Agent.Penumbra;

/// <summary>
/// Penumbra-specific configuration, stored inside workload.json under "penumbraConfig".
/// </summary>
public sealed class PenumbraConfig
{
    /// <summary>Absolute path to the Penumbra monorepo root.</summary>
    [JsonPropertyName("projectDir")]
    public string ProjectDir { get; set; } = string.Empty;

    /// <summary>Path to Chrome/Edge executable. Empty = auto-detect.</summary>
    [JsonPropertyName("chromePath")]
    public string ChromePath { get; set; } = string.Empty;

    /// <summary>Chrome DevTools Protocol port (default 9222).</summary>
    [JsonPropertyName("cdpPort")]
    public int CdpPort { get; set; } = 9222;

    /// <summary>Vite dev server port (default 3000).</summary>
    [JsonPropertyName("vitePort")]
    public int VitePort { get; set; } = 3000;

    /// <summary>Penumbra backend to test: "webgpu" or "webgl2".</summary>
    [JsonPropertyName("defaultBackend")]
    public string DefaultBackend { get; set; } = "webgpu";

    /// <summary>Canvas pixel width for captures (default 960).</summary>
    [JsonPropertyName("defaultCanvasWidth")]
    public int DefaultCanvasWidth { get; set; } = 960;

    /// <summary>Canvas pixel height for captures (default 540).</summary>
    [JsonPropertyName("defaultCanvasHeight")]
    public int DefaultCanvasHeight { get; set; } = 540;

    /// <summary>Additional Chrome command-line flags for launch.</summary>
    [JsonPropertyName("chromeFlags")]
    public List<string> ChromeFlags { get; set; } = new();

    /// <summary>
    /// Milliseconds to wait after camera change before capturing (render stabilization).
    /// Default 500ms. Atlas scenes may need longer — the bridge polls isAtlasBuildComplete().
    /// </summary>
    [JsonPropertyName("defaultStabilizeMs")]
    public int DefaultStabilizeMs { get; set; } = 500;
}

/// <summary>
/// Extended workload config that includes the optional Penumbra section.
/// </summary>
public sealed class PenumbraWorkloadConfig
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

    [JsonPropertyName("penumbraConfig")]
    public PenumbraConfig PenumbraConfig { get; set; } = new();

    /// <summary>
    /// Load from a workload.json file.
    /// </summary>
    public static async Task<PenumbraWorkloadConfig> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        return JsonSerializer.Deserialize<PenumbraWorkloadConfig>(json)
            ?? throw new JsonException("Failed to deserialize Penumbra workload config.");
    }
}
