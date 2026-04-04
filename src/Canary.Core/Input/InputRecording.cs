using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Input;

/// <summary>
/// Metadata about an input recording session.
/// </summary>
public sealed class RecordingMetadata
{
    /// <summary>Workload name (e.g. "pigment").</summary>
    [JsonPropertyName("workload")]
    public string Workload { get; set; } = string.Empty;

    /// <summary>When the recording was made.</summary>
    [JsonPropertyName("recordedAt")]
    public DateTime RecordedAt { get; set; }

    /// <summary>Viewport width in pixels at the time of recording.</summary>
    [JsonPropertyName("viewportWidth")]
    public int ViewportWidth { get; set; }

    /// <summary>Viewport height in pixels at the time of recording.</summary>
    [JsonPropertyName("viewportHeight")]
    public int ViewportHeight { get; set; }

    /// <summary>Window title at the time of recording.</summary>
    [JsonPropertyName("windowTitle")]
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>Total recording duration in milliseconds.</summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }
}

/// <summary>
/// A complete input recording: metadata plus a sequence of timestamped events.
/// </summary>
public sealed class InputRecording
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Recording metadata.</summary>
    [JsonPropertyName("metadata")]
    public RecordingMetadata Metadata { get; set; } = new();

    /// <summary>Ordered list of input events.</summary>
    [JsonPropertyName("events")]
    public List<InputEvent> Events { get; set; } = new();

    /// <summary>
    /// Serialize the recording to a JSON string.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>
    /// Deserialize a recording from a JSON string.
    /// </summary>
    public static InputRecording FromJson(string json)
    {
        return JsonSerializer.Deserialize<InputRecording>(json, JsonOptions)
            ?? throw new JsonException("Failed to deserialize InputRecording.");
    }

    /// <summary>
    /// Save the recording to a file.
    /// </summary>
    public async Task SaveAsync(string filePath)
    {
        var json = ToJson();
        await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
    }

    /// <summary>
    /// Load a recording from a file.
    /// </summary>
    public static async Task<InputRecording> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        return FromJson(json);
    }
}
