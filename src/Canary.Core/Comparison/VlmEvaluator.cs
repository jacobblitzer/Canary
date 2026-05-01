using Canary.Config;

namespace Canary.Comparison;

/// <summary>
/// Factory that creates the appropriate <see cref="IVlmProvider"/> from a
/// <see cref="VlmConfig"/>. Resolves the API key from environment variables.
/// </summary>
public static class VlmEvaluator
{
    /// <summary>
    /// Create a VLM provider based on the given configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the required API key is not found in environment variables,
    /// or the provider name is not recognized.
    /// </exception>
    public static IVlmProvider Create(VlmConfig config)
    {
        var provider = (config.Provider ?? "claude").ToLowerInvariant();
        return provider switch
        {
            "claude" => CreateClaude(config),
            "ollama" => CreateOllama(config),
            _ => throw new InvalidOperationException(
                $"Unknown VLM provider '{config.Provider}'. Supported: claude, ollama")
        };
    }

    private static ClaudeVlmProvider CreateClaude(VlmConfig config)
    {
        var apiKey = ResolveApiKey("CANARY_VLM_API_KEY", "ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "No API key found for Claude VLM provider. " +
                "Set CANARY_VLM_API_KEY or ANTHROPIC_API_KEY environment variable.");
        }

        var http = new HttpClient();
        return new ClaudeVlmProvider(http, apiKey, config.Model, config.MaxTokens);
    }

    private static OllamaVlmProvider CreateOllama(VlmConfig config)
    {
        var baseUrl = Environment.GetEnvironmentVariable("CANARY_OLLAMA_URL")
            ?? "http://localhost:11434";

        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        return new OllamaVlmProvider(http, config.Model, baseUrl);
    }

    /// <summary>
    /// Try environment variable names in order, return the first non-empty value.
    /// </summary>
    internal static string? ResolveApiKey(params string[] envVarNames)
    {
        foreach (var name in envVarNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }
}
