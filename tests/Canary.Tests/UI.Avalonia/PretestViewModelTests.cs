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

    // --- the copy-report button ------------------------------------------------
    //
    // Operator feedback 2026-08-19: every panel was readable and none of it was copyable in
    // one go, and the real workflow on the QC machine is pasting state back and forth with an
    // agent. A surface whose findings must be retyped is one whose findings get summarised
    // from memory - which is the thing this campaign replaced.

    /// <summary>The report carries every panel, not just the verdict.</summary>
    [Fact]
    public void TheReport_CarriesEveryPanel()
    {
        With(root =>
        {
            // gh:Absent is declared and unprovided; rhino:Penumbra.Rhino is declared, missing
            // AND provided by a package - only the latter can produce an install command, which
            // is the distinction the plan section depends on.
            WriteWorkload(root, "rhino",
                ("plugin", "gh:Slop"), ("plugin", "gh:Absent"), ("plugin", "rhino:Penumbra.Rhino"));
            WritePackageMap(root);
            WriteCapture(root, "rhino", @"gh:Slop=1.0@C:\Repos\Slopin\Release
et48\Slop.gha");
            new CommissioningReport
            {
                CapturedUtc = "2026-08-19T00:00:00Z",
                Machine = MachineIdentity.Describe(root),
                Workload = "rhino",
                Layers = new[]
                {
                    new CommissioningLayer(1, "comparer", LayerOutcome.Passed, "self 0, pair 256/4096", true),
                    new CommissioningLayer(2, "repeatable", LayerOutcome.Passed, "identical", true),
                    new CommissioningLayer(3, "reference", LayerOutcome.Failed, "differs by 4%", false),
                },
            }.Save(CommissioningReport.PathFor(root));

            var vm = new PretestViewModel();
            vm.SetWorkloadsDir(root);
            var report = vm.BuildReport();

            // the verdict and the ruling-12 stamp
            Assert.Contains("# Canary pretest", report, StringComparison.Ordinal);
            Assert.Contains(Environment.MachineName, report, StringComparison.Ordinal);
            Assert.Contains("tier evidence", report, StringComparison.Ordinal);

            // all three layers, with their fatality - the distinction a reader most needs
            Assert.Contains("comparer", report, StringComparison.Ordinal);
            Assert.Contains("repeatable", report, StringComparison.Ordinal);
            Assert.Contains("reference", report, StringComparison.Ordinal);
            Assert.Contains("not fatal", report, StringComparison.OrdinalIgnoreCase);

            // the readiness join, both states
            Assert.Contains("gh:Slop", report, StringComparison.Ordinal);
            Assert.Contains("gh:Absent", report, StringComparison.Ordinal);
            Assert.Contains("developer", report, StringComparison.Ordinal);

            // the install plan, and that it is not run from here
            Assert.Contains("machine-setup.ps1", report, StringComparison.Ordinal);
            Assert.Contains("NOT run from the UI", report, StringComparison.Ordinal);

            // the exit-code semantics, so a reader out of context cannot collapse them
            Assert.Contains("**4**", report, StringComparison.Ordinal);
            Assert.Contains("**1**", report, StringComparison.Ordinal);
            Assert.Contains("**3**", report, StringComparison.Ordinal);

            // and where the machine-readable originals live
            Assert.Contains("commissioning-report.json", report, StringComparison.Ordinal);
        });
    }

    /// <summary>An unmeasured machine produces a report that SAYS it is unmeasured.</summary>
    /// <remarks>
    /// The failure mode this guards is a report that looks complete because every empty
    /// section rendered as an empty table - a reader would take that for "nothing wrong".
    /// </remarks>
    [Fact]
    public void TheReport_OnAnUnmeasuredMachine_SaysSoRatherThanLookingEmpty()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino");
            var vm = new PretestViewModel();
            vm.SetWorkloadsDir(root);

            var report = vm.BuildReport();

            Assert.Contains("Not commissioned", report, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Not surveyed", report, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void CopyReport_GoesToTheClipboard()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino");
            var vm = new PretestViewModel();
            string? copied = null;
            vm.CopyToClipboard = t => copied = t;
            vm.SetWorkloadsDir(root);

            vm.CopyReportCommand.Execute(null);

            Assert.NotNull(copied);
            Assert.Equal(vm.BuildReport(), copied);
            Assert.Equal(PretestState.Idle, vm.State);
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

    // ------------------------------------- what the first pasted report got wrong
    //
    // Every test below came from the operator running this tab on the DEV machine and
    // pasting the report back, 2026-08-19. None of them is hypothetical: each is a line
    // that was in that paste and could be misread by whoever reads the next one.

    private static string WriteSurvey(string root, object survey)
    {
        var path = Path.Combine(root, "survey.json");
        File.WriteAllText(path, JsonSerializer.Serialize(survey));
        return path;
    }

    /// <summary>A Rhino install with no Rhino.exe is a named row, not a blank one.</summary>
    /// <remarks>
    /// A Rhino 9 WIP folder has no <c>System\Rhino.exe</c>, so the survey reports a null
    /// version — and the first version of this rendered the VERSION as the row name, so the
    /// row read "- : C:\Program Files\Rhino 9 WIP". Which install it is, is the identity; the
    /// version is the part allowed to be absent.
    /// </remarks>
    [Fact]
    public void ARhinoInstallWithNoExe_IsNamedByItsFolder_AndSaysWhyThereIsNoVersion()
    {
        With(root =>
        {
            var path = WriteSurvey(root, new
            {
                rhino = new
                {
                    installs = new object[]
                    {
                        new { dir = @"C:\Program Files\Rhino 8", exe = @"C:\Program Files\Rhino 8\System\Rhino.exe", version = "8.24" },
                        new { dir = @"C:\Program Files\Rhino 9 WIP", exe = (string?)null, version = (string?)null },
                    },
                },
            });

            var vm = new PretestViewModel();
            vm.LoadSurvey(path);

            var rows = vm.MachineFacts.Where(f => f.Group == "Rhino").ToList();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
            Assert.Contains(rows, r => r.Name.EndsWith("Rhino 8", StringComparison.Ordinal) && r.Value == "8.24");
            var wip = Assert.Single(rows, r => r.Name.EndsWith("Rhino 9 WIP", StringComparison.Ordinal));
            Assert.Contains("Rhino.exe", wip.Value, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>Directories that are not checkouts are counted, not listed as blank rows.</summary>
    /// <remarks>
    /// The survey walks every directory under the repo root, so caches and scratch folders
    /// appear with a null branch and head. Rendered, that is a row reading "` @ `" — which in
    /// a pasted table looks like a repo that is fine rather than a thing that is not a repo.
    /// </remarks>
    [Fact]
    public void NonRepoDirectories_AreSummarised_NotListedBlank()
    {
        With(root =>
        {
            var path = WriteSurvey(root, new
            {
                repos = new object[]
                {
                    new { name = "Canary", isGit = true, branch = "main", head = "abc1234", dirty = false },
                    new { name = "files", isGit = false, branch = (string?)null, head = (string?)null, dirty = (bool?)null },
                    new { name = "cache", isGit = false, branch = (string?)null, head = (string?)null, dirty = (bool?)null },
                },
            });

            var vm = new PretestViewModel();
            vm.LoadSurvey(path);

            var rows = vm.MachineFacts.Where(f => f.Group == "Repos").ToList();
            Assert.DoesNotContain(rows, r => r.Name is "files" or "cache");
            Assert.Contains(rows, r => r.Name == "Canary" && r.Value.Contains("main", StringComparison.Ordinal));
            Assert.Contains(rows, r => r.Value.Contains("2 director", StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>The survey does not restate what the identity stamp already carries.</summary>
    /// <remarks>
    /// The stamp reads <c>RuntimeInformation.OSDescription</c> and the survey reads the CIM
    /// caption, so showing both put "Microsoft Windows 10.0.26200" and "Microsoft Windows 11
    /// Pro" in one report. Both are right; together they read as a contradiction.
    /// </remarks>
    [Fact]
    public void TheSurveyDoesNotRepeatTheIdentityStamp()
    {
        With(root =>
        {
            var path = WriteSurvey(root, new
            {
                identity = new { machineName = "DESKTOP-X", user = "jake", os = "Microsoft Windows 11 Pro", domain = "WORKGROUP" },
            });

            var vm = new PretestViewModel();
            vm.LoadSurvey(path);

            var rows = vm.MachineFacts.Where(f => f.Group == "Machine").ToList();
            Assert.DoesNotContain(rows, r => r.Name is "machineName" or "user" or "os");
            Assert.Contains(rows, r => r.Name == "domain");
        });
    }

    /// <summary>An id no package map mentions says so, rather than showing an empty cell.</summary>
    [Fact]
    public void AnUnmappedId_SaysUnmapped_RatherThanBlank()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", ("plugin", "gh:KinematicImporter"));
            WritePackageMap(root);
            WriteCapture(root, "rhino", "gh:Other=1.0@C:/x/Other.gha");

            var vm = new PretestViewModel();
            vm.SetWorkloadsDir(root);

            var row = Assert.Single(vm.Readiness);
            Assert.False(string.IsNullOrWhiteSpace(row.Grounded));
            Assert.Contains("unmapped", row.Grounded, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>An unrun doctor reads as unrun, and the report says that is not a pass.</summary>
    /// <remarks>
    /// Same rule as a fatal commissioning layer sitting at NotRun, and as an absent
    /// hostReady: the gap between "we checked and it was fine" and "we did not check" is the
    /// gap this whole campaign exists to hold open.
    /// </remarks>
    [Fact]
    public void TheReport_WithNoDoctorRun_SaysSo_AndSaysItIsNotAPass()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino");
            var vm = new PretestViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Null(vm.DoctorExit);

            var report = vm.BuildReport();

            Assert.Contains("doctor has not been run", report, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not a pass", report, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>Doctor's verdict is in the report, separately from commissioning's.</summary>
    /// <remarks>
    /// The report the operator first pasted carried a harness verdict and a machine survey
    /// and no install verdict at all, which leaves a reader to infer the install from the
    /// other two. They are three findings with three different owners.
    /// </remarks>
    [Fact]
    public async Task TheReport_CarriesDoctorsVerdictSeparately()
    {
        var root = Path.Combine(Path.GetTempPath(), "canary-pretest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            WriteWorkload(root, "rhino");
            var vm = new PretestViewModel();
            vm.SetWorkloadsDir(root);

            await vm.RunDoctorCommand.ExecuteAsync(null);

            Assert.NotNull(vm.DoctorExit);
            var report = vm.BuildReport();
            Assert.Contains("what doctor says", report, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"exit {vm.DoctorExit}", report, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(PretestState.Idle, vm.State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
