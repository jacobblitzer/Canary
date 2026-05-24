namespace Canary.Telemetry;

// Thread-safe, non-blocking sink for TelemetryRecord per design §C1.
// Producers fire records synchronously on the test thread; a slow Write
// implementation stalls the suite. Sinks should buffer or async-dispatch
// internally if they need to do real I/O.
public interface ITelemetrySink
{
    void Write(TelemetryRecord record);
}

// No-op sink. Used as the default when TestRunner hasn't wired anything
// yet, so producers can call Write unconditionally without null-checks.
public sealed class NullTelemetrySink : ITelemetrySink
{
    public static readonly NullTelemetrySink Instance = new();
    public void Write(TelemetryRecord record) { }
}

// Fans a single Write to every wrapped sink in order. Failures in one
// sink do not prevent the others from receiving the record.
public sealed class CompositeTelemetrySink : ITelemetrySink
{
    private readonly ITelemetrySink[] _sinks;

    public CompositeTelemetrySink(params ITelemetrySink[] sinks)
    {
        _sinks = sinks ?? Array.Empty<ITelemetrySink>();
    }

    public void Write(TelemetryRecord record)
    {
        foreach (var s in _sinks)
        {
            try { s.Write(record); } catch { /* one bad sink shouldn't break the rest */ }
        }
    }
}

// Implementing this on an ICanaryAgent lets TestRunner inject a sink after
// construction. Per design §C1 — Penumbra + Qualia agents implement it;
// Rhino agent's interception is deferred to a v2 follow-up so it does not.
public interface ITelemetryAware
{
    void RegisterTelemetrySink(ITelemetrySink sink);
}
