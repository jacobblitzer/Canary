using System.Text.Json;
using System.Text.Json.Serialization;

namespace Canary.Telemetry;

// One JSON envelope shape for every workload agent + harness component
// per design §C1. Serialized as NDJSON (one record per line) by
// NdjsonFileSink. The Data payload shape varies by Kind — kept as a
// loose object so each producer can supply its own typed POCO or a
// JsonObject without forcing a closed-set discriminator. The
// per-kind shapes in §C1's table are conventions, not enforced contracts.
public sealed class TelemetryRecord
{
    // ISO 8601 timestamp (serialized as a string by the default options).
    public DateTime T { get; init; } = DateTime.UtcNow;

    // Run-correlation id assigned by TestRunner at suite start. Records
    // emitted outside a run (e.g. UI background polling) carry null.
    public string? RunId { get; init; }

    // The test being executed when this record was emitted. Null for
    // suite-level / non-test records.
    public string? TestName { get; init; }

    // The checkpoint in flight when this record was emitted. Null when the
    // record straddles checkpoints (e.g. console output between captures).
    public string? CheckpointName { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TelemetryKind Kind { get; init; }

    // "info" | "warn" | "error" | "debug". Free-form to match CDP's
    // Runtime.consoleAPICalled.type values without a closed mapping.
    public string? Level { get; init; }

    // "rhino" | "penumbra" | "qualia" | "canary-harness" — the producer
    // bucket. Lets downstream filtering (Past Runs + Telemetry tabs in
    // Phase 7) group records by origin.
    public string? Source { get; init; }

    // Kind-specific payload. Convention shapes (per §C1 table):
    //   Console: { text, args, sourceUrl, lineNumber }
    //   Network: { method, url, status, durationMs, errorText }
    //   Input:   { type, subtype, vx, vy, key, button }
    //   AgentState: heartbeat State dict
    //   AgentAction: { action, params, success, message, durationMs }
    //   Log: { text }
    //   Screenshot: { path, width, height }
    // Producers are expected to supply objects that JSON-serialize to those
    // shapes. The runtime does not validate.
    public object? Data { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static TelemetryRecord? FromJson(string json)
        => JsonSerializer.Deserialize<TelemetryRecord>(json, SerializerOptions);

    // Shared options — `WriteIndented = false` so NDJSON stays
    // one-record-per-line. Camel-case to match CDP conventions consumers
    // already know.
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
