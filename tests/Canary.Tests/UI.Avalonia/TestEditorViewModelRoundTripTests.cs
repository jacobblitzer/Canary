using Canary.Config;
using Canary.UI.Avalonia.ViewModels.Editors;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class TestEditorViewModelRoundTripTests
{
    /// <summary>
    /// Phase 14.2 — VLM + setup-commands + capture/scrub round-trip through the
    /// editor without data loss. Mirrors the kin_15_watt_straight_line fixture
    /// minus the bulk so the test stays compact.
    /// </summary>
    [Fact]
    public void Load_then_BuildDefinition_preserves_vlm_commands_capture_and_scrub()
    {
        var original = new TestDefinition
        {
            Name = "cpig-kin-15-watt",
            Workload = "rhino",
            Setup = new TestSetup
            {
                File = "fixtures/cpig_slop_loader.gh",
                VlmDescription = "A 4-bar with the coupler midpoint tracing a near-straight vertical line.",
                Vlm = new VlmConfig { Provider = "ollama", Model = "gemma4:e2b", MaxTokens = 1024 },
                Commands = new() { "_-Grid _Show _Enter", "_-NamedView _Restore Top _Enter" },
            },
            Checkpoints = new()
            {
                new TestCheckpoint
                {
                    Name = "post-build",
                    AtTimeMs = 5000,
                    Tolerance = 0.02,
                    Capture = new TestCheckpointCapture
                    {
                        Gif = true,
                        FrameCount = 30,
                        IntervalMs = 150,
                        Scrub = new TestCheckpointScrub
                        {
                            Nickname = "AnimSlider",
                            Values = new double[] { 0, 200, 400, 600, 800, 1000, 1200, 1400, 1600, 1800, 2000 },
                            SettleMs = 50,
                            SolveTimeoutMs = 10000,
                        },
                    },
                },
            },
        };

        var vm = new TestEditorViewModel();
        vm.Load(original);
        var roundTripped = vm.BuildDefinition();

        Assert.Equal("A 4-bar with the coupler midpoint tracing a near-straight vertical line.", roundTripped.Setup!.VlmDescription);
        Assert.Equal("ollama", roundTripped.Setup.Vlm!.Provider);
        Assert.Equal("gemma4:e2b", roundTripped.Setup.Vlm.Model);
        // MaxTokens isn't editor-surfaced but round-trips through the backing POCO.
        Assert.Equal(1024, roundTripped.Setup.Vlm.MaxTokens);
        Assert.Equal(2, roundTripped.Setup.Commands.Count);
        Assert.Equal("_-Grid _Show _Enter", roundTripped.Setup.Commands[0]);

        var cp = roundTripped.Checkpoints[0];
        Assert.NotNull(cp.Capture);
        Assert.True(cp.Capture!.Gif);
        Assert.Equal(30, cp.Capture.FrameCount);
        Assert.Equal(150, cp.Capture.IntervalMs);
        Assert.NotNull(cp.Capture.Scrub);
        Assert.Equal("AnimSlider", cp.Capture.Scrub!.Nickname);
        Assert.Equal(11, cp.Capture.Scrub.Values.Length);
        Assert.Equal(2000.0, cp.Capture.Scrub.Values[^1]);
        Assert.Equal(50, cp.Capture.Scrub.SettleMs);
        Assert.Equal(10000, cp.Capture.Scrub.SolveTimeoutMs);
    }

    [Fact]
    public void BuildDefinition_omits_capture_object_when_all_capture_fields_blank()
    {
        var original = new TestDefinition
        {
            Name = "cpig-kin-01",
            Workload = "rhino",
            Checkpoints = new()
            {
                new TestCheckpoint { Name = "post-build", AtTimeMs = 5000 },
            },
        };

        var vm = new TestEditorViewModel();
        vm.Load(original);
        var roundTripped = vm.BuildDefinition();

        Assert.Null(roundTripped.Checkpoints[0].Capture);
    }

    [Fact]
    public void BuildDefinition_emits_capture_when_only_gif_enabled_without_scrub()
    {
        var vm = new TestEditorViewModel();
        vm.Load(new TestDefinition
        {
            Name = "t", Workload = "rhino",
            Checkpoints = new() { new TestCheckpoint { Name = "post-build" } },
        });
        vm.Checkpoints[0].CaptureGif = true;
        vm.Checkpoints[0].CaptureFrameCount = 12;
        vm.Checkpoints[0].CaptureIntervalMs = 200;

        var built = vm.BuildDefinition();
        var cap = built.Checkpoints[0].Capture;
        Assert.NotNull(cap);
        Assert.True(cap!.Gif);
        Assert.Equal(12, cap.FrameCount);
        Assert.Equal(200, cap.IntervalMs);
        Assert.Null(cap.Scrub);
    }
}
