using Canary.Telemetry;
using Xunit;

namespace Canary.Tests.Telemetry;

// R1.6 repro: the cpig.session.snapshot line vanished from two live sessions while its
// 4ms-later rhino.command neighbor landed. Feed the tail the EXACT production lines.
[Trait("Category", "Unit")]
public class TailSnapshotReproTests
{
    private sealed class RecordingSink : ITelemetrySink
    {
        public readonly List<TelemetryRecord> Records = new();
        public void Write(TelemetryRecord record) { lock (Records) Records.Add(record); }
    }

    [Fact]
    public async Task Tail_DeliversSnapshotAndCommand_WrittenAfterBaseline()
    {
        var path = Path.Combine(Path.GetTempPath(), "tail-repro-" + Guid.NewGuid().ToString("N") + ".ndjson");
        File.WriteAllText(path, "{\"kind\":\"Log\",\"t\":\"2026-07-03T20:24:00Z\",\"data\":{\"phase\":\"pre.baseline\"}}\n");
        var sink = new RecordingSink();
        using var tail = PenumbraPreviewTelemetryTail.Start(sink, path, "repro");
        await Task.Delay(900);   // let it baseline

        File.AppendAllText(path,
            "{\"kind\":\"Log\",\"level\":\"info\",\"source\":\"rhino\",\"t\":\"2026-07-03T20:25:46.4506134Z\",\"data\":{\"reason\":\"scripted\",\"fieldCount\":0,\"liveHandles\":0,\"fields\":[],\"phase\":\"cpig.session.snapshot\"},\"ref\":\"20260703-202431-764c\"}\n" +
            "{\"kind\":\"Log\",\"level\":\"info\",\"source\":\"rhino\",\"t\":\"2026-07-03T20:25:46.4543838Z\",\"data\":{\"command\":\"CPigDumpState\",\"result\":\"Success\",\"durationMs\":3.9,\"phase\":\"rhino.command\"},\"ref\":\"20260703-202431-764c\"}\n");
        await Task.Delay(1200);  // >2 poll ticks

        List<string?> events;
        lock (sink.Records)
            events = sink.Records.Select(r => (r.Data as System.Text.Json.Nodes.JsonObject)?["event"]?.GetValue<string>()).ToList();
        Assert.Contains("cpig.session.snapshot", events);
        Assert.Contains("rhino.command", events);
    }
}
