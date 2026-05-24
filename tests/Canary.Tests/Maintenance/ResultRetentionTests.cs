using Canary.Maintenance;
using Xunit;

namespace Canary.Tests.Maintenance;

// Phase 3 / §C2 — basic correctness for the per-run dir retention helper.
public class ResultRetentionTests : IDisposable
{
    private readonly string _root;

    public ResultRetentionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "canary-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeRunDir(string workload, string test, string stamp, DateTime? lastWriteUtc = null)
    {
        var dir = Path.Combine(_root, workload, "results", test, "runs", stamp);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "result.json"), "{}");
        if (lastWriteUtc.HasValue) Directory.SetLastWriteTimeUtc(dir, lastWriteUtc.Value);
        return dir;
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void PurgeOlderThan_DeletesOldRunDirsOnly()
    {
        var oldDir = MakeRunDir("wl", "alpha", "20260101-000000-aaaa", DateTime.UtcNow - TimeSpan.FromDays(30));
        var newDir = MakeRunDir("wl", "alpha", "20260524-142300-bbbb", DateTime.UtcNow - TimeSpan.FromHours(1));

        var report = ResultRetention.PurgeOlderThan(_root, TimeSpan.FromDays(14));

        Assert.False(Directory.Exists(oldDir), "old run dir should have been purged");
        Assert.True(Directory.Exists(newDir), "fresh run dir should be kept");
        Assert.Equal(1, report.DirsPurged);
        Assert.Equal(2, report.DirsScanned);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void PurgeOlderThan_LeavesLegacyFlatLayoutAlone()
    {
        // Legacy: results/<test>/result.json (no runs/)
        var legacyTestDir = Path.Combine(_root, "wl", "results", "beta");
        Directory.CreateDirectory(legacyTestDir);
        File.WriteAllText(Path.Combine(legacyTestDir, "result.json"), "{}");
        Directory.SetLastWriteTimeUtc(legacyTestDir, DateTime.UtcNow - TimeSpan.FromDays(365));

        var report = ResultRetention.PurgeOlderThan(_root, TimeSpan.FromDays(14));

        Assert.True(Directory.Exists(legacyTestDir));
        Assert.Equal(0, report.DirsPurged);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void PurgeOlderThan_NonexistentRoot_NoOp()
    {
        var fake = Path.Combine(_root, "does-not-exist");
        var report = ResultRetention.PurgeOlderThan(fake, TimeSpan.FromDays(14));
        Assert.Equal(0, report.DirsScanned);
        Assert.Equal(0, report.DirsPurged);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DefaultRetention_Is14Days()
    {
        Assert.Equal(TimeSpan.FromDays(14), ResultRetention.DefaultRetention);
    }
}
