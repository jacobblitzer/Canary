using System.Text.Json.Serialization;

namespace Canary.Config;

/// <summary>
/// Configuration for the Vision-Language Model oracle. Specifies which provider
/// and model to use for VLM-mode checkpoints. Lives in the test definition's
/// <see cref="TestSetup.Vlm"/> field, or can be overridden per checkpoint.
/// </summary>
public sealed class VlmConfig
{
    /// <summary>
    /// Provider name: "claude" or "ollama".
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "claude";

    /// <summary>
    /// Model identifier passed to the provider's API.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "claude-sonnet-4-20250514";

    /// <summary>
    /// Maximum tokens for the VLM response.
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int MaxTokens { get; set; } = 1024;
}
