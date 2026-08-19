using System.Text.Json;
using Canary.Agent;
using Canary.Commissioning;
using Canary.Orchestration;
using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

/// <summary>
/// Deployment campaign Stage C4 — the Pretest tab.
/// </summary>
/// <remarks>
/// The tab answers "is this machine ready to be believed" before any test runs, so most of
/// these tests are about it refusing to overstate: an unmeasured machine must not look
/// measured, and an unknown requirement must not look missing.
/// </remarks>
[Trait("Category", "Unit")]
public class PretestViewModelTests
{
    private static void With(Action<string> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-pretest-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(root); body(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void WriteWorkload(string root, string name, params (string Kind, string Id)[] requires)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(dir, "tests"));
        File.WriteAllText(Path.Combine(dir, "workload.json"), JsonSerializer.Serialize(new
        {
            name,
            displayName = name,
            requires = requires.Select(r => new { kind = r.Kind, id = r.Id, fix = "install it" }),
        }));
    }

    private static void WritePackageMap(string root)
        => File.WriteAllText(Path.Combine(root, InstallReadiness.PackageMapFileName), JsonSerializer.Serialize(new
        {
            source = "%CANARY_HANDOFF%/_yak",
            packages = new[]
            {
                new { package = "slop", ids = new[] { "gh:Slop" }, grounded = "capture" },
                new { package = "penumbra", ids = new[] { "rhino:Penumbra.Rhino" }, grounded = "inferred" },
            },
        }));

    private static void WriteCapture(string root, string workload, params string[] loadedRows)
        => EnvironmentCapture.Create(
                workload,
                new Dictionary<string, string> { [HostStateFields.Loaded] = string.Join("\n", loadedRows) },
                Array.Empty<EnvironmentClash>(),
                workloadsDir: root)
            .Save(EnvironmentCapture.PathFor(root, workload));

    // ------------------------------------------------- unknown is not missing

    /// <summary>
    /// With no capture, every requirement is Unknown — never Missing.
    /// </summary>
    /// <remarks>
    /// "Everything is missing" is a confident false answer, and it is the one that would send
    /// a setup pass installing things the machine already has. This is the same distinction
    /// <c>machine-setup.ps1</c> makes, and the reason it exists at all.
    /// </remarks>
    [Fact]
    public void WithNoCapture_RequirementsAreUnknown_NotMissing()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", ("plugin", "gh:Slop"));
            WritePackageMap(root);

            var rows = InstallReadiness.ForWorkload(root, "rhino");

