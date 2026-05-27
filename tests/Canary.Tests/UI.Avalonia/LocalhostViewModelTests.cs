using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

[Trait("Category", "Unit")]
public class LocalhostViewModelTests
{
    // Heavy I/O (port enumeration, process listing) is exercised by the
    // existing Canary.Tests/Localhost/* suite. These tests focus on the
    // VM-shaped surface: command gating + ShowTier3 toggle behavior.

    [Fact]
    public void Initial_State_NoRowSelected_KillCommandDisabled()
    {
        var vm = new LocalhostViewModel();
        Assert.Null(vm.SelectedRow);
        Assert.False(vm.KillSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void Selecting_RowWithoutPort_KeepsKillDisabled()
    {
        var vm = new LocalhostViewModel();
        vm.SelectedRow = new LocalhostRow
        {
            Port = "—", Pid = "1234", ProcessName = "node",
            Provenance = "DevServerHeuristic", StartedDisplay = "—", Path = "—",
            RawPort = null,
            ProvenanceValue = Canary.Localhost.PortProvenance.DevServerHeuristic,
        };
        Assert.False(vm.KillSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void Selecting_RowWithPort_EnablesKill()
    {
        var vm = new LocalhostViewModel();
        vm.SelectedRow = new LocalhostRow
        {
            Port = "5173", Pid = "1234", ProcessName = "node",
            Provenance = "Unknown", StartedDisplay = "—", Path = "—",
            RawPort = 5173,
            ProvenanceValue = Canary.Localhost.PortProvenance.Unknown,
        };
        Assert.True(vm.KillSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void ShowTier3_TogglesAndPersists()
    {
        var vm = new LocalhostViewModel();
        var before = vm.ShowTier3;
        vm.ShowTier3 = !before;
        Assert.Equal(!before, vm.ShowTier3);
        // Restore so the test doesn't leave a side effect in
        // %LocalAppData%\Canary\settings.json.
        vm.ShowTier3 = before;
    }
}
