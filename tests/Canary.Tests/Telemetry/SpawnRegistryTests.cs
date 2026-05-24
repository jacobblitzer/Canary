using Canary.Telemetry;
using Xunit;

namespace Canary.Tests.Telemetry;

// Phase 6 / §C7 Tier 2 — SpawnRegistry round-trip + cross-session merge.
public class SpawnRegistryTests : IDisposable
{
    private readonly string _tempDir;

    public SpawnRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "canary-spawnreg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Register_PersistsToDisk_ReloadedSnapshotMatches()
    {
        var path = Path.Combine(_tempDir, "session-1.json");
        var registry = new SpawnRegistry(path, "session-1");

        registry.Register(pid: 1234, name: "node.exe", commandLine: "cmd /c npm run dev",
            workingDirectory: @"C:\Repos\Qualia", port: 5173, intent: "Qualia Vite");

        var snap = registry.Snapshot();
        Assert.Single(snap);
        Assert.Equal(1234, snap[0].Pid);
        Assert.Equal("node.exe", snap[0].Name);
        Assert.Equal(5173, snap[0].Port);
        Assert.Equal("Qualia Vite", snap[0].Intent);

        Assert.True(File.Exists(path), "Register should flush to disk");
        Assert.Contains("Qualia Vite", File.ReadAllText(path));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Register_SamePidTwice_ReplacesRecord()
    {
        var registry = new SpawnRegistry(Path.Combine(_tempDir, "s.json"), "s");
        registry.Register(1234, "node.exe", "cmd 1", "/dir1", 5173, "First");
        registry.Register(1234, "node.exe", "cmd 2", "/dir2", 5173, "Second");

        var snap = registry.Snapshot();
        Assert.Single(snap);
        Assert.Equal("Second", snap[0].Intent);
        Assert.Equal("/dir2", snap[0].WorkingDirectory);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Unregister_RemovesRecord_PersistsToDisk()
    {
        var path = Path.Combine(_tempDir, "u.json");
        var registry = new SpawnRegistry(path, "u");
        registry.Register(1234, "node.exe", "cmd", "/", 5173, "Vite");
        registry.Register(5678, "chrome.exe", "cmd", "/", 9222, "Chrome");

        registry.Unregister(1234);

        var snap = registry.Snapshot();
        Assert.Single(snap);
        Assert.Equal(5678, snap[0].Pid);
        Assert.DoesNotContain("Vite", File.ReadAllText(path));
        Assert.Contains("Chrome", File.ReadAllText(path));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void Snapshot_IsImmutable_ChangesDoNotAffectInternalList()
    {
        var registry = new SpawnRegistry(Path.Combine(_tempDir, "s.json"), "s");
        registry.Register(1, "a", "c", "/", null, "i");

        var snap1 = registry.Snapshot();
        registry.Register(2, "b", "c", "/", null, "j");
        var snap2 = registry.Snapshot();

        Assert.Single(snap1);
        Assert.Equal(2, snap2.Count);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void JsonSerialization_RoundTripsViaSessionDocument()
    {
        var path = Path.Combine(_tempDir, "rt.json");
        var registry = new SpawnRegistry(path, "session-rt");
        registry.Register(7, "node.exe", "npm run dev", @"C:\Repos\Qualia", 5173, "Qualia Vite dev server");

        var json = File.ReadAllText(path);
        var doc = System.Text.Json.JsonSerializer.Deserialize<SpawnRegistry.SessionDocument>(json, SpawnRegistry.JsonOptions);

        Assert.NotNull(doc);
        Assert.Equal("session-rt", doc!.SessionId);
        Assert.Single(doc.Spawns);
        Assert.Equal(7, doc.Spawns[0].Pid);
        Assert.Equal(5173, doc.Spawns[0].Port);
    }
}
