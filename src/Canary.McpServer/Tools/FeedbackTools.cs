using System.Text.Json;
using System.Text.Json.Nodes;
using Canary.Feedback;

namespace Canary.McpServer.Tools;

// MCP tools for the docs/feedback/ inbox per design §C6. Reads the
// markdown front-matter + body to return summaries (list_feedback)
// or full content (get_feedback). mark_feedback_triaged moves the
// item + its sidecar from inbox/ to triaged/.
//
// Inbox root: discovered by walking up from AppContext.BaseDirectory
// looking for docs/feedback/ — same heuristic as ImageViewerForm's
// FindInboxRoot.
internal static class FeedbackRoot
{
    public static string Discover()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "feedback");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "docs", "feedback");
    }

    public static string Inbox => Path.Combine(Discover(), "inbox");
    public static string Triaged => Path.Combine(Discover(), "triaged");
    public static string Resolved => Path.Combine(Discover(), "resolved");

    public static string DirFor(string status) => status switch
    {
        "open" => Inbox,
        "triaged" => Triaged,
        "resolved" => Resolved,
        _ => Inbox,
    };
}

internal sealed class ListFeedbackTool : McpTool
{
    public override string Name => "list_feedback";
    public override string Description => "List items in the Canary feedback inbox (operator-authored sketch+annotate items). Filter by status (open / triaged / resolved).";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "status": { "type": "string", "enum": ["open", "triaged", "resolved"], "description": "Filter by lifecycle status; default 'open'." },
            "limit":  { "type": "integer", "description": "Max items to return; default 50." }
          },
          "required": []
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var status = args["status"]?.GetValue<string>() ?? "open";
        var limit = args["limit"]?.GetValue<int>() ?? 50;
        var dir = FeedbackRoot.DirFor(status);

        if (!Directory.Exists(dir))
            return Task.FromResult($"No feedback dir at: {dir}");

        var items = Directory.EnumerateFiles(dir, "*.md")
            .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
            .Take(limit)
            .Select(p =>
            {
                var slug = Path.GetFileNameWithoutExtension(p);
                var (title, project, urgency) = ParseFrontmatter(File.ReadAllText(p));
                return new { id = slug, status, project, urgency, title, path = p };
            })
            .ToArray();

        return Task.FromResult(JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static (string Title, string Project, string Urgency) ParseFrontmatter(string md)
    {
        var lines = md.Split('\n');
        string title = "(no title)", project = "", urgency = "normal";
        bool inFm = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line == "---") { if (inFm) break; inFm = true; continue; }
            if (inFm)
            {
                if (line.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
                    project = line.Substring("project:".Length).Trim();
                else if (line.StartsWith("urgency:", StringComparison.OrdinalIgnoreCase))
                    urgency = line.Substring("urgency:".Length).Trim();
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                title = line.Substring(2).Trim();
                break;
            }
        }
        return (title, project, urgency);
    }
}

internal sealed class GetFeedbackTool : McpTool
{
    public override string Name => "get_feedback";
    public override string Description => "Fetch the full body + frontmatter + sidecar paths for one feedback item by id.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "Slug id of the feedback item (e.g. 2026-05-24-007-pencil-toon-too-bright)." }
          },
          "required": ["id"]
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var id = args["id"]?.GetValue<string>() ?? throw new ArgumentException("id is required");

        foreach (var statusDir in new[] { FeedbackRoot.Inbox, FeedbackRoot.Triaged, FeedbackRoot.Resolved })
        {
            var md = Path.Combine(statusDir, id + ".md");
            if (!File.Exists(md)) continue;

            var sidecar = Path.Combine(statusDir, id);
            var body = File.ReadAllText(md);
            var result = new
            {
                id,
                statusDir,
                body,
                sourcePng = SafePath(sidecar, "source.png"),
                annotatedPng = SafePath(sidecar, "annotated.png"),
                annotationsJson = SafePath(sidecar, "annotations.json"),
            };
            return Task.FromResult(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }

        return Task.FromResult($"No feedback item with id '{id}' found in inbox / triaged / resolved.");
    }

    private static string? SafePath(string dir, string filename)
    {
        var p = Path.Combine(dir, filename);
        return File.Exists(p) ? p : null;
    }
}

internal sealed class MarkFeedbackTriagedTool : McpTool
{
    public override string Name => "mark_feedback_triaged";
    public override string Description => "Move a feedback item from inbox/ to triaged/. Updates frontmatter status from 'open' to 'triaged'. Sidecar dir moves alongside.";
    public override string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "Slug id of the feedback item." }
          },
          "required": ["id"]
        }
        """;

    public override Task<string> InvokeAsync(JsonObject args)
    {
        var id = args["id"]?.GetValue<string>() ?? throw new ArgumentException("id is required");
        var srcMd = Path.Combine(FeedbackRoot.Inbox, id + ".md");
        var srcSide = Path.Combine(FeedbackRoot.Inbox, id);

        if (!File.Exists(srcMd))
            return Task.FromResult($"Item '{id}' not found in inbox.");

        Directory.CreateDirectory(FeedbackRoot.Triaged);
        var dstMd = Path.Combine(FeedbackRoot.Triaged, id + ".md");
        var dstSide = Path.Combine(FeedbackRoot.Triaged, id);

        // Update status frontmatter line before move.
        var body = File.ReadAllText(srcMd).Replace("status: open", "status: triaged");
        File.WriteAllText(srcMd, body);

        if (File.Exists(dstMd)) File.Delete(dstMd);
        File.Move(srcMd, dstMd);
        if (Directory.Exists(srcSide))
        {
            if (Directory.Exists(dstSide)) Directory.Delete(dstSide, recursive: true);
            Directory.Move(srcSide, dstSide);
        }

        return Task.FromResult($"Moved '{id}' from inbox/ to triaged/.");
    }
}
