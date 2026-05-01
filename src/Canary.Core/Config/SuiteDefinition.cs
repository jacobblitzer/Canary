using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Config;

/// <summary>
/// A suite definition loaded from a JSON file. Groups tests by name for batch execution
/// with isolated result directories.
/// </summary>
public sealed class SuiteDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("tests")]
    public List<string> Tests { get; set; } = new();

    /// <summary>
    /// When true, the target application is kept alive after the suite completes
    /// so the user can inspect state manually. Applies regardless of pass/fail.
    /// </summary>
    [JsonPropertyName("keepOpen")]
    public bool KeepOpen { get; set; }

    /// <summary>
    /// Parse a suite definition from a JSON string.
    /// </summary>
    public static SuiteDefinition Parse(string json)
    {
        var def = JsonSerializer.Deserialize<SuiteDefinition>(json)
            ?? throw new JsonException("Failed to deserialize suite definition.");

        if (string.IsNullOrWhiteSpace(def.Name))
            throw new JsonException("Suite definition is missing required field 'name'.");

        if (def.Tests.Count == 0)
            throw new JsonException("Suite definition must contain at least one test.");

        return def;
    }

    /// <summary>
    /// Load a suite definition from a file.
    /// </summary>
    public static async Task<SuiteDefinition> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        return Parse(json);
    }
}
