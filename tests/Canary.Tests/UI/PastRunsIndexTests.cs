using Canary.UI.Panels;
using Xunit;

namespace Canary.Tests.UI;

// Phase 7 / §C8 — tests for the REPORT.md first-line verdict parser
// used by PastRunsPanel's run list. Behavior contract: parses the
// trailing verdict from "# Canary run — <test> — <VERDICT>".
public class PastRunsIndexTests : IDisposable
{
    private readonly string _tempDir;
    public PastRunsIndexTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "canary-pastruns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }
    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseVerdict_StandardHeader_ReturnsVerdict()
    {
        var path = Path.Combine(_tempDir, "REPORT.md");
        File.WriteAllText(path, "# Canary run — diag-pencil-baseline — FAIL\n\nRun ID: abc\n");
        Assert.Equal("FAIL", PastRunsPanel.ParseVerdict(path));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseVerdict_PassedHeader_ReturnsPass()
    {
        var path = Path.Combine(_tempDir, "REPORT.md");
        File.WriteAllText(path, "# Canary run — main-pencil — PASS\n\nbody\n");
        Assert.Equal("PASS", PastRunsPanel.ParseVerdict(path));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseVerdict_MissingEmDash_ReturnsQuestionMark()
    {
        var path = Path.Combine(_tempDir, "REPORT.md");
        File.WriteAllText(path, "# Not a canary report header\n\nbody\n");
        Assert.Equal("?", PastRunsPanel.ParseVerdict(path));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ParseVerdict_NonexistentFile_ReturnsQuestionMark()
    {
        var path = Path.Combine(_tempDir, "missing.md");
        Assert.Equal("?", PastRunsPanel.ParseVerdict(path));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void FeedbackPanel_DiscoverFeedbackRoot_WalksUpForDocsFeedback()
    {
        // Sanity-only: on this dev machine, docs/feedback/ exists; on a
        // bare deploy it doesn't. Either way the API returns a string —
        // it never throws.
        var root = FeedbackPanel.DiscoverFeedbackRoot();
        Assert.False(string.IsNullOrEmpty(root));
        Assert.EndsWith("feedback", root, StringComparison.OrdinalIgnoreCase);
    }
}
