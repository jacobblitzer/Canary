using Canary.Settings;
using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class SettingsViewModelTests
{
    private static SettingsViewModel MakeVm(string uiMode = "stabilization", bool tier3 = false, int retention = 14)
        => new SettingsViewModel(new CanarySettings { UiMode = uiMode, ShowTier3Processes = tier3, RetentionDays = retention });

    [Fact]
    public void Initial_State_MatchesSettings_Stabilization()
    {
        var vm = MakeVm();
        Assert.True(vm.IsStabilization);
        Assert.False(vm.IsMaturation);
        Assert.False(vm.ShowTier3);
        Assert.Equal(14, vm.RetentionDays);
    }

    [Fact]
    public void Initial_State_MatchesSettings_Maturation()
    {
        var vm = MakeVm(uiMode: "maturation", tier3: true, retention: 30);
        Assert.False(vm.IsStabilization);
        Assert.True(vm.IsMaturation);
        Assert.True(vm.ShowTier3);
        Assert.Equal(30, vm.RetentionDays);
    }

    [Fact]
    public void Toggling_Maturation_FlipsStabilization()
    {
        var vm = MakeVm();
        vm.IsMaturation = true;
        Assert.True(vm.IsMaturation);
        Assert.False(vm.IsStabilization);
        Assert.Equal("maturation", vm.Snapshot().UiMode);
    }

    [Fact]
    public void Toggling_Tier3_PersistsToSettings()
    {
        var vm = MakeVm();
        vm.ShowTier3 = true;
        Assert.True(vm.Snapshot().ShowTier3Processes);
    }

    [Fact]
    public void Setting_RetentionDays_ClampsToValidRange()
    {
        var vm = MakeVm(retention: 100);
        vm.RetentionDays = 99;
        Assert.Equal(99, vm.Snapshot().RetentionDays);
    }

    [Fact]
    public void StatusText_ReflectsCurrentSettings()
    {
        var vm = MakeVm();
        Assert.Contains("Stabilization", vm.StatusText);
        vm.IsMaturation = true;
        Assert.Contains("Maturation", vm.StatusText);
    }
}
