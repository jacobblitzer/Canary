using Canary.Orchestration;
using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class ResultsViewerViewModelTests
{
    private static TestResult FakeTestResult(string name, params (string cp, TestStatus status)[] checkpoints)
    {
        var result = new TestResult { TestName = name, Workload = "qualia", Status = TestStatus.Passed };
        foreach (var (cp, status) in checkpoints)
        {
            result.CheckpointResults.Add(new CheckpointResult { Name = cp, Status = status });
        }
        return result;
    }

    [Fact]
    public void LoadResult_PopulatesCards_OnePerCheckpoint()
    {
        var vm = new ResultsViewerViewModel();
        var result = FakeTestResult("smoke", ("home", TestStatus.Passed), ("editor", TestStatus.Failed));
        vm.LoadResult(result);

        Assert.Equal(2, vm.Cards.Count);
        Assert.Equal("smoke", vm.Cards[0].TestName);
        Assert.Equal("home", vm.Cards[0].Name);
        Assert.Equal("Passed", vm.Cards[0].Status);
        Assert.Equal("editor", vm.Cards[1].Name);
        Assert.Equal("Failed", vm.Cards[1].Status);
    }

    [Fact]
    public void LoadSuiteResult_FlattensAllCheckpoints()
    {
        var suite = new SuiteResult();
        suite.TestResults.Add(FakeTestResult("a", ("ca1", TestStatus.Passed), ("ca2", TestStatus.Passed)));
        suite.TestResults.Add(FakeTestResult("b", ("cb1", TestStatus.Failed)));

        var vm = new ResultsViewerViewModel();
        vm.LoadSuiteResult(suite, "primary");

        Assert.Equal(3, vm.Cards.Count);
        Assert.Contains("Suite: primary", vm.Header);
    }

    [Fact]
    public void ApproveCheckpoint_WithoutContext_DoesNotThrow()
    {
        var vm = new ResultsViewerViewModel();
        var result = FakeTestResult("smoke", ("home", TestStatus.Failed));
        vm.LoadResult(result);
        var card = vm.Cards[0];
        Assert.False(card.Resolved);
        // No SetContext call → workloadsDir is null → command short-circuits.
        vm.ApproveCheckpointCommand.Execute(card);
        Assert.False(card.Resolved);
    }

    [Fact]
    public void ApproveCheckpoint_WithFixture_CopiesCandidateToBaseline()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-rv-vm-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Mirror BaselineManager's expected layout:
            //   workloads/<w>/results/<test>/{candidates,baselines}/
            var testDir = Path.Combine(root, "qualia", "results", "smoke");
            var candidates = Path.Combine(testDir, "candidates");
            Directory.CreateDirectory(candidates);
            var pngPath = Path.Combine(candidates, "home.png");
            File.WriteAllBytes(pngPath, new byte[] { 0x89, 0x50 });

            var vm = new ResultsViewerViewModel();
            vm.SetContext(root, "qualia", suiteName: null);
            vm.LoadResult(FakeTestResult("smoke", ("home", TestStatus.Failed)));
            var card = vm.Cards[0];
            vm.ApproveCheckpointCommand.Execute(card);

            Assert.True(card.Resolved);
            Assert.True(File.Exists(Path.Combine(testDir, "baselines", "home.png")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectCheckpoint_WithFixture_DeletesCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-rv-vm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var testDir = Path.Combine(root, "qualia", "results", "smoke");
            var candidates = Path.Combine(testDir, "candidates");
            Directory.CreateDirectory(candidates);
            var pngPath = Path.Combine(candidates, "home.png");
            File.WriteAllBytes(pngPath, new byte[] { 0x89, 0x50 });

            var vm = new ResultsViewerViewModel();
            vm.SetContext(root, "qualia", suiteName: null);
            vm.LoadResult(FakeTestResult("smoke", ("home", TestStatus.Failed)));
            var card = vm.Cards[0];
            vm.RejectCheckpointCommand.Execute(card);

            Assert.True(card.Resolved);
            Assert.False(File.Exists(pngPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
