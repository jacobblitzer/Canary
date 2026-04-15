using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Config;

/// <summary>
/// Configuration for a workload — how to launch and connect to the target application.
/// </summary>
public sealed class WorkloadConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("appPath")]
    public string AppPath { get; set; } = string.Empty;

    [JsonPropertyName("appArgs")]
    public string AppArgs { get; set; } = string.Empty;

    [JsonPropertyName("agentType")]
    public string AgentType { get; set; } = string.Empty;

    [JsonPropertyName("pipeName")]
    public string PipeName { get; set; } = string.Empty;

    [JsonPropertyName("startupTimeoutMs")]
    public int StartupTimeoutMs { get; set; } = 30000;

    [JsonPropertyName("windowTitle")]
    public string WindowTitle { get; set; } = string.Empty;

    [JsonPropertyName("viewportClass")]
    public string ViewportClass { get; set; } = string.Empty;

    /// <summary>
    /// Commands to run via the agent after every app launch (before test-specific setup).
    /// Used to set a consistent viewport state (e.g. maximize perspective view).
    /// </summary>
    [JsonPropertyName("setupCommands")]
    public List<string> SetupCommands { get; set; } = new();

    /// <summary>
    /// Parse a workload config from a JSON string.
    /// </summary>
    public static WorkloadConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<WorkloadConfig>(json)
            ?? throw new JsonException("Failed to deserialize workload config.");

        if (string.IsNullOrWhiteSpace(config.Name))
            throw new JsonException("Workload config is missing required field 'name'.");

        return config;
    }

    /// <summary>
    /// Load a workload config from a file.
    /// </summary>
    public static async Task<WorkloadConfig> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        return Parse(json);
    }
}
