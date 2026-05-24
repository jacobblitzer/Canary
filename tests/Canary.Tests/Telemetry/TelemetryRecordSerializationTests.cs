using Canary.Cdp;
using Canary.Telemetry;
using Xunit;

namespace Canary.Tests.Telemetry;

// Unit tests for the TelemetryRecord JSON envelope shape (design §C1).
// Stable wire format matters — downstream consumers (Phase 3 REPORT.md
// generator, Phase 6 MCP server tools, Phase 7 PastRuns/Telemetry tabs,
// Claude reading via tools) all parse it.
public class TelemetryRecordSerializationTests
{
    [Trait("Category", "Unit")]
    [Fact]
    public void Serialize_AllFieldsSet_RoundTrips()
    {
        var record = new TelemetryRecord
        {
            T = new DateTime(2026, 5, 24, 14, 23, 1, DateTimeKind.Utc),
            RunId = "20260524-142300-a3f1",
            TestName = "diag-pencil-baseline",
            CheckpointName = "init",
            Kind = TelemetryKind.Console,
            Level = "warn",
            Source = "qualia",
            Data = new ConsolePayload { Text = "deprecated", Type = "warning" },
        };

        var json = record.ToJson();
        var back = TelemetryRecord.FromJson(json);

        Assert.NotNull(back);
        Assert.Equal(record.T, back!.T);
        Assert.Equal(record.RunId, back.RunId);
        Assert.Equal(record.TestName, back.TestName);
        Assert.Equal(record.CheckpointName, back.CheckpointName);
        Assert.Equal(record.Kind, back.Kind);
        Assert.Equal(record.Level, back.Level);
        Assert.Equal(record.Source, back.Source);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Serialize_KindEmittedAsString()
    {
        var record = new TelemetryRecord { Kind = TelemetryKind.Network };
        var json = record.ToJson();
        Assert.Contains("\"kind\":\"Network\"", json);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Serialize_NullFieldsOmitted()
    {
        var record = new TelemetryRecord { Kind = TelemetryKind.Log };
        var json = record.ToJson();
        Assert.DoesNotContain("\"runId\"", json);
        Assert.DoesNotContain("\"testName\"", json);
        Assert.DoesNotContain("\"checkpointName\"", json);
        Assert.DoesNotContain("\"level\"", json);
        Assert.DoesNotContain("\"source\"", json);
        Assert.DoesNotContain("\"data\"", json);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Serialize_PropertyNamesAreCamelCase()
    {
        var record = new TelemetryRecord
        {
            Kind = TelemetryKind.Console,
            TestName = "t",
            CheckpointName = "c",
            RunId = "r",
        };
        var json = record.ToJson();
        Assert.Contains("\"testName\":", json);
        Assert.Contains("\"checkpointName\":", json);
        Assert.Contains("\"runId\":", json);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Serialize_TimestampInIso8601()
    {
        var record = new TelemetryRecord { T = new DateTime(2026, 5, 24, 14, 23, 1, DateTimeKind.Utc) };
        var json = record.ToJson();
        // JsonSerializer's default DateTime format is ISO 8601 round-trip.
        Assert.Contains("2026-05-24T14:23:01", json);
    }
}
