using System.Text.Json;
using System.Text.Json.Nodes;
using Canary.Session;

namespace Canary.McpServer.Tools;

// MCP tools for the supervised-session feature (Phase 3 of the
// supervised-session implementation, shipping alongside the Phase 2
// UI surface). Mirrors RunsTools.cs but reads workloads/<w>/sessions/
// instead of workloads/<w>/results/.

internal sealed class ListSessionsTool : McpTool
{
    public override string Name => "list_sessions";
    public override string Description => "List recent Canary supervised sessions across workloads. Each session corresponds to a workloads/<w>/sessions/<yyyyMMdd-HHmmss-xxxx>/ dir produced by `canary session start`.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "workload": { "type": "string", "description": "Filter by workload name (qualia, penumbra). Omit for all." },
            "limit":    { "type": "integer", "description": "Max sessions to return (most recent first); default 25." }
          },
          "required": []
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var workloadFilter = args["workload"]?.GetValue<string>();
        var limit = args["limit"]?.GetValue<int>() ?? 25;

        var root = WorkloadsRoot.Discover();
        if (!Directory.Exists(root))
            return Task.FromResult($"No workloads dir at: {root}");

        var rows = new List<object>();

        foreach (var workloadDir in Directory.EnumerateDirectories(root))
        {
            var workloadName = Path.GetFileName(workloadDir);
            if (workloadFilter != null && !string.Equals(workloadName, workloadFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var sessionsDir = Path.Combine(workloadDir, SessionPaths.SessionsSubdir);
            if (!Directory.Exists(sessionsDir)) continue;

            foreach (var sDir in Directory.EnumerateDirectories(sessionsDir))
            {
                var data = SessionReportWriter.TryReadJson(sDir);
                if (data == null) continue;
                var annotated = data.Captures.Count(c => !string.IsNullOrEmpty(c.AnnotatedPngFile));
                rows.Add(new
                {
                    sessionId = data.SessionId,
                    workload = workloadName,
                    startedAt = data.StartedAtUtc.ToString("o"),
                    endedAt = data.EndedAtUtc?.ToString("o"),
                    durationSeconds = data.EndedAtUtc.HasValue
                        ? (int)(data.EndedAtUtc.Value - data.StartedAtUtc).TotalSeconds
                        : (int?)null,
                    captureCount = data.Captures.Count,
                    annotatedCount = annotated,
                    sessionDir = sDir,
                    reportPath = SessionPaths.ReportPath(sDir),
                });
            }
        }

        var sorted = rows
            .Cast<dynamic>()
            .OrderByDescending(r => (string)r.startedAt)
            .Take(limit)
            .ToArray();

        return Task.FromResult(JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class GetSessionReportTool : McpTool
{
    public override string Name => "get_session_report";
    public override string Description => "Fetch SESSION_REPORT.md contents (Markdown text) for a specific supervised session by its session id.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "sessionId": { "type": "string", "description": "The yyyyMMdd-HHmmss-xxxx session id." }
          },
          "required": ["sessionId"]
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var sessionId = args["sessionId"]?.GetValue<string>() ?? throw new ArgumentException("sessionId is required");
        var root = WorkloadsRoot.Discover();
        if (!Directory.Exists(root))
            return Task.FromResult($"No workloads dir at: {root}");

        foreach (var workloadDir in Directory.EnumerateDirectories(root))
        {
            var sessionDir = Path.Combine(workloadDir, SessionPaths.SessionsSubdir, sessionId);
            var report = SessionPaths.ReportPath(sessionDir);
            if (File.Exists(report))
            {
                var content = File.ReadAllText(report);
                return Task.FromResult($"# Session {sessionId}\n\nPath: {report}\n\n---\n\n{content}");
            }
        }

        return Task.FromResult($"No SESSION_REPORT.md found for session id '{sessionId}'.");
    }
}

// R1.6 flight-recorder Phase D (2026-07-03) — the two tools that make a session folder
// fully readable from any AI coding agent without shelling out: the manifest (identity: opened file
// + SHA, machine, PID, env decisions, exit record, harvested Penumbra SHAs) and the raw
// telemetry stream (filterable by domain-event prefix, because tailed Penumbra/CPig records
// are Kind=Log with the domain kind nested at Data.event).

internal sealed class GetSessionManifestTool : McpTool
{
    public override string Name => "get_session_manifest";
    public override string Description => "Fetch manifest.json for a supervised session by id: opened file + SHA256, machine, app+PID, launcher env decisions (incl. PENUMBRA_SESSION_REF), exit record (diedUnexpectedly on kill/crash), and harvested Penumbra identity (plugin/bundle SHAs, skew verdict, GPU).";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "sessionId": { "type": "string", "description": "The yyyyMMdd-HHmmss-xxxx session id." }
          },
          "required": ["sessionId"]
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var sessionId = args["sessionId"]?.GetValue<string>() ?? throw new ArgumentException("sessionId is required");
        var root = WorkloadsRoot.Discover();
        if (!Directory.Exists(root))
            return Task.FromResult($"No workloads dir at: {root}");

        foreach (var workloadDir in Directory.EnumerateDirectories(root))
        {
            var sessionDir = Path.Combine(workloadDir, SessionPaths.SessionsSubdir, sessionId);
            var manifest = SessionPaths.ManifestPath(sessionDir);
            if (File.Exists(manifest))
                return Task.FromResult(File.ReadAllText(manifest));
        }

        return Task.FromResult($"No manifest.json found for session id '{sessionId}' (pre-flight-recorder session, or wrong id).");
    }
}

