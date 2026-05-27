using System.Text.Json;
using Canary.Config;
using Canary.UI.Avalonia.ViewModels.Editors;
using Xunit;

namespace Canary.Tests.UI.Avalonia.Editors;

[Trait("Category", "Unit")]
public class TestEditorViewModelTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static TestDefinition LoadSeed()
        => new()
        {
            Name = "smoke",
            Workload = "qualia",
            Description = "Boots Qualia + reloads under the eager-L3 provider.",
            RunMode = "fresh",
            KeepOpenOnFailure = false,
            Recording = string.Empty,
            Setup = new TestSetup
            {
                File = "fixtures/empty.qualia.json",
                Viewport = new ViewportSetup { Width = 1280, Height = 800, Projection = "perspective", DisplayMode = "shaded" },
            },
            Checkpoints =
            {
                new TestCheckpoint { Name = "home", AtTimeMs = 0, Tolerance = 0.5, Description = "Default landing layout.", Source = "viewport", Mode = "pixel-diff" },
                new TestCheckpoint { Name = "editor", AtTimeMs = 1500, Tolerance = 1.0, Description = "Markdown editor open.", Source = "viewport", Mode = "vlm" },
            },
            Asserts =
            {
                new TestAssert { Type = "PanelEquals", Nickname = "Status", Text = "Ready", Description = "Initial status text" },
            },
        };

    [Fact]
    public void Load_PopulatesAllSurfacedFields()
    {
        var vm = new TestEditorViewModel();
        vm.Load(LoadSeed());

        Assert.Equal("smoke", vm.Name);
        Assert.Equal("qualia", vm.Workload);
        Assert.Equal("fresh", vm.RunMode);
        Assert.Equal(2, vm.Checkpoints.Count);
        Assert.Single(vm.Asserts);
        Assert.Equal(1280, vm.ViewportWidth);
        Assert.Equal(800, vm.ViewportHeight);
        Assert.Equal("home", vm.Checkpoints[0].Name);
        Assert.Equal("vlm", vm.Checkpoints[1].Mode);
    }

    [Fact]
    public void RoundTrip_EditAndSave_IsIdempotentForUnchangedFields()
    {
        var seed = LoadSeed();
        var beforeJson = JsonSerializer.Serialize(seed, Options);

        var vm = new TestEditorViewModel();
        vm.Load(seed);
        var rebuilt = vm.BuildDefinition();
        var afterJson = JsonSerializer.Serialize(rebuilt, Options);

        Assert.Equal(beforeJson, afterJson);
    }

    [Fact]
    public void Save_FiresSaveRequestedWithFormattedJson()
    {
        var vm = new TestEditorViewModel();
        vm.Load(LoadSeed());

        string? captured = null;
        vm.SaveRequested += json => captured = json;
        vm.SaveCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.Contains("\"name\": \"smoke\"", captured);
        Assert.Contains("\"workload\": \"qualia\"", captured);
    }

    [Fact]
    public void Save_BlocksOnEmptyName()
    {
        var vm = new TestEditorViewModel();
        vm.Load(LoadSeed());
        vm.Name = string.Empty;
        string? captured = null;
        vm.SaveRequested += json => captured = json;
        vm.SaveCommand.Execute(null);
        Assert.Null(captured);
        Assert.NotNull(vm.ValidationError);
    }

    [Fact]
    public void AddRemoveCheckpoint_MutatesCollection()
    {
        var vm = new TestEditorViewModel();
        vm.Load(LoadSeed());
        var before = vm.Checkpoints.Count;
        vm.AddCheckpointCommand.Execute(null);
        Assert.Equal(before + 1, vm.Checkpoints.Count);
        vm.RemoveCheckpointCommand.Execute(vm.Checkpoints[before]);
        Assert.Equal(before, vm.Checkpoints.Count);
    }

    [Fact]
    public void UnmanagedFields_RoundTripUntouched()
    {
        // Penumbra-specific fields aren't on the editor surface. Verify they
        // survive a Load → BuildDefinition cycle.
        var seed = LoadSeed();
        seed.Setup!.DisplayPreset = "studio-default";
        seed.Setup.Scene = new SceneSetup { SceneName = "atrium", Index = 3 };
        seed.Setup.Canvas = new CanvasSetup { Width = 1920, Height = 1080 };
        seed.Setup.Commands.Add("ResetView");

        var vm = new TestEditorViewModel();
        vm.Load(seed);
        var rebuilt = vm.BuildDefinition();

        Assert.Equal("studio-default", rebuilt.Setup?.DisplayPreset);
        Assert.Equal("atrium", rebuilt.Setup?.Scene?.SceneName);
        Assert.Equal(3, rebuilt.Setup?.Scene?.Index);
        Assert.Equal(1920, rebuilt.Setup?.Canvas?.Width);
        Assert.Contains("ResetView", rebuilt.Setup!.Commands);
    }
}
