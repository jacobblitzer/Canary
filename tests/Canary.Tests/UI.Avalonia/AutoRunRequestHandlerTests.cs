using System.Text.Json;
using Canary.Cli;
using Canary.Orchestration;
using Canary.UI.Avalonia.Services;
using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class AutoRunRequestHandlerTests
{
    private static string BuildWorkloadFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-autorun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var w = Path.Combine(root, "qualia");
        Directory.CreateDirectory(Path.Combine(w, "tests"));
        Directory.CreateDirectory(Path.Combine(w, "suites"));
        File.WriteAllText(Path.Combine(w, "workload.json"),
            JsonSerializer.Serialize(new { name = "qualia", displayName = "Qualia", agentType = "qualia-cdp" }));
        File.WriteAllText(Path.Combine(w, "tests", "smoke.json"),
            JsonSerializer.Serialize(new { name = "smoke", workload = "qualia", checkpoints = Array.Empty<object>() }));
        File.WriteAllText(Path.Combine(w, "suites", "primary.json"),
            JsonSerializer.Serialize(new { name = "primary", tests = new[] { "smoke" } }));
        return root;
    }

    [Fact]
    public async Task FindNode_ByWorkloadOnly_ReturnsWorkloadRoot()
    {
        var dir = BuildWorkloadFixture();
        try
        {
            var tree = new WorkloadTreeViewModel();
            await tree.LoadAsync(dir);
            var args = new AutoRunArgs { Workload = "qualia" };
            var node = AutoRunRequestHandler.FindNode(tree, args);
            Assert.NotNull(node);
            Assert.Equal(WorkloadNodeKind.Workload, node!.Kind);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task FindNode_ByWorkloadAndTest_ReturnsTestLeaf()
    {
        var dir = BuildWorkloadFixture();
        try
        {
            var tree = new WorkloadTreeViewModel();
            await tree.LoadAsync(dir);
            var args = new AutoRunArgs { Workload = "qualia", Test = "smoke" };
            var node = AutoRunRequestHandler.FindNode(tree, args);
            Assert.NotNull(node);
            Assert.Equal(WorkloadNodeKind.Test, node!.Kind);
            Assert.Equal("smoke", node.Label);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task FindNode_ByWorkloadAndSuite_ReturnsSuiteLeaf()
    {
        var dir = BuildWorkloadFixture();
        try
        {
            var tree = new WorkloadTreeViewModel();
            await tree.LoadAsync(dir);
            var args = new AutoRunArgs { Workload = "qualia", Suite = "primary" };
            var node = AutoRunRequestHandler.FindNode(tree, args);
            Assert.NotNull(node);
            Assert.Equal(WorkloadNodeKind.Suite, node!.Kind);
            Assert.Contains("primary", node.Label);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task FindNode_UnknownTest_ReturnsNull()
    {
        var dir = BuildWorkloadFixture();
        try
        {
            var tree = new WorkloadTreeViewModel();
            await tree.LoadAsync(dir);
            var args = new AutoRunArgs { Workload = "qualia", Test = "does-not-exist" };
            Assert.Null(AutoRunRequestHandler.FindNode(tree, args));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task FindNode_UnknownWorkload_ReturnsNull()
    {
        var dir = BuildWorkloadFixture();
        try
        {
            var tree = new WorkloadTreeViewModel();
            await tree.LoadAsync(dir);
            var args = new AutoRunArgs { Workload = "nope" };
            Assert.Null(AutoRunRequestHandler.FindNode(tree, args));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseMode_MapsExpectedStrings()
    {
        Assert.Equal(ModeOverride.PixelDiff, AutoRunRequestHandler.ParseMode("pixel-diff"));
        Assert.Equal(ModeOverride.Vlm, AutoRunRequestHandler.ParseMode("vlm"));
        Assert.Equal(ModeOverride.Both, AutoRunRequestHandler.ParseMode("both"));
        Assert.Equal(ModeOverride.None, AutoRunRequestHandler.ParseMode(null));
        Assert.Equal(ModeOverride.None, AutoRunRequestHandler.ParseMode("garbage"));
        Assert.Equal(ModeOverride.PixelDiff, AutoRunRequestHandler.ParseMode("PIXEL-DIFF"));
    }

    [Fact]
    public async Task TestsViewModel_CreateTestFromRecording_WritesJson()
    {
        var dir = BuildWorkloadFixture();
        try
        {
            // Add a fake recording file under the workload.
            var recordingsDir = Path.Combine(dir, "qualia", "recordings");
            Directory.CreateDirectory(recordingsDir);
            var recordingPath = Path.Combine(recordingsDir, "demo.input.json");
            File.WriteAllText(recordingPath, "{\"events\": [], \"metadata\": { \"durationMs\": 0 }}");

            var vm = new TestsViewModel();
            await vm.LoadWorkloadsAsync(dir);
            var entry = vm.Tree.LoadedWorkloads.First(w => w.Config.Name == "qualia");

            var savedPath = await vm.CreateTestFromRecordingFileAsync("from-recording", recordingPath, entry);
            Assert.NotNull(savedPath);
            Assert.True(File.Exists(savedPath));
            var testJson = File.ReadAllText(savedPath!);
            Assert.Contains("\"name\": \"from-recording\"", testJson);
            Assert.Contains("\"recording\": \"demo.input.json\"", testJson);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
