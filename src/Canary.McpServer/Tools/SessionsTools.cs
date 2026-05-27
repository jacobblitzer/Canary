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
