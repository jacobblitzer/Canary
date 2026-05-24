namespace Canary.Feedback;

// Per design §C5 — operator-authored feedback item created by the sketch
// + annotate flow. Frontmatter shape lives on disk as YAML; this POCO
// carries the parsed fields. Phase 5 ships the producer side (writer);
// Phase 6 ships the MCP server that reads them.
public sealed class FeedbackItem
{
    // YYYY-MM-DD-NNN-<3-to-5-word-slug>. Stable across the lifecycle —
    // moving from inbox/ to triaged/ to resolved/ preserves the slug.
    public required string Slug { get; init; }

    public required DateTime Date { get; init; }

    // "open" | "triaged" | "resolved". On disk, mirrored by directory
    // location AND frontmatter field (cheap dual signal).
    public string Status { get; init; } = "open";

    // "canary" | "qualia" | "penumbra" | "rhino" | "cross-repo".
    public string? Project { get; init; }

    // Optional pointer to the per-run dir from §C2: relative path from
    // repo root, e.g. "workloads/qualia/results/diag-pencil-baseline/runs/20260524-142300-a3f1/".
    public string? RunRef { get; init; }

    public string? CheckpointRef { get; init; }
    public string? ImageRef { get; init; }
    public string Urgency { get; init; } = "normal";   // low | normal | high
    public List<string> Tags { get; init; } = new() { "feedback", "sketch" };

    // Free-form body (markdown).
    public required string Title { get; init; }
    public required string Body { get; init; }
}
