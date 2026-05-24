using Canary.Feedback;
using Xunit;

namespace Canary.Tests.Feedback;

// Phase 5 / §C5 + §C6 file inbox half — verifies the writer produces
// the expected on-disk layout (md + sidecar dir with three artifacts).
public class FeedbackInboxWriterTests : IDisposable
{
    private readonly string _inboxRoot;

    public FeedbackInboxWriterTests()
    {
        _inboxRoot = Path.Combine(Path.GetTempPath(), "canary-fb-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_inboxRoot, recursive: true); } catch { }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Write_ProducesMdAndSidecarTriad()
    {
        var item = new FeedbackItem
        {
            Slug = "2026-05-24-007-something-wrong",
            Date = new DateTime(2026, 5, 24, 14, 23, 0, DateTimeKind.Utc),
            Status = "open",
            Project = "qualia",
            Title = "Something Wrong",
            Body = "Description of what's wrong.",
            RunRef = "workloads/qualia/results/diag/runs/20260524-142300-aaaa/",
            CheckpointRef = "init",
            Urgency = "normal",
        };

        var writer = new FeedbackInboxWriter(_inboxRoot);
        writer.Write(item, sourcePng: new byte[] { 0x89, 0x50 }, annotatedPng: new byte[] { 0x89, 0x50, 0xFF }, annotationsJson: "{}");

        var mdPath = Path.Combine(_inboxRoot, "2026-05-24-007-something-wrong.md");
        var sidecar = Path.Combine(_inboxRoot, "2026-05-24-007-something-wrong");

        Assert.True(File.Exists(mdPath), "markdown body file should exist");
        Assert.True(Directory.Exists(sidecar), "sidecar directory should exist");
        Assert.True(File.Exists(Path.Combine(sidecar, "source.png")));
        Assert.True(File.Exists(Path.Combine(sidecar, "annotated.png")));
        Assert.True(File.Exists(Path.Combine(sidecar, "annotations.json")));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Write_MarkdownHasFrontmatterAndBody()
    {
        var item = new FeedbackItem
        {
            Slug = "2026-05-24-001-test",
            Date = new DateTime(2026, 5, 24, 0, 0, 0, DateTimeKind.Utc),
            Status = "open",
            Project = "canary",
            Title = "Test title",
            Body = "Test body paragraph.",
            Urgency = "high",
        };

        var writer = new FeedbackInboxWriter(_inboxRoot);
        writer.Write(item, Array.Empty<byte>(), Array.Empty<byte>(), "{}");

        var md = File.ReadAllText(Path.Combine(_inboxRoot, "2026-05-24-001-test.md"));
        Assert.Contains("---", md);
        Assert.Contains("date: 2026-05-24", md);
        Assert.Contains("id: 2026-05-24-001-test", md);
        Assert.Contains("status: open", md);
        Assert.Contains("project: canary", md);
        Assert.Contains("urgency: high", md);
        Assert.Contains("# Test title", md);
        Assert.Contains("Test body paragraph.", md);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Write_OptionalRefsOmittedWhenNull()
    {
        var item = new FeedbackItem
        {
            Slug = "2026-05-24-001-test",
            Date = new DateTime(2026, 5, 24),
            Status = "open",
            Title = "T",
            Body = "B",
        };

        var writer = new FeedbackInboxWriter(_inboxRoot);
        writer.Write(item, Array.Empty<byte>(), Array.Empty<byte>(), "{}");

        var md = File.ReadAllText(Path.Combine(_inboxRoot, "2026-05-24-001-test.md"));
        Assert.DoesNotContain("runRef:", md);
        Assert.DoesNotContain("checkpointRef:", md);
        Assert.DoesNotContain("imageRef:", md);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ExistingSlugs_ReturnsAllMdFilenames()
    {
        Directory.CreateDirectory(_inboxRoot);
        File.WriteAllText(Path.Combine(_inboxRoot, "2026-05-24-001-foo.md"), "---");
        File.WriteAllText(Path.Combine(_inboxRoot, "2026-05-24-002-bar.md"), "---");

        var writer = new FeedbackInboxWriter(_inboxRoot);
        var slugs = writer.ExistingSlugs();

        Assert.Equal(2, slugs.Count);
        Assert.Contains("2026-05-24-001-foo", slugs);
        Assert.Contains("2026-05-24-002-bar", slugs);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ExistingSlugs_EmptyDirectory_ReturnsEmpty()
    {
        var writer = new FeedbackInboxWriter(_inboxRoot);
        var slugs = writer.ExistingSlugs();
        Assert.Empty(slugs);
    }
}
