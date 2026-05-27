using Canary.Orchestration;
using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class TestRunnerViewModelTests
{
    [Fact]
    public void Initial_State_IsIdle_StopDisabled()
    {
        var vm = new TestRunnerViewModel();
        Assert.Equal(TestRunnerState.Idle, vm.State);
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact]
    public void ProgressCard_FindOrCreate_DeduplicatesByKey()
    {
        var vm = new TestRunnerViewModel();
        vm.OnCheckpointStarted("test-a", "cp-1", "before");
        vm.OnCheckpointStarted("test-a", "cp-1", "still the same");
        vm.OnCheckpointStarted("test-a", "cp-2", null);
        Assert.Equal(2, vm.ProgressCards.Count);
    }

    [Fact]
    public void OnVlmVerdict_UpdatesCardStatusColor()
    {
        var vm = new TestRunnerViewModel();
        vm.OnCheckpointStarted("t", "cp", null);
        vm.OnVlmVerdict("t", "cp", passed: true, confidence: 0.9, reasoning: "looks fine");
        var card = vm.ProgressCards.Single();
        Assert.Contains("PASS", card.Status);
        Assert.Equal("#50C850", card.StatusColor);
        Assert.Equal("looks fine", card.VlmReasoning);

        vm.OnVlmVerdict("t", "cp", passed: false, confidence: 0.3, reasoning: "wrong color");
        Assert.Contains("FAIL", card.Status);
        Assert.Equal("#DC3C3C", card.StatusColor);
        Assert.Equal("wrong color", card.VlmReasoning);
    }

    [Fact]
    public void OnScreenshotCaptured_StoresImagePath()
    {
        var vm = new TestRunnerViewModel();
        vm.OnCheckpointStarted("t", "cp", null);
        vm.OnScreenshotCaptured("t", "cp", @"C:\fake\path.png");
        Assert.Equal(@"C:\fake\path.png", vm.ProgressCards.Single().ImagePath);
    }
}
