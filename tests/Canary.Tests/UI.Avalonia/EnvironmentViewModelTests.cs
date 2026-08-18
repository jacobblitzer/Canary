using System.Text.Json;
using Canary.Agent;
using Canary.UI.Avalonia.ViewModels;
using Xunit;

namespace Canary.Tests.UI.Avalonia;

/// <summary>
/// Deployment campaign Phase 5b — the Environment tab.
/// </summary>
/// <remarks>
/// The tab exists so a machine that is NOT set up correctly cannot look like one that is, so
/// most of these tests are about the difference between "nothing is wrong" and "I have not
/// looked". An empty grid can honestly mean either, and only the status line can tell them
/// apart.
/// </remarks>
[Trait("Category", "Unit")]
public class EnvironmentViewModelTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "canary-env-vm-" + Guid.NewGuid().ToString("N"));

    /// <summary>Writes a workload with an optional captured report.</summary>
    private static void WriteWorkload(
        string root, string name, string? loaded = null, string? scanFolders = null,
        string? discovered = null, string? hostReady = "true",
        IEnumerable<(string Severity, string Kind, string Detail)>? findings = null,
        string? rawJson = null)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(dir, "tests"));
        File.WriteAllText(Path.Combine(dir, "workload.json"),
            JsonSerializer.Serialize(new { name, displayName = name }));

        if (loaded == null && findings == null && rawJson == null) return;

        var results = Path.Combine(dir, "results");
        Directory.CreateDirectory(results);
        var path = Path.Combine(results, "environment.json");

        if (rawJson != null) { File.WriteAllText(path, rawJson); return; }

        var host = new Dictionary<string, string>
        {
            [HostStateFields.Host] = "rhino",
            [HostStateFields.HostVersion] = "8.34.26223.11001",
            [HostStateFields.Framework] = ".NET 8.0.23",
        };
        if (hostReady != null) host[HostStateFields.HostReady] = hostReady;
        if (loaded != null) host[HostStateFields.Loaded] = loaded;
        if (scanFolders != null) host[HostStateFields.ScanFolders] = scanFolders;
        if (discovered != null) host[HostStateFields.Discovered] = discovered;

        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            capturedUtc = "2026-08-18T17:11:11Z",
            workload = name,
            host,
            findings = (findings ?? Array.Empty<(string, string, string)>())
                .Select(f => new { severity = f.Severity, kind = f.Kind, detail = f.Detail }),
        }));
    }

    private static void With(Action<string> body)
    {
        var root = NewRoot();
        try { Directory.CreateDirectory(root); body(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    // -----------------------------------------------------------------------
    // "I have not looked" must never render as "nothing is wrong".
    // -----------------------------------------------------------------------

    [Fact]
    public void WithNoWorkloadsDir_SaysSo_AndShowsNothing()
    {
        var vm = new EnvironmentViewModel();

        Assert.Empty(vm.Plugins);
        Assert.Empty(vm.Findings);
        Assert.Contains("Open a workloads folder", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The distinction the whole tab turns on: no capture is not a clean capture.
    /// </summary>
    [Fact]
    public void AWorkloadWithNoCapture_IsReportedAsUncaptured_NotAsClean()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino");

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Empty(vm.Plugins);
            // An empty grid alone is ambiguous, so the status line must resolve it.
            Assert.Contains("No capture for", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, vm.CapturedAt);
            Assert.Contains("no capture", vm.HostSummary, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ACaptureWithNothingLoaded_IsDistinguishableFromNoCapture()
    {
        With(root =>
        {
            // A real capture that genuinely found nothing: loaded present but empty.
            WriteWorkload(root, "rhino", loaded: string.Empty, findings: Array.Empty<(string, string, string)>());

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Empty(vm.Plugins);
            Assert.DoesNotContain("No capture for", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("0 loaded", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(string.Empty, vm.CapturedAt);
        });
    }

    /// <summary>A corrupt report must not read as a clean one.</summary>
    [Fact]
    public void ACorruptCapture_ReportsTheReadFailure_RatherThanLookingEmpty()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", rawJson: "{ this is not json");

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Empty(vm.Plugins);
            Assert.Contains("Could not read", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// A BOM-prefixed report still parses.
    /// </summary>
    /// <remarks>
    /// A BOM already cost this campaign a silently-unverified manifest row, so it is worth one
    /// test rather than one more incident.
    /// </remarks>
    [Fact]
    public void ABomPrefixedCapture_StillParses()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", loaded: "gh:Slop=1.0@C:/GH/Libraries/Slop.gha");
            var path = Path.Combine(root, "rhino", "results", "environment.json");
            var json = File.ReadAllText(path);
            File.WriteAllText(path, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Single(vm.Plugins);
            Assert.DoesNotContain("Could not read", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        });
    }

    // -----------------------------------------------------------------------
    // The content: plug-ins, origins, findings, folders.
    // -----------------------------------------------------------------------

    [Fact]
    public void LoadedPlugins_AreShownWithTheirOrigin()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", loaded: string.Join(Environment.NewLine, new[]
            {
                @"gh:Slop=1.0@C:\Repos\Slop\bin\Release\net48\Slop.gha",
                @"gh:Kangaroo=5.0@C:\Users\x\AppData\Roaming\Grasshopper\Libraries\Kangaroo.gha",
            }));

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Equal(2, vm.Plugins.Count);
            var slop = Assert.Single(vm.Plugins, p => p.Id == "gh:Slop");
            // The distinction that makes install/update honest: a build-output folder shadows
            // a deployed install, so "developer" must be visible, not inferred.
            Assert.Equal("developer", slop.Origin);
            Assert.Equal("1.0", slop.Version);

            var kangaroo = Assert.Single(vm.Plugins, p => p.Id == "gh:Kangaroo");
            Assert.Equal("libraries", kangaroo.Origin);
        });
    }

    [Fact]
    public void Findings_AreSurfacedAndCountedBySeverity()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino",
                loaded: "gh:Slop=1.0@C:/GH/Libraries/Slop.gha",
                findings: new[]
                {
                    ("Error", "duplicate-id", "gh:Slop is loaded more than once"),
                    ("Warning", "present-but-not-loaded", "CPig.gha sits on a scanned folder"),
                    ("Note", "developer-origin", "gh:Slop loaded from a build output"),
                });

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Equal(3, vm.Findings.Count);
            Assert.Contains("1 error", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("1 warning", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("1 note", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ScanFolders_AreSplitIntoPathAndExistence()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino",
                loaded: string.Empty,
                scanFolders: string.Join(Environment.NewLine, new[]
                {
                    @"C:\Users\x\AppData\Roaming\Grasshopper\Libraries|OK",
                    @"D:\gone|MISSING",
                }));

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Equal(2, vm.ScanFolders.Count);
            var dead = Assert.Single(vm.ScanFolders, f => f.Exists == "MISSING");
            Assert.Equal(@"D:\gone", dead.Path);
        });
    }

    /// <summary>
    /// A partial probe is surfaced: some sections could not be read, so the report is
    /// incomplete rather than reassuring.
    /// </summary>
    [Fact]
    public void APartialProbe_IsCalledOutInTheStatusLine()
    {
        With(root =>
        {
            var dir = Path.Combine(root, "rhino", "results");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(root, "rhino", "tests"));
            File.WriteAllText(Path.Combine(root, "rhino", "workload.json"),
                JsonSerializer.Serialize(new { name = "rhino" }));
            File.WriteAllText(Path.Combine(dir, "environment.json"), JsonSerializer.Serialize(new
            {
                capturedUtc = "2026-08-18T17:11:11Z",
                workload = "rhino",
                host = new Dictionary<string, string>
                {
                    [HostStateFields.Host] = "rhino",
                    [HostStateFields.HostReady] = "true",
                    [HostStateFields.Loaded] = string.Empty,
                    [HostStateFields.PartialFailures] = "loadGrasshopper: timed out",
                },
                findings = Array.Empty<object>(),
            }));

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Contains("PARTIAL PROBE", vm.StatusText, StringComparison.Ordinal);
            Assert.Contains("timed out", vm.StatusText, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// An unready host reports its readiness, so "nothing loaded" is not mistaken for a fact.
    /// </summary>
    [Fact]
    public void AnUnreadyHost_ShowsItsReadinessInTheHostSummary()
    {
        With(root =>
        {
            WriteWorkload(root, "rhino", loaded: string.Empty, hostReady: "false");

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Contains("ready: false", vm.HostSummary, StringComparison.OrdinalIgnoreCase);
        });
    }

    // -----------------------------------------------------------------------
    // Workload selection.
    // -----------------------------------------------------------------------

    [Fact]
    public void SwitchingWorkload_ReloadsThatWorkloadsCapture()
    {
        With(root =>
        {
            WriteWorkload(root, "aaa", loaded: "gh:One=1.0@C:/GH/Libraries/One.gha");
            WriteWorkload(root, "bbb", loaded: string.Join(Environment.NewLine, new[]
            {
                "gh:Two=2.0@C:/GH/Libraries/Two.gha",
                "gh:Three=3.0@C:/GH/Libraries/Three.gha",
            }));

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Equal(new[] { "aaa", "bbb" }, vm.Workloads);
            Assert.Equal("aaa", vm.SelectedWorkload);
            Assert.Single(vm.Plugins);

            vm.SelectedWorkload = "bbb";
            Assert.Equal(2, vm.Plugins.Count);
        });
    }

    /// <summary>
    /// Re-pointing at a different root with the same workload names still reloads.
    /// </summary>
    /// <remarks>
    /// SelectedWorkload does not change value here, so the change hook does not fire — this
    /// is why SetWorkloadsDir refreshes explicitly instead of relying on it. Without that,
    /// the tab would keep showing the PREVIOUS machine's report under the new root, which is
    /// the worst possible lie for a tool whose job is comparing machines.
    /// </remarks>
    [Fact]
    public void RepointingToANewRootWithTheSameWorkloadName_ReloadsRatherThanKeepingStaleRows()
    {
        var a = NewRoot();
        var b = NewRoot();
        try
        {
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            WriteWorkload(a, "rhino", loaded: "gh:One=1.0@C:/GH/Libraries/One.gha");
            WriteWorkload(b, "rhino", loaded: string.Join(Environment.NewLine, new[]
            {
                "gh:Two=2.0@C:/GH/Libraries/Two.gha",
                "gh:Three=3.0@C:/GH/Libraries/Three.gha",
            }));

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(a);
            Assert.Single(vm.Plugins);
            Assert.Equal("gh:One", vm.Plugins[0].Id);

            vm.SetWorkloadsDir(b);
            Assert.Equal(2, vm.Plugins.Count);
            Assert.DoesNotContain(vm.Plugins, p => p.Id == "gh:One");
        }
        finally
        {
            if (Directory.Exists(a)) Directory.Delete(a, recursive: true);
            if (Directory.Exists(b)) Directory.Delete(b, recursive: true);
        }
    }

    [Fact]
    public void AnEmptyWorkloadsRoot_SaysNoWorkloadsRatherThanShowingAnEmptyGrid()
    {
        With(root =>
        {
            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Empty(vm.Workloads);
            Assert.Contains("No workloads found", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        });
    }

    // -----------------------------------------------------------------------
    // Requirements: unchecked must not read as OK, and unjudgeable must not vanish.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RequirementsBeforeChecking_ReadAsNotChecked_NotAsOk()
    {
        await WithAsync(async root =>
        {
            WriteRequirements(root, "rhino",
                new { kind = "file", path = Path.Combine(root, "rhino", "tests") },
                new { kind = "plugin", id = "gh:Slop" });

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);

            Assert.Equal(2, vm.Requirements.Count);
            Assert.Contains(vm.Requirements, r => r.Status == "not checked");
            Assert.DoesNotContain(vm.Requirements, r => r.Status == "OK");
            await Task.CompletedTask;
        });
    }

    /// <summary>
    /// Checking resolves the offline-decidable ones and leaves plug-ins visibly unjudged.
    /// </summary>
    /// <remarks>
    /// <c>CheckOfflineAsync</c> returns only MISSES, so a plugin requirement it cannot judge
    /// is absent from that list — indistinguishable from a pass. Dropping it from the grid
    /// would make a half-checked machine read as fully checked.
    /// </remarks>
    [Fact]
    public async Task Checking_MarksFilesOkOrMissing_AndKeepsPluginsVisiblyUnjudged()
    {
        await WithAsync(async root =>
        {
            WriteRequirements(root, "rhino",
                new { kind = "file", path = Path.Combine(root, "rhino", "workload.json") },
                new { kind = "file", path = Path.Combine(root, "rhino", "definitely-absent.3dm") },
                new { kind = "plugin", id = "gh:Slop" });

            var vm = new EnvironmentViewModel();
            vm.SetWorkloadsDir(root);
            await vm.CheckCommand.ExecuteAsync(null);

            Assert.Equal(3, vm.Requirements.Count);
            Assert.Single(vm.Requirements, r => r.Status == "OK");
            var missing = Assert.Single(vm.Requirements, r => r.Status == "MISSING");
            Assert.Contains("definitely-absent", missing.Requirement, StringComparison.OrdinalIgnoreCase);

            var plugin = Assert.Single(vm.Requirements, r => r.Status == "in-app only");
            Assert.Contains("gh:Slop", plugin.Requirement, StringComparison.Ordinal);

            Assert.Contains("1 missing", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("only be judged inside the application", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void WriteRequirements(string root, string workload, params object[] requirements)
    {
        var dir = Path.Combine(root, workload);
        Directory.CreateDirectory(Path.Combine(dir, "tests"));
        File.WriteAllText(Path.Combine(dir, "workload.json"),
            JsonSerializer.Serialize(new { name = workload, displayName = workload, requires = requirements }));
    }

    private static async Task WithAsync(Func<string, Task> body)
    {
        var root = NewRoot();
        try { Directory.CreateDirectory(root); await body(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
