using System.Text;
using System.Text.Json;
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
            BuildMarkdown(session));
    }

    public static string BuildMarkdown(SessionData s)
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
        return sb.ToString();
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
