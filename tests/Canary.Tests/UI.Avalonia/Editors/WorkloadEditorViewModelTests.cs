using System.Text.Json;
using Canary.Config;
using Canary.UI.Avalonia.ViewModels.Editors;
using Xunit;

namespace Canary.Tests.UI.Avalonia.Editors;

[Trait("Category", "Unit")]
public class WorkloadEditorViewModelTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static WorkloadConfig Seed()
        => new()
        {
            Name = "qualia",
            DisplayName = "Qualia",
            AppPath = "C:\\Repos\\Qualia\\dist\\app.exe",
            AppArgs = "--dev",
            AgentType = "qualia-cdp",
            PipeName = "canary-qualia",
            StartupTimeoutMs = 30000,
            WindowTitle = "Qualia",
            ViewportClass = "Chrome_WidgetWin_1",
            SetupCommands = { "ResetView", "ZoomToFit" },
        };

    [Fact]
    public void Load_PopulatesAllFields()
    {
        var vm = new WorkloadEditorViewModel();
        vm.Load(Seed());
        Assert.Equal("qualia", vm.Name);
        Assert.Equal("Qualia", vm.DisplayName);
        Assert.Equal("qualia-cdp", vm.AgentType);
        Assert.Equal(30000, vm.StartupTimeoutMs);
        Assert.Equal(2, vm.SetupCommands.Count);
    }

    [Fact]
    public void RoundTrip_IsIdempotent()
    {
        var seed = Seed();
        var before = JsonSerializer.Serialize(seed, Options);
        var vm = new WorkloadEditorViewModel();
        vm.Load(seed);
        var rebuilt = vm.BuildConfig();
        Assert.Equal(before, JsonSerializer.Serialize(rebuilt, Options));
    }

    [Fact]
    public void AddRemoveSetupCommand_MutatesCollection()
    {
        var vm = new WorkloadEditorViewModel();
        vm.Load(Seed());
        var before = vm.SetupCommands.Count;
        vm.AddSetupCommandCommand.Execute(null);
        Assert.Equal(before + 1, vm.SetupCommands.Count);
        vm.RemoveSetupCommandCommand.Execute(vm.SetupCommands[before]);
        Assert.Equal(before, vm.SetupCommands.Count);
    }

    [Fact]
    public void BuildConfig_FiltersEmptySetupCommands()
    {
        var vm = new WorkloadEditorViewModel();
        vm.Load(Seed());
        vm.AddSetupCommandCommand.Execute(null); // empty string
        var rebuilt = vm.BuildConfig();
        Assert.Equal(2, rebuilt.SetupCommands.Count); // the empty row is filtered out
    }

    [Fact]
    public void Save_BlocksOnEmptyName()
    {
        var vm = new WorkloadEditorViewModel();
        vm.Load(Seed());
        vm.Name = string.Empty;
        string? captured = null;
        vm.SaveRequested += json => captured = json;
        vm.SaveCommand.Execute(null);
        Assert.Null(captured);
        Assert.Contains("Name is required", vm.ValidationError);
    }
}
