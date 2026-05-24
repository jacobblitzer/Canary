using System.Text;

namespace Canary.Feedback;

// Writes a feedback item to disk per design §C5 + §C6 file-inbox half.
// One markdown file alongside a sidecar dir containing the source PNG,
// the annotated PNG, and the annotations.json vector data. All
// writes are atomic-per-file (write to temp + move).
public sealed class FeedbackInboxWriter
{
    public string InboxRoot { get; }

    public FeedbackInboxWriter(string inboxRoot)
    {
        InboxRoot = inboxRoot;
    }

    // Discovers an unused slug + writes the item + sidecar dir.
    // Returns the resolved slug (used by the UI to confirm the path).
    public string Write(
        FeedbackItem item,
        byte[] sourcePng,
        byte[] annotatedPng,
        string annotationsJson)
    {
        Directory.CreateDirectory(InboxRoot);

        var mdPath = Path.Combine(InboxRoot, item.Slug + ".md");
        var sidecarDir = Path.Combine(InboxRoot, item.Slug);
        Directory.CreateDirectory(sidecarDir);

        // Markdown body — frontmatter + Title heading + Body text.
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"date: {item.Date:yyyy-MM-dd}");
        sb.AppendLine($"id: {item.Slug}");
        sb.AppendLine($"status: {item.Status}");
        if (!string.IsNullOrEmpty(item.Project)) sb.AppendLine($"project: {item.Project}");
        if (!string.IsNullOrEmpty(item.RunRef)) sb.AppendLine($"runRef: \"{item.RunRef}\"");
        if (!string.IsNullOrEmpty(item.CheckpointRef)) sb.AppendLine($"checkpointRef: \"{item.CheckpointRef}\"");
        if (!string.IsNullOrEmpty(item.ImageRef)) sb.AppendLine($"imageRef: \"{item.ImageRef}\"");
        sb.AppendLine($"urgency: {item.Urgency}");
        sb.AppendLine($"tags: [{string.Join(", ", item.Tags)}]");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {item.Title}");
        sb.AppendLine();
        sb.AppendLine(item.Body);
        sb.AppendLine();
        AtomicWriteAllText(mdPath, sb.ToString());

        AtomicWriteAllBytes(Path.Combine(sidecarDir, "source.png"), sourcePng);
        AtomicWriteAllBytes(Path.Combine(sidecarDir, "annotated.png"), annotatedPng);
        AtomicWriteAllText(Path.Combine(sidecarDir, "annotations.json"), annotationsJson);

        return item.Slug;
    }

    public IReadOnlyList<string> ExistingSlugs()
    {
        if (!Directory.Exists(InboxRoot)) return Array.Empty<string>();
        return Directory.EnumerateFiles(InboxRoot, "*.md")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .ToList();
    }

    private static void AtomicWriteAllText(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Encoding.UTF8);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    private static void AtomicWriteAllBytes(string path, byte[] content)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, content);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}
