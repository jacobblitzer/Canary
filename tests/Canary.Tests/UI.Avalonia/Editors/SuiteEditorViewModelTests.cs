using System.Text.Json;
using Canary.Config;
using Canary.UI.Avalonia.ViewModels.Editors;
using Xunit;

namespace Canary.Tests.UI.Avalonia.Editors;

[Trait("Category", "Unit")]
public class SuiteEditorViewModelTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    [Fact]
    public void Load_PopulatesNameDescriptionKeepOpen()
    {
        var vm = new SuiteEditorViewModel();
        vm.Load(
            new SuiteDefinition { Name = "primary", Description = "smoke + slow path", KeepOpen = true, Tests = new() { "smoke" } },
            new[] { new TestDefinition { Name = "smoke", Workload = "qualia" }, new TestDefinition { Name = "deep", Workload = "qualia" } });

        Assert.Equal("primary", vm.Name);
        Assert.Equal("smoke + slow path", vm.Description);
        Assert.True(vm.KeepOpen);
        Assert.Equal(2, vm.AvailableTests.Count);
        Assert.True(vm.AvailableTests.First(t => t.TestName == "smoke").IsSelected);
        Assert.False(vm.AvailableTests.First(t => t.TestName == "deep").IsSelected);
    }

    [Fact]
    public void RoundTrip_IsIdempotent()
    {
        var seed = new SuiteDefinition { Name = "primary", Description = "smoke", KeepOpen = false, Tests = new() { "a", "b" } };
        var before = JsonSerializer.Serialize(seed, Options);

        var vm = new SuiteEditorViewModel();
        vm.Load(seed, new[] { new TestDefinition { Name = "a", Workload = "w" }, new TestDefinition { Name = "b", Workload = "w" } });
        var rebuilt = vm.BuildDefinition();
        Assert.Equal(before, JsonSerializer.Serialize(rebuilt, Options));
    }

    [Fact]
    public void Save_BlocksOnEmptyName()
    {
        var vm = new SuiteEditorViewModel();
        vm.Load(new SuiteDefinition { Name = "", Tests = new() { "a" } },
            new[] { new TestDefinition { Name = "a", Workload = "w" } });
        // Force the selection in case Load normalized it differently.
        vm.AvailableTests[0].IsSelected = true;
        string? captured = null;
        vm.SaveRequested += json => captured = json;
        vm.SaveCommand.Execute(null);
        Assert.Null(captured);
        Assert.Contains("Name is required", vm.ValidationError);
    }

    [Fact]
    public void Save_BlocksOnNoSelectedTests()
    {
        var vm = new SuiteEditorViewModel();
        vm.Load(new SuiteDefinition { Name = "primary", Tests = new() },
            new[] { new TestDefinition { Name = "a", Workload = "w" } });
        string? captured = null;
        vm.SaveRequested += json => captured = json;
        vm.SaveCommand.Execute(null);
        Assert.Null(captured);
        Assert.Contains("at least one test", vm.ValidationError);
    }
}
