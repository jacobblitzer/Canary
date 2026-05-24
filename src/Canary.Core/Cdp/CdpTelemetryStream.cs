using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Canary.Telemetry;

namespace Canary.Cdp;

// Enables the CDP domains Canary needs for telemetry capture and registers
// subscribers that translate CDP events into TelemetryRecords on a provided
// sink. Per design §C1 + impl §4.
//
// Shared between PenumbraBridgeAgent and QualiaBridgeAgent — both use the
// same browser+CDP stack so the producer code is identical aside from the
// `source` discriminator passed through to each TelemetryRecord.
public static class CdpTelemetryStream
{
    // Enables Console + Log + Network + Runtime, then registers subscribers
    // for Runtime.consoleAPICalled (page-JS console output), Log.entryAdded
    // (browser-internal warnings: CORS, deprecations, XHR errors, etc),
    // Network.responseReceived + Network.loadingFailed. Returns an
    // IDisposable that detaches all subscriptions when disposed.
    //
    // Runtime.enable is idempotent — both agents already call it; we call it
    // again here to make the helper self-sufficient.
    public static async Task<IDisposable> EnableAndSubscribeAsync(
        CdpClient cdp,
        ITelemetrySink sink,
        string source,
        CancellationToken ct = default)
    {
        if (cdp == null) throw new ArgumentNullException(nameof(cdp));
        if (sink == null) throw new ArgumentNullException(nameof(sink));

        await cdp.EnableDomainAsync("Runtime", ct).ConfigureAwait(false);
        await cdp.EnableDomainAsync("Console", ct).ConfigureAwait(false);
        await cdp.EnableDomainAsync("Log", ct).ConfigureAwait(false);
        await cdp.EnableDomainAsync("Network", ct).ConfigureAwait(false);

        var subs = new List<IDisposable>();

        // Per-request start times keyed by CDP requestId so we can populate
        // Network durationMs. ConcurrentDictionary so the subscribers can
        // run on the CDP read-loop thread without locking.
        var requestStartTicks = new ConcurrentDictionary<string, long>();

        subs.Add(cdp.Subscribe("Runtime.consoleAPICalled", payload =>
        {
            // payload shape:
            //   { type: "log"|"warning"|"error"|..., args: [{type,value}...],
            //     stackTrace: {...}, executionContextId, timestamp }
            var type = payload["type"]?.GetValue<string>() ?? "log";
            var text = JoinConsoleArgs(payload["args"]);
            var stack = payload["stackTrace"]?["callFrames"]?[0];
            sink.Write(new TelemetryRecord
            {
                Kind = TelemetryKind.Console,
                Source = source,
                Level = NormalizeLevel(type),
                Data = new ConsolePayload
                {
                    Text = text,
                    Type = type,
                    SourceUrl = stack?["url"]?.GetValue<string>(),
                    LineNumber = stack?["lineNumber"]?.GetValue<int>(),
                }
            });
        }));

        subs.Add(cdp.Subscribe("Log.entryAdded", payload =>
        {
            // payload.entry: { source, level, text, timestamp, url, ... }
            var entry = payload["entry"];
            if (entry == null) return;
            sink.Write(new TelemetryRecord
            {
                Kind = TelemetryKind.Console,
                Source = source,
                Level = NormalizeLevel(entry["level"]?.GetValue<string>()),
                Data = new ConsolePayload
                {
                    Text = entry["text"]?.GetValue<string>(),
                    Type = "browser-log",
                    SourceUrl = entry["url"]?.GetValue<string>(),
                    LineNumber = entry["lineNumber"]?.GetValue<int>(),
                    Category = entry["source"]?.GetValue<string>(),
                }
            });
        }));

        subs.Add(cdp.Subscribe("Network.requestWillBeSent", payload =>
        {
            var id = payload["requestId"]?.GetValue<string>();
            if (id != null) requestStartTicks[id] = Stopwatch.GetTimestamp();
        }));

        subs.Add(cdp.Subscribe("Network.responseReceived", payload =>
        {
            var id = payload["requestId"]?.GetValue<string>();
            var response = payload["response"];
            if (response == null) return;

            double? durationMs = null;
            if (id != null && requestStartTicks.TryRemove(id, out var startTicks))
                durationMs = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;

            var status = response["status"]?.GetValue<int>() ?? 0;
            sink.Write(new TelemetryRecord
            {
                Kind = TelemetryKind.Network,
                Source = source,
                Level = status >= 400 ? "warn" : "info",
                Data = new NetworkPayload
                {
                    Method = response["requestHeaders"]?["method"]?.GetValue<string>() ?? "GET",
                    Url = response["url"]?.GetValue<string>(),
                    Status = status,
                    DurationMs = durationMs,
                }
            });
        }));

        subs.Add(cdp.Subscribe("Network.loadingFailed", payload =>
        {
            var id = payload["requestId"]?.GetValue<string>();
            double? durationMs = null;
            if (id != null && requestStartTicks.TryRemove(id, out var startTicks))
                durationMs = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;

            sink.Write(new TelemetryRecord
            {
                Kind = TelemetryKind.Network,
                Source = source,
                Level = "error",
                Data = new NetworkPayload
                {
                    Url = null,
                    Status = 0,
                    DurationMs = durationMs,
                    ErrorText = payload["errorText"]?.GetValue<string>(),
                }
            });
        }));

        return new CompositeDisposable(subs);
    }

    private static string JoinConsoleArgs(JsonNode? args)
    {
        if (args is not JsonArray arr) return string.Empty;
        var parts = new List<string>(arr.Count);
        foreach (var a in arr)
        {
            if (a == null) continue;
            var value = a["value"];
            if (value != null) { parts.Add(value.ToJsonString().Trim('"')); continue; }
            var description = a["description"]?.GetValue<string>();
            if (description != null) { parts.Add(description); continue; }
            parts.Add(a.ToJsonString());
        }
        return string.Join(" ", parts);
    }

    private static string NormalizeLevel(string? raw) => raw?.ToLowerInvariant() switch
    {
        "log" or "info" or null => "info",
        "warn" or "warning" => "warn",
        "error" or "assert" => "error",
        "debug" or "verbose" or "trace" => "debug",
        _ => raw!,
    };
}

internal sealed class CompositeDisposable : IDisposable
{
    private readonly List<IDisposable> _items;
    private bool _disposed;
    public CompositeDisposable(List<IDisposable> items) { _items = items; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var i in _items) { try { i.Dispose(); } catch { } }
    }
}

public sealed class ConsolePayload
{
    public string? Text { get; init; }
    public string? Type { get; init; }
    public string? SourceUrl { get; init; }
    public int? LineNumber { get; init; }
    public string? Category { get; init; }
}

public sealed class NetworkPayload
{
    public string? Method { get; init; }
    public string? Url { get; init; }
    public int Status { get; init; }
    public double? DurationMs { get; init; }
    public string? ErrorText { get; init; }
}