internal sealed class GetSessionTelemetryTool : McpTool
{
    public override string Name => "get_session_telemetry";
    public override string Description => "Read a supervised session's telemetry.ndjson (raw NDJSON lines), optionally filtered by event prefix. Tailed Penumbra/CPig records are Kind=Log with the domain kind at Data.event (e.g. gl.scene.snapshot, cpig.push.done) — the filter matches Data.event first and falls back to the record Kind. Returns the LAST N matches (tail).";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "sessionId":   { "type": "string", "description": "The yyyyMMdd-HHmmss-xxxx session id." },
            "eventPrefix": { "type": "string", "description": "Case-insensitive prefix filter on Data.event (fallback: record Kind). E.g. 'cpig.push', 'gl.scene.snapshot', 'gl.fsm'. Omit for all records." },
            "tail":        { "type": "integer", "description": "Return only the LAST N matching lines; default 200, max 2000." },
            "prior":       { "type": "boolean", "description": "Read telemetry-prior.ndjson (the PREVIOUS session's rescued Penumbra log) instead of telemetry.ndjson." }
          },
          "required": ["sessionId"]
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var sessionId = args["sessionId"]?.GetValue<string>() ?? throw new ArgumentException("sessionId is required");
        var eventPrefix = args["eventPrefix"]?.GetValue<string>();
        var tail = Math.Min(Math.Max(args["tail"]?.GetValue<int>() ?? 200, 1), 2000);
        var prior = args["prior"]?.GetValue<bool>() ?? false;

        var root = WorkloadsRoot.Discover();
        if (!Directory.Exists(root))
            return Task.FromResult($"No workloads dir at: {root}");

        foreach (var workloadDir in Directory.EnumerateDirectories(root))
        {
            var sessionDir = Path.Combine(workloadDir, SessionPaths.SessionsSubdir, sessionId);
            var file = prior
                ? Path.Combine(sessionDir, SessionPaths.TelemetryPriorFileName)
                : SessionPaths.TelemetryPath(sessionDir);
            if (!File.Exists(file)) continue;

            var matches = new List<string>();
            int total = 0, malformed = 0;
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                total++;
                if (eventPrefix == null) { matches.Add(line); continue; }
                string? evt = null, kind = null;
                try
                {
                    var node = JsonNode.Parse(line);
                    kind = node?["kind"]?.GetValue<string>();
                    evt = node?["data"]?["event"]?.GetValue<string>();
                }
                catch { malformed++; continue; }
                var key = evt ?? kind ?? string.Empty;
                if (key.StartsWith(eventPrefix, StringComparison.OrdinalIgnoreCase))
                    matches.Add(line);
            }

            var start = Math.Max(0, matches.Count - tail);
            var shown = matches.Skip(start).ToList();
            var header =
                $"# session {sessionId} — {Path.GetFileName(file)}: {total} record(s), " +
                $"{matches.Count} match(es) for prefix '{eventPrefix ?? "(none)"}'" +
                (start > 0 ? $", showing the LAST {shown.Count} (raise tail= for more)" : "") +
                (malformed > 0 ? $", {malformed} malformed line(s) skipped" : "") +
                "\n";
            return Task.FromResult(header + string.Join("\n", shown));
        }

        return Task.FromResult($"No {(prior ? SessionPaths.TelemetryPriorFileName : SessionPaths.TelemetryNdjsonFileName)} found for session id '{sessionId}'.");
    }
}
