using System.Text.Json;
using System.Text.Json.Nodes;

namespace Canary.McpServer.Tools;

// MCP tools for the Phase 3 per-run dir layout. list_recent_runs walks
// every workloads/*/results/.../runs/<timestamp>/ to surface the most
// recent test runs across all workloads. get_run_report reads the
// REPORT.md for a specific run.
internal static class WorkloadsRoot
{
    public static string Discover()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "workloads");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "workloads");
    }
}

internal sealed class ListRecentRunsTool : McpTool
{
    public override string Name => "list_recent_runs";
    public override string Description => "List recent Canary test runs across workloads. Each run corresponds to a runs/<timestamp>/ dir produced by Phase 3 (REPORT.md + result.json per run).";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "workload": { "type": "string", "description": "Filter by workload name (qualia, penumbra, rhino, ...). Omit for all." },
            "verdict":  { "type": "string", "enum": ["Passed", "Failed", "Crashed", "New"], "description": "Filter by parsed verdict from REPORT.md header. Omit for all." },
            "limit":    { "type": "integer", "description": "Max runs to return (most recent first); default 25." }
          },
          "required": []
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var workloadFilter = args["workload"]?.GetValue<string>();
        var verdictFilter = args["verdict"]?.GetValue<string>();
        var limit = args["limit"]?.GetValue<int>() ?? 25;

        var root = WorkloadsRoot.Discover();
        if (!Directory.Exists(root))
            return Task.FromResult($"No workloads dir at: {root}");

        var runs = new List<object>();

        foreach (var workloadDir in Directory.EnumerateDirectories(root))
        {
            var workloadName = Path.GetFileName(workloadDir);
            if (workloadFilter != null && !string.Equals(workloadName, workloadFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            var resultsDir = Path.Combine(workloadDir, "results");
            if (!Directory.Exists(resultsDir)) continue;

            foreach (var reportPath in Directory.EnumerateFiles(resultsDir, "REPORT.md", SearchOption.AllDirectories))
            {
                var runDir = Path.GetDirectoryName(reportPath)!;
                var runId = Path.GetFileName(runDir);
                var testName = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(runDir))!);
                var verdict = ParseVerdictFromReport(reportPath);

                if (verdictFilter != null && !string.Equals(verdict, verdictFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                runs.Add(new
                {
                    runId,
                    workload = workloadName,
                    testName,
                    verdict,
                    timestamp = File.GetLastWriteTimeUtc(reportPath).ToString("o"),
                    reportPath,
                });
            }
        }

        var sorted = runs.Cast<dynamic>()
            .OrderByDescending(r => (string)r.timestamp)
            .Take(limit)
            .ToArray();

        return Task.FromResult(JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ParseVerdictFromReport(string reportPath)
    {
        // First line shape: "# Canary run — <test> — <VERDICT>"
        try
        {
            using var sr = new StreamReader(reportPath);
            var first = sr.ReadLine() ?? string.Empty;
            var lastDash = first.LastIndexOf('—');
            if (lastDash < 0) return "Unknown";
            return first.Substring(lastDash + 1).Trim();
        }
        catch { return "Unknown"; }
    }
}

internal sealed class GetRunReportTool : McpTool
{
    public override string Name => "get_run_report";
    public override string Description => "Fetch the REPORT.md contents (Markdown text) for a specific run by its run id.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "runId": { "type": "string", "description": "The yyyyMMdd-HHmmss-xxxx run id." }
          },
          "required": ["runId"]
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var runId = args["runId"]?.GetValue<string>() ?? throw new ArgumentException("runId is required");
        var root = WorkloadsRoot.Discover();

        // Walk for any results/.../runs/<runId>/REPORT.md
        foreach (var dir in Directory.EnumerateDirectories(root, runId, SearchOption.AllDirectories))
        {
            var report = Path.Combine(dir, "REPORT.md");
            if (File.Exists(report))
            {
                var content = File.ReadAllText(report);
                return Task.FromResult($"# Run {runId}\n\nPath: {report}\n\n---\n\n{content}");
            }
        }

        return Task.FromResult($"No REPORT.md found for run id '{runId}'.");
    }
}