            Assert.Equal(RequirementState.Unknown, Assert.Single(rows).State);
        });
    }

    [Fact]
    public void WithACapture_PresentAndMissingAreDistinguished()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", ("plugin", "gh:Slop"), ("plugin", "gh:Absent"));
            WritePackageMap(root);
            WriteCapture(root, "rhino", @"gh:Slop=1.0@C:\Users\x\AppData\Roaming\Grasshopper\Libraries\Slop.gha");

            var rows = InstallReadiness.ForWorkload(root, "rhino").ToDictionary(r => r.Id);

            Assert.Equal(RequirementState.Present, rows["gh:Slop"].State);
            Assert.Equal("libraries", rows["gh:Slop"].Origin);
            Assert.Equal("1.0", rows["gh:Slop"].Version);
            Assert.Equal("slop", rows["gh:Slop"].Package);
            Assert.Equal(RequirementState.Missing, rows["gh:Absent"].State);
        });
    }

    /// <summary>An id nothing provides is reported as such, not silently dropped.</summary>
    [Fact]
    public void ARequirementNoPackageProvides_IsStillListed()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", ("plugin", "gh:KinematicImporter"));
            WritePackageMap(root);
            WriteCapture(root, "rhino", "gh:Other=1.0@C:/x/Other.gha");

            var row = Assert.Single(InstallReadiness.ForWorkload(root, "rhino"));

            Assert.Equal(RequirementState.Missing, row.State);
            Assert.Equal(string.Empty, row.Package);
        });
    }

    /// <summary>An inferred id is carried through, so the UI can warn about trusting it.</summary>
    [Fact]
    public void AnInferredId_IsMarkedAsSuch()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", ("plugin", "rhino:Penumbra.Rhino"));
            WritePackageMap(root);
            WriteCapture(root, "rhino", "gh:Other=1.0@C:/x/Other.gha");

            var row = Assert.Single(InstallReadiness.ForWorkload(root, "rhino"));

            Assert.Equal("penumbra", row.Package);
            Assert.Equal("inferred", row.Grounded);
        });
    }

    /// <summary>file and service requirements are doctor's business, not the installer's.</summary>
    [Fact]
    public void OnlyPluginRequirementsAppear()
    {
        With(root =>
        {
            var dir = Path.Combine(root, "rhino");
            Directory.CreateDirectory(Path.Combine(dir, "tests"));
            File.WriteAllText(Path.Combine(dir, "workload.json"), JsonSerializer.Serialize(new
            {
                name = "rhino",
                displayName = "rhino",
                requires = new object[]
                {
                    new { kind = "plugin", id = "gh:Slop", fix = "x" },
                    new { kind = "file", path = "C:/nope.txt", fix = "x" },
                },
            }));

            var row = Assert.Single(InstallReadiness.ForWorkload(root, "rhino"));
            Assert.Equal("gh:Slop", row.Id);
        });
    }

    // -------------------------------------------------------- the view model

    [Fact]
    public void WithNoWorkloadsDir_SaysNothingHasBeenProven()
    {
        var vm = new PretestViewModel();

        Assert.Empty(vm.Layers);
        Assert.Empty(vm.Readiness);
        Assert.Contains("not been commissioned", vm.Verdict, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An uncommissioned machine says so, and does not look proven.
    /// </summary>
    [Fact]
    public void WithNoCommissioningReport_TheVerdictSaysSo()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", ("plugin", "gh:Slop"));
            var vm = new PretestViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Empty(vm.Layers);
            Assert.Contains("not been commissioned", vm.Verdict, StringComparison.OrdinalIgnoreCase);
            // Ruling 12's stamp is available with no run at all.
            Assert.Contains("tier", vm.Identity, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void WithAPassingReport_TheVerdictIsGreenAndTheLayersShow()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", ("plugin", "gh:Slop"));
            new CommissioningReport
            {
                CapturedUtc = "2026-08-19T00:00:00Z",
                Machine = MachineIdentity.Describe(root),
                Workload = "rhino",
                Layers = new[]
                {
                    new CommissioningLayer(1, "comparer", LayerOutcome.Passed, "ok", true),
                    new CommissioningLayer(2, "repeatable", LayerOutcome.Passed, "ok", true),
                    new CommissioningLayer(3, "reference", LayerOutcome.Passed, "ok", false),
                },
            }.Save(CommissioningReport.PathFor(root));

            var vm = new PretestViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Equal(3, vm.Layers.Count);
            Assert.Contains("can be read", vm.Verdict, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("#4EC94E", vm.VerdictBrush);
        });
    }

    /// <summary>A report from elsewhere must not be read as this machine's.</summary>
    [Fact]
    public void WithAForeignReport_TheVerdictRejectsIt()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", ("plugin", "gh:Slop"));
            new CommissioningReport
            {
                CapturedUtc = "2026-08-19T00:00:00Z",
                Machine = new Dictionary<string, string>
                {
                    [MachineIdentity.MachineName] = Environment.MachineName + "-ELSEWHERE",
                },
                Layers = new[] { new CommissioningLayer(1, "comparer", LayerOutcome.Passed, "ok", true) },
            }.Save(CommissioningReport.PathFor(root));

            var vm = new PretestViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Contains("DIFFERENT machine", vm.Verdict, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("#FF6B68", vm.VerdictBrush);
        });
    }

    /// <summary>
    /// The tab renders an install command; it never runs one.
    /// </summary>
    /// <remarks>
    /// Operator ruling: the UI reports and plans, and applying stays a deliberate act
    /// elsewhere. A machine repaired before it was measured has destroyed the evidence it
    /// existed to provide.
    /// </remarks>
    [Fact]
    public void AnInstallPlan_IsRenderedForCopying_NotExecuted()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", ("plugin", "gh:Slop"));
            WritePackageMap(root);
            WriteCapture(root, "rhino", "gh:Other=1.0@C:/x/Other.gha");

            var vm = new PretestViewModel();
            string? copied = null;
            vm.CopyToClipboard = t => copied = t;
            vm.SetWorkloadsDir(root);

            Assert.True(vm.HasInstallPlan);
            Assert.Contains("machine-setup.ps1", vm.InstallCommand, StringComparison.Ordinal);
            Assert.Contains("-Only slop", vm.InstallCommand, StringComparison.Ordinal);

            vm.CopyInstallCommandCommand.Execute(null);
            Assert.Equal(vm.InstallCommand, copied);
            // Copying is the whole action. Nothing ran.
            Assert.Equal(PretestState.Idle, vm.State);
        });
    }

    [Fact]
    public void WithNothingMissing_ThereIsNoInstallPlan()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", ("plugin", "gh:Slop"));
            WritePackageMap(root);
            WriteCapture(root, "rhino", "gh:Slop=1.0@C:/x/Slop.gha");

            var vm = new PretestViewModel();
            vm.SetWorkloadsDir(root);

            Assert.False(vm.HasInstallPlan);
            Assert.Equal(string.Empty, vm.InstallCommand);
        });
    }

    /// <summary>The commissioning workload is not offered as an app to measure against.</summary>
    /// <remarks>It carries no tests and launches nothing — selecting it could only fail.</remarks>
    [Fact]
    public void TheCommissioningWorkloadIsNotOfferedAsAnApp()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino");
            WriteWorkload(root, MachineTier.CommissioningWorkload);

            var vm = new PretestViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Equal(new[] { "rhino" }, vm.Workloads);
        });
    }
}
