using System.Globalization;
using System.Text.Json.Nodes;

namespace Canary.Telemetry;

/// <summary>
/// Tails Penumbra's in-Rhino preview telemetry NDJSON and forwards each appended event to an
/// <see cref="ITelemetrySink"/> — the Rhino-side analogue of the Qualia/Penumbra CDP Console stream. The
/// Penumbra Rhino plug-in writes domain events (host.start, scene.loaded, frame.real, gl.field.transform,
/// rep.live, render.error) via <c>Penumbra.Bridge.NdjsonLog</c> to
/// <c>%LocalAppData%\Penumbra\preview\telemetry.ndjson</c>, envelope <c>{t, kind, level, source, data}</c>.
///
/// Penumbra's free-form <c>kind</c> doesn't map onto Canary's fixed <see cref="TelemetryKind"/> enum, so each
/// event is captured as <c>Kind=Log, Source="penumbra"</c> with the domain kind in <c>Data.event</c> and the
/// payload in <c>Data.payload</c> — losslessly, for the session REPORT + the agent's debugging.
///
/// This is a POLLING tail (the file is appended by another process — the Penumbra plug-in in Rhino): it opens
/// shared-read, baselines at the file's CURRENT end on <see cref="Start"/> (so only events from session start
/// are captured, not stale ones from a prior run), and forwards newly-appended complete lines until disposed.
/// </summary>
public sealed class PenumbraPreviewTelemetryTail : IDisposable
{
    /// <summary>The default location the Penumbra Rhino plug-in writes its preview telemetry to.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Penumbra", "preview", "telemetry.ndjson");

    private readonly ITelemetrySink _sink;
    private readonly string _path;
    private readonly string? _runId;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private PenumbraPreviewTelemetryTail(ITelemetrySink sink, string path, string? runId)
    {
        _sink = sink;
        _path = path;
        _runId = runId;
        _loop = Task.Run(() => TailLoopAsync(_cts.Token));
    }

    /// <summary>Start tailing (from the file's current end). Returns a handle; Dispose to stop. Safe to call
    /// even if the file doesn't exist yet — it picks up once the Penumbra plug-in starts writing.</summary>
    public static PenumbraPreviewTelemetryTail Start(ITelemetrySink sink, string? path = null, string? runId = null)
        => new(sink ?? NullTelemetrySink.Instance, path ?? DefaultPath, runId);

    private async Task TailLoopAsync(CancellationToken ct)
    {
        int processed = -1;   // -1 = not yet baselined; else count of COMPLETE lines already consumed
        int retrySameLine = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(_path))
                {
                    string content = ReadAllSharedText(_path);
                    var parts = content.Split('\n');
                    // The final Split element is either "" (content ends with '\n') or a partial
                    // still-being-written line — EITHER WAY it is not a complete line, so the
                    // complete-line count is parts.Length - 1 unconditionally. The old
                    // `EndsWith('\n') ? parts.Length : parts.Length - 1` overcounted by one for
                    // newline-terminated content, which skewed `processed` one PAST the next
                    // unseen line — in any multi-line burst the earlier lines were never visited
                    // (R1.6 catch, 2026-07-03: cpig.session.snapshot vanished from two live
                    // sessions while its 4ms-later rhino.command neighbor landed; unit repro
                    // TailSnapshotReproTests).
                    int complete = parts.Length - 1;
                    if (complete < 0) complete = 0;

                    if (processed < 0)
                    {
                        processed = complete;                 // baseline: skip events that predate the session
                    }
                    else
                    {
                        if (complete < processed) { processed = 0; retrySameLine = 0; }   // truncated / rotated → re-read from start
                        bool stalled = false;
                        int i = processed;
                        for (; i < complete; i++)
                        {
                            var line = parts[i];
                            var rec = ParsePenumbraLine(line);
                            if (rec == null && line.Trim().Length > 0)
                            {
                                // R1.6 catch (2026-07-03): a shared read can observe a line TORN
                                // mid-write; the old code advanced past it unconditionally, silently
                                // dropping the record forever (a cpig.session.snapshot vanished this
                                // way while its 4ms-later neighbor landed). Stop at the unparseable
                                // line and re-read it next tick — the write will have completed.
                                // Persistently malformed lines get 3 attempts, then are skipped so
                                // genuine garbage can't wedge the tail.
                                if (retrySameLine < 3) { retrySameLine++; stalled = true; break; }
                                retrySameLine = 0;   // give up on this line, move on
                                continue;
                            }
                            retrySameLine = 0;
                            if (rec != null)
                            {
                                try { _sink.Write(rec); } catch { /* one bad write shouldn't kill the tail */ }
                            }
                        }
                        processed = stalled ? i : complete;
                    }
                }
            }
            catch { /* transient IO (locked mid-write, rotating) — retry next tick */ }

            try { await Task.Delay(400, ct).ConfigureAwait(false); } catch { break; }
        }
    }

    private TelemetryRecord? ParsePenumbraLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return null;
        try
        {
            if (JsonNode.Parse(line) is not JsonObject obj) return null;
            string kind = TryStr(obj["kind"]) ?? "event";
            string level = TryStr(obj["level"]) ?? "info";
            DateTime t = DateTime.UtcNow;
            var ts = TryStr(obj["t"]);
            if (ts != null && DateTime.TryParse(ts, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                t = parsed;
            var data = obj["data"]?.DeepClone();
            // Penumbra's real event name is its data.phase (its top-level `kind` is the coarse "Log" category);
            // surface phase as the event so the SESSION_REPORT reads "gl.scene.loaded", not "Log".
            string evt = TryStr((data as JsonObject)?["phase"]) ?? kind;
            return new TelemetryRecord
            {
                T = t,
                RunId = _runId,
                Kind = TelemetryKind.Log,
                Level = level,
                Source = "penumbra",
                Data = new JsonObject { ["event"] = evt, ["payload"] = data },
            };
        }
        catch { return null; }
    }

    private static string? TryStr(JsonNode? node)
    {
        try { return node?.GetValue<string>(); } catch { return node?.ToJsonString(); }
    }

    private static string ReadAllSharedText(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _cts.Dispose(); } catch { }
    }
}
