using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Canary.Session;

public static class SessionReportWriter
{
    public static void Write(string sessionDir, SessionData session)
        => Write(sessionDir, session, manifest: null);

    public static void Write(string sessionDir, SessionData session, SessionManifest? manifest)
    {
        Directory.CreateDirectory(sessionDir);
        AtomicWriteAllText(
            SessionPaths.SessionJsonPath(sessionDir),
            JsonSerializer.Serialize(session, JsonOptions));
        if (manifest != null)
        {
            try { SessionManifestWriter.Write(sessionDir, manifest); } catch { }
        }
        AtomicWriteAllText(
            SessionPaths.ReportPath(sessionDir),
            BuildMarkdown(session, sessionDir, manifest));
    }

    public static string BuildMarkdown(SessionData s, string? sessionDir = null, SessionManifest? manifest = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: \"Supervised session — {s.Workload} {s.StartedAtUtc:yyyy-MM-dd HH:mm}\"");
        sb.AppendLine($"sessionId: {s.SessionId}");
        sb.AppendLine($"workload: {s.Workload}");
        if (!string.IsNullOrEmpty(s.Url)) sb.AppendLine($"url: {s.Url}");
        if (!string.IsNullOrEmpty(s.OpenedFile)) sb.AppendLine($"openedFile: {s.OpenedFile}");
        if (manifest?.SessionRef != null) sb.AppendLine($"sessionRef: {manifest.SessionRef}");
        if (manifest is { ExitedBeforeCloseout: true })
        {
            sb.AppendLine($"exitedBeforeCloseout: true");
            sb.AppendLine($"diedUnexpectedly: {(manifest.DiedUnexpectedly ? "true" : "false")}");
        }
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
        AppendManifestSection(sb, s, manifest, sessionDir);
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
                if (!string.IsNullOrEmpty(c.ActiveView) || !string.IsNullOrEmpty(c.FrameStatus))
                {
                    var bits = new List<string>();
                    if (!string.IsNullOrEmpty(c.ActiveView)) bits.Add($"view={c.ActiveView}");
                    if (!string.IsNullOrEmpty(c.FrameStatus)) bits.Add($"frame: {c.FrameStatus}");
                    sb.AppendLine($"_{string.Join(" · ", bits)}_");
                    sb.AppendLine();
                }
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

    /// <summary>Session manifest summary + debug pointers (flight-recorder Phase A) — the first
    /// thing a debugging agent reads: what ran, on what, with what env, and where the evidence is.</summary>
    private static void AppendManifestSection(StringBuilder sb, SessionData s, SessionManifest? m, string? sessionDir)
    {
        if (m == null && sessionDir == null) return;

        if (m != null)
        {
            sb.AppendLine("## Session manifest");
            sb.AppendLine();
            if (m.ExitedBeforeCloseout)
            {
                sb.AppendLine(m.DiedUnexpectedly
                    ? $"> **⚠ App DIED before close-out** — exit code {m.ProcessExitCode} at {m.ProcessExitAtUtc:O}. The telemetry below runs up to the moment of death."
                    : $"> App exited cleanly before close-out (exit 0 at {m.ProcessExitAtUtc:O}) — likely closed by the operator.");
                sb.AppendLine();
            }
            sb.AppendLine("| Field | Value |");
            sb.AppendLine("|---|---|");
            if (m.SessionRef != null) sb.AppendLine($"| sessionRef (`PENUMBRA_SESSION_REF`) | `{m.SessionRef}` |");
            if (m.OpenedFile != null) sb.AppendLine($"| opened file | `{m.OpenedFile}` |");
            if (m.OpenedFileSha256 != null) sb.AppendLine($"| file sha256 | `{m.OpenedFileSha256}` |");
            if (m.MachineName != null) sb.AppendLine($"| machine | {m.MachineName} |");
            if (m.AppPath != null) sb.AppendLine($"| app | `{m.AppPath}` (pid {m.ProcessId?.ToString() ?? "?"}) |");
            if (m.Harvested != null)
            {
                foreach (var kv in m.Harvested.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| {kv.Key} (harvested) | `{kv.Value}` |");
            }
            else if (string.Equals(m.Workload, "rhino", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("| harvested SHAs/GPU | _(none — no Penumbra scene push happened this session; SHAs arrive with the first push until the Phase B banner fix)_ |");
            }
            if (m.AppliedEnv is { Count: > 0 })
            {
                sb.AppendLine($"| launcher env decisions | {string.Join(", ", m.AppliedEnv.Select(kv => $"`{kv.Key}={Truncate(kv.Value, 60)}`"))} |");
            }
            sb.AppendLine();
        }

        if (sessionDir != null)
        {
            sb.AppendLine("## Debug pointers");
            sb.AppendLine();
            sb.AppendLine($"- Telemetry (this session): [{SessionPaths.TelemetryNdjsonFileName}]({SessionPaths.TelemetryNdjsonFileName})");
            if (File.Exists(SessionPaths.TelemetryPriorPath(sessionDir)))
                sb.AppendLine($"- Telemetry (PREVIOUS session, rescued pre-launch): [{SessionPaths.TelemetryPriorFileName}]({SessionPaths.TelemetryPriorFileName})");
            if (File.Exists(SessionPaths.ManifestPath(sessionDir)))
                sb.AppendLine($"- Machine-readable manifest: [{SessionPaths.ManifestFileName}]({SessionPaths.ManifestFileName})");
            sb.AppendLine($"- Captures: [{SessionPaths.CapturesSubdir}/]({SessionPaths.CapturesSubdir}/)");
            sb.AppendLine();
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max) + "…";

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
