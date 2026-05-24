using Canary.Cdp;
using Canary.Telemetry;
using Xunit;

namespace Canary.Tests.Telemetry;

// Unit tests for NdjsonFileSink — the per-suite file writer that backs
// the C1 telemetry envelope's persistence layer. Verifies line-per-record
// shape, thread safety, truncation, and CompositeTelemetrySink fan-out.
public class NdjsonFileSinkTests : IDisposable
{
    private readonly string _tempDir;

    public NdjsonFileSinkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "canary-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Write_OneRecord_ProducesOneLine()
    {
        var path = Path.Combine(_tempDir, "single.ndjson");
        using (var sink = new NdjsonFileSink(path))
        {
            sink.Write(new TelemetryRecord { Kind = TelemetryKind.Console, Source = "test" });
        }

        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
        Assert.Contains("\"kind\":\"Console\"", lines[0]);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Write_MultipleRecords_OneRecordPerLine()
    {
        var path = Path.Combine(_tempDir, "multi.ndjson");
        using (var sink = new NdjsonFileSink(path))
        {
            sink.Write(new TelemetryRecord { Kind = TelemetryKind.Console });
            sink.Write(new TelemetryRecord { Kind = TelemetryKind.Network });
            sink.Write(new TelemetryRecord { Kind = TelemetryKind.Log });
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l => Assert.StartsWith("{", l));
        Assert.All(lines, l => Assert.EndsWith("}", l));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task Write_ThreadSafe_NoInterleaving()
    {
        var path = Path.Combine(_tempDir, "concurrent.ndjson");
        const int writers = 8;
        const int per = 250;

        using (var sink = new NdjsonFileSink(path))
        {
            var tasks = Enumerable.Range(0, writers).Select(w => Task.Run(() =>
            {
                for (int i = 0; i < per; i++)
                {
                    sink.Write(new TelemetryRecord
                    {
                        Kind = TelemetryKind.Console,
                        Source = $"writer-{w}",
                        Data = new ConsolePayload { Text = $"line-{i}" },
                    });
                }
            })).ToArray();

            await Task.WhenAll(tasks);
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(writers * per, lines.Length);
        // Every line should parse as a valid TelemetryRecord — no torn writes.
        foreach (var line in lines)
        {
            var record = TelemetryRecord.FromJson(line);
            Assert.NotNull(record);
            Assert.Equal(TelemetryKind.Console, record!.Kind);
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void CompositeTelemetrySink_FansToAllWrappedSinks()
    {
        var got1 = new List<TelemetryRecord>();
        var got2 = new List<TelemetryRecord>();
        var s1 = new CallbackSink(got1.Add);
        var s2 = new CallbackSink(got2.Add);
        var composite = new CompositeTelemetrySink(s1, s2);

        var r = new TelemetryRecord { Kind = TelemetryKind.Log };
        composite.Write(r);

        Assert.Single(got1);
        Assert.Single(got2);
        Assert.Same(r, got1[0]);
        Assert.Same(r, got2[0]);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void CompositeTelemetrySink_OneSinkThrows_OthersStillReceive()
    {
        var got2 = new List<TelemetryRecord>();
        var s1 = new CallbackSink(_ => throw new InvalidOperationException("boom"));
        var s2 = new CallbackSink(got2.Add);
        var composite = new CompositeTelemetrySink(s1, s2);

        composite.Write(new TelemetryRecord { Kind = TelemetryKind.Log });

        Assert.Single(got2);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void NullTelemetrySink_AcceptsWriteSilently()
    {
        NullTelemetrySink.Instance.Write(new TelemetryRecord { Kind = TelemetryKind.Log });
        // No assertion; the test passes if no exception is thrown.
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void EventStreamSink_RaisesOnRecord()
    {
        var captured = new List<TelemetryRecord>();
        var sink = new EventStreamSink();
        sink.OnRecord += captured.Add;

        var r = new TelemetryRecord { Kind = TelemetryKind.Screenshot };
        sink.Write(r);

        Assert.Single(captured);
        Assert.Same(r, captured[0]);
    }

    private sealed class CallbackSink : ITelemetrySink
    {
        private readonly Action<TelemetryRecord> _cb;
        public CallbackSink(Action<TelemetryRecord> cb) { _cb = cb; }
        public void Write(TelemetryRecord record) => _cb(record);
    }
}
