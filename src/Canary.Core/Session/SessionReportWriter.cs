using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Canary.Session;

public static class SessionReportWriter
{
    public static void Write(string sessionDir, SessionData session)
    {
        Directory.CreateDirectory(sessionDir);
        AtomicWriteAllText(
            SessionPaths.SessionJsonPath(sessionDir),
            JsonSerializer.Serialize(session, JsonOptions));
        AtomicWriteAllText(
            SessionPaths.ReportPath(sessionDir),
            BuildMarkdown(session, sessionDir));
    }

    public static string BuildMarkdown(SessionData s, string? sessionDir = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: \"Supervised session — {s.Workload} {s.StartedAtUtc:yyyy-MM-dd HH:mm}\"");
        sb.AppendLine($"sessionId: {s.SessionId}");
        sb.AppendLine($"workload: {s.Workload}");
        if (!string.IsNullOrEmpty(s.Url)) sb.AppendLine($"url: {s.Url}");
        sb.AppendLine($"startedAt: {s.StartedAtUtc:O}");
        if (s.EndedAtUtc.HasValue)
        {
            sb.AppendLine($"endedAt: {s.EndedAtUtc.Value:O}");
            var dur = (s.EndedAtUtc.Value - s.StartedAtUtc).TotalSeconds;
            sb.AppendLine($"durationSeconds: {(int)dur}");
        }
        sb.AppendLine($"captureCount: {s.Captures.Count}");
        var annotated = s.Captures.Count(c => !string.IsNullOrEmpty(c.AnnotatedPngFile));
        sb.AppendLine($"annotatedCount: {annotated}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# Supervised session — {s.Workload} ({s.StartedAtUtc:yyyy-MM-dd HH:mm})");
        sb.AppendLine();
        sb.AppendLine("## Close-out notes");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(s.CloseoutNotes) ? "_(none)_" : s.CloseoutNotes);
        sb.AppendLine();
        sb.AppendLine("## Captures");
        sb.AppendLine();
        if (s.Captures.Count == 0)
        {
            sb.AppendLine("_(no captures)_");
            sb.AppendLine();
        }
        else
        {
            foreach (var c in s.Captures)
            {
                var displaySlug = string.IsNullOrEmpty(c.Slug) ? "(no annotation)" : c.Slug;
                sb.AppendLine($"### {c.Sequence:D3} — {c.CapturedAtUtc:HH:mm:ss} — {displaySlug}");
                sb.AppendLine();
                var imageRel = !string.IsNullOrEmpty(c.AnnotatedPngFile)
                    ? $"{SessionPaths.CapturesSubdir}/{c.AnnotatedPngFile}"
                    : $"{SessionPaths.CapturesSubdir}/{c.PngFile}";
                sb.AppendLine($"![]({imageRel})");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(c.NoteBody))
                {
                    sb.AppendLine("> " + c.NoteBody.Replace("\n", "\n> "));
                    sb.AppendLine();
                }
                var links = new List<string>
                {
                    $"[source PNG]({SessionPaths.CapturesSubdir}/{c.PngFile})"
                };
                if (!string.IsNullOrEmpty(c.AnnotatedPngFile))
                    links.Add($"[annotated PNG]({SessionPaths.CapturesSubdir}/{c.AnnotatedPngFile})");
                if (!string.IsNullOrEmpty(c.AnnotationsJsonFile))
                    links.Add($"[annotations.json]({SessionPaths.CapturesSubdir}/{c.AnnotationsJsonFile})");
                sb.AppendLine(string.Join(" · ", links));
                sb.AppendLine();
            }
        }

        // Penumbra preview telemetry (v2): events the Penumbra Rhino plug-in emitted during this session,
        // tailed into telemetry.ndjson by RhinoSessionAgent — the Rhino analogue of the CDP Console stream
        // (scene.loaded with +tape/+grid + bounds, gl.field.transform for gumball moves, rep.live for display-
        // rep switches, frame.real, render.error). Lets the agent debug a hand-driven session from the report.
        if (sessionDir != null)
        {
            sb.AppendLine("## Penumbra preview telemetry");
            sb.AppendLine();
            var events = ReadPenumbraEvents(SessionPaths.TelemetryPath(sessionDir));
            if (events.Count == 0)
            {
                sb.AppendLine("_(no Penumbra preview events captured — the Penumbra Rhino plug-in wasn't loaded, or nothing rendered this session)_");
            }
            else
            {
                sb.AppendLine($"{events.Count} event(s) · full stream: [{SessionPaths.TelemetryNdjsonFileName}]({SessionPaths.TelemetryNdjsonFileName})");
                sb.AppendLine();
                sb.AppendLine("```");
                const int max = 80;
                int start = Math.Max(0, events.Count - max);
                if (start > 0) sb.AppendLine($"… {start} earlier event(s) omitted; see {SessionPaths.TelemetryNdjsonFileName} …");
                for (int i = start; i < events.Count; i++) sb.AppendLine(events[i]);
                sb.AppendLine("```");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Read the Penumbra-sourced records from a session's telemetry.ndjson and format each as a
    /// compact "HH:mm:ss [level] event payload" line for the SESSION_REPORT. Robust to partial/garbled lines.</summary>
    private static List<string> ReadPenumbraEvents(string telemetryPath)
    {
        var lines = new List<string>();
        if (!File.Exists(telemetryPath)) return lines;
        string[] raw;
        try { raw = File.ReadAllLines(telemetryPath); } catch { return lines; }
        foreach (var line in raw)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonNode.Parse(line) is not JsonObject o) continue;
                if (TryStr(o["source"]) != "penumbra") continue;
                var data = o["data"] as JsonObject;
                string evt = TryStr(data?["event"]) ?? "event";
                string payload = data?["payload"]?.ToJsonString() ?? "";
                if (payload.Length > 300) payload = payload.Substring(0, 300) + "…";
                string level = TryStr(o["level"]) ?? "info";
                string lvl = level == "info" ? "" : $"[{level}] ";
                string ts = "";
                var t = TryStr(o["t"]);
                if (t != null && DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                    ts = dt.ToLocalTime().ToString("HH:mm:ss");
                lines.Add($"{ts}  {lvl}{evt}  {payload}".TrimEnd());
            }
            catch { /* skip a garbled line */ }
        }
        return lines;
    }

    private static string? TryStr(JsonNode? node)
    {
        try { return node?.GetValue<string>(); } catch { return null; }
    }

    public static SessionData? TryReadJson(string sessionDir)
    {
        var path = SessionPaths.SessionJsonPath(sessionDir);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<SessionData>(File.ReadAllText(path), JsonOptions); }
        catch { return null; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static void AtomicWriteAllText(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Encoding.UTF8);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}
