using System.Text.Json;
using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class WorkloadTreeViewModelTests
{
    private static string CreateWorkloadFixture(out string workloadsDir)
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-tree-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        workloadsDir = root;

        // Workload A: 1 test + 1 suite + 1 recording.
        var a = Path.Combine(root, "alpha");
        Directory.CreateDirectory(Path.Combine(a, "tests"));
        Directory.CreateDirectory(Path.Combine(a, "suites"));
        Directory.CreateDirectory(Path.Combine(a, "recordings"));
        File.WriteAllText(Path.Combine(a, "workload.json"),
            JsonSerializer.Serialize(new { name = "alpha", displayName = "Alpha", agentType = "qualia-cdp", appPath = "" }));
        File.WriteAllText(Path.Combine(a, "tests", "smoke.json"),
            JsonSerializer.Serialize(new { name = "smoke", workload = "alpha", checkpoints = Array.Empty<object>() }));
        File.WriteAllText(Path.Combine(a, "suites", "primary.json"),
            JsonSerializer.Serialize(new { name = "primary", tests = new[] { "smoke" } }));
        File.WriteAllText(Path.Combine(a, "recordings", "rec1.input.json"),
            "{ \"events\": [], \"metadata\": { \"durationMs\": 0 } }");

        // Workload B: no tests/suites/recordings.
        var b = Path.Combine(root, "bravo");
        Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(b, "workload.json"),
            JsonSerializer.Serialize(new { name = "bravo", displayName = "Bravo", agentType = "rhino", appPath = "" }));

        return root;
    }

    [Fact]
    public async Task Load_BuildsTreeWithExpectedNodeShape()
    {
        var dir = CreateWorkloadFixture(out _);
        try
        {
            var vm = new WorkloadTreeViewModel();
            await vm.LoadAsync(dir);

            Assert.Equal(2, vm.Roots.Count);

            var alpha = vm.Roots.First(r => r.Label == "Alpha");
            Assert.Equal(WorkloadNodeKind.Workload, alpha.Kind);
            Assert.True(alpha.IsExpanded);
            // 3 group nodes: Suites, All Tests, Recordings.
            Assert.Equal(3, alpha.Children.Count);
            Assert.Contains(alpha.Children, c => c.Kind == WorkloadNodeKind.SuitesGroup);
            Assert.Contains(alpha.Children, c => c.Kind == WorkloadNodeKind.TestsGroup);
            Assert.Contains(alpha.Children, c => c.Kind == WorkloadNodeKind.RecordingsGroup);

            var testsGroup = alpha.Children.First(c => c.Kind == WorkloadNodeKind.TestsGroup);
            Assert.Single(testsGroup.Children);
            Assert.Equal(WorkloadNodeKind.Test, testsGroup.Children[0].Kind);
            Assert.Equal("smoke", testsGroup.Children[0].Label);

            var bravo = vm.Roots.First(r => r.Label == "Bravo");
            Assert.Empty(bravo.Children);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Load_StatusText_SummarizesCounts()
    {
        var dir = CreateWorkloadFixture(out _);
        try
        {
            var vm = new WorkloadTreeViewModel();
            await vm.LoadAsync(dir);
            Assert.Contains("2 workloads", vm.StatusText);
            Assert.Contains("1 suites", vm.StatusText);
            Assert.Contains("1 tests", vm.StatusText);
            Assert.Contains("1 recordings", vm.StatusText);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Load_MissingDir_LeavesEmptyRoots()
    {
        var vm = new WorkloadTreeViewModel();
        await vm.LoadAsync("/this/does/not/exist");
        Assert.Empty(vm.Roots);
    }
}
