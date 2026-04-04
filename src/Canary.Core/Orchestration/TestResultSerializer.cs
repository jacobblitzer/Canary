using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Orchestration;

/// <summary>
/// Serializes and deserializes TestResult to/from JSON for persistence.
/// </summary>
public static class TestResultSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new TimeSpanConverter() }
    };

    public static async Task SaveAsync(TestResult result, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(result, Options);
        await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
    }

    public static async Task<TestResult> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        return JsonSerializer.Deserialize<TestResult>(json, Options)
            ?? throw new JsonException("Failed to deserialize test result.");
    }

    private sealed class TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TimeSpan.Parse(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
