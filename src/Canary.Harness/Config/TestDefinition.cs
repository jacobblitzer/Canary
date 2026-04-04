using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Config;

/// <summary>
/// A test definition loaded from a JSON file. Describes what to do and what to verify.
/// </summary>
public sealed class TestDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("workload")]
    public string Workload { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("setup")]
    public TestSetup? Setup { get; set; }

    [JsonPropertyName("recording")]
    public string Recording { get; set; } = string.Empty;

    [JsonPropertyName("checkpoints")]
    public List<TestCheckpoint> Checkpoints { get; set; } = new();

    /// <summary>
    /// Parse a test definition from a JSON string.
    /// </summary>
    public static TestDefinition Parse(string json)
    {
        var def = JsonSerializer.Deserialize<TestDefinition>(json)
            ?? throw new JsonException("Failed to deserialize test definition.");

        if (string.IsNullOrWhiteSpace(def.Name))
            throw new JsonException("Test definition is missing required field 'name'.");

        if (string.IsNullOrWhiteSpace(def.Workload))
            throw new JsonException("Test definition is missing required field 'workload'.");

        return def;
    }

    /// <summary>
    /// Load a test definition from a file.
    /// </summary>
    public static async Task<TestDefinition> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        return Parse(json);
    }
}

/// <summary>
/// Setup configuration for a test — what file to open, viewport settings, setup commands.
/// </summary>
public sealed class TestSetup
{
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("viewport")]
    public ViewportSetup? Viewport { get; set; }

    [JsonPropertyName("commands")]
    public List<string> Commands { get; set; } = new();
}

/// <summary>
/// Viewport configuration for test setup.
/// </summary>
public sealed class ViewportSetup
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = 800;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 600;

    [JsonPropertyName("projection")]
    public string Projection { get; set; } = string.Empty;

    [JsonPropertyName("displayMode")]
    public string DisplayMode { get; set; } = string.Empty;
}

/// <summary>
/// A checkpoint within a test — a moment to capture and compare a screenshot.
/// </summary>
public sealed class TestCheckpoint
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("atTimeMs")]
    public long AtTimeMs { get; set; }

    [JsonPropertyName("tolerance")]
    public double Tolerance { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
