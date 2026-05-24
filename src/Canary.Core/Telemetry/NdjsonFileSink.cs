using System.Text;

namespace Canary.Telemetry;

// Writes TelemetryRecords as newline-delimited JSON to a file. One record
// per line; consumers (REPORT.md generator in Phase 3, Past Runs telemetry
// viewer in Phase 7, Claude reading via the MCP server in Phase 6) can
// stream-parse trivially.
//
// Per design doc §0.4 default: 500 KB per-line cap. Records longer than
// that are truncated with a `"truncated": true` marker appended; producers
// are responsible for splitting if they need the full content.
public sealed class NdjsonFileSink : ITelemetrySink, IDisposable
{
    public const int MaxLineBytes = 500 * 1024;

    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private bool _disposed;

    public string FilePath { get; }

    public NdjsonFileSink(string filePath, bool append = false)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        var stream = new FileStream(filePath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false,  // flushed under lock per-write to avoid losing lines on crash
        };
    }

    public void Write(TelemetryRecord record)
    {
        if (_disposed) return;
        string line;
        try
        {
            line = record.ToJson();
            if (Encoding.UTF8.GetByteCount(line) > MaxLineBytes)
            {
                // Truncate the line and replace with a marker. Producers
                // wanting full fidelity should split before sending.
                line = TruncateToBytes(line, MaxLineBytes - 64) + "...\"truncated\":true}";
            }
        }
        catch (Exception ex)
        {
            // Fall back to a minimal envelope so the line is at least valid
            // NDJSON and the consumer can flag the failure.
            line = $"{{\"t\":\"{DateTime.UtcNow:O}\",\"kind\":\"log\",\"level\":\"error\",\"source\":\"canary-harness\",\"data\":{{\"text\":\"telemetry-serialize-failed: {Escape(ex.Message)}\"}}}}";
        }

        lock (_lock)
        {
            if (_disposed) return;
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    private static string TruncateToBytes(string s, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= maxBytes) return s;
        // Step back to a valid UTF-8 boundary.
        int cut = maxBytes;
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;
        return Encoding.UTF8.GetString(bytes, 0, cut);
    }

    private static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    public void Dispose()
    {
        if (_disposed) return;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer.Flush(); } catch { }
            try { _writer.Dispose(); } catch { }
        }
    }
}

// Fan-out sink for live UI consumers. Subscribers are invoked
// synchronously on the writer's thread — consumers must marshal to UI
// thread themselves if needed (Form.BeginInvoke / equivalent).
public sealed class EventStreamSink : ITelemetrySink
{
    public event Action<TelemetryRecord>? OnRecord;

    public void Write(TelemetryRecord record)
    {
        try { OnRecord?.Invoke(record); } catch { /* one bad subscriber shouldn't kill the writer */ }
    }
}
