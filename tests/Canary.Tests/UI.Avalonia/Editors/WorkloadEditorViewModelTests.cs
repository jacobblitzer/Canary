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
    public void RoundTrip_PreservesUnknownAgentConfigBlocks()
    {
        // Regression: bug 0018 — the editor's load → mutate → serialize →
        // overwrite path silently DELETED every JSON section WorkloadConfig
        // doesn't model (qualiaConfig / penumbraConfig / future knobs),
        // destroying the browser workloads' agent config on any UI Save.
        var json = """
        {
          "name": "qualia-web",
          "displayName": "Qualia (Deployed Web — vite preview)",
          "appPath": "npm.cmd",
          "appArgs": "run preview",
          "agentType": "qualia-cdp",
          "pipeName": "canary-qualia-web",
          "startupTimeoutMs": 30000,
          "windowTitle": "Qualia",
          "viewportClass": "",
          "qualiaConfig": {
            "projectDir": "C:\\Repos\\Qualia",
            "cdpPort": 9225,
            "vitePort": 4173,
            "viteScript": "preview",
            "desktop": true,
            "appExePath": "C:\\Repos\\Qualia\\src-tauri\\target\\release\\app.exe"
          },
          "penumbraConfig": { "anything": [1, 2, 3] },
          "futureTopLevelKnob": "survives"
        }
        """;

        var config = WorkloadConfig.Parse(json);
        var vm = new WorkloadEditorViewModel();
        vm.Load(config);
        vm.DisplayName = "Edited display name"; // a real editor mutation

        using var doc = JsonDocument.Parse(vm.ToJson());
        var root = doc.RootElement;

        Assert.Equal("Edited display name", root.GetProperty("displayName").GetString());
        var qc = root.GetProperty("qualiaConfig");
        Assert.Equal("preview", qc.GetProperty("viteScript").GetString());
        Assert.Equal(4173, qc.GetProperty("vitePort").GetInt32());
        Assert.Equal(9225, qc.GetProperty("cdpPort").GetInt32());
        Assert.True(qc.GetProperty("desktop").GetBoolean());
        Assert.Equal("C:\\Repos\\Qualia\\src-tauri\\target\\release\\app.exe",
            qc.GetProperty("appExePath").GetString());
        Assert.True(root.TryGetProperty("penumbraConfig", out _));
        Assert.Equal("survives", root.GetProperty("futureTopLevelKnob").GetString());
    }

    [Fact]
    public void RealQualiaWebWorkload_SurvivesEditorSave()
    {
        // Bug 0018's operator-facing proof: the ACTUAL
        // workloads/qualia-web/workload.json survives an editor round-trip
        // with its qualiaConfig block (viteScript=preview et al) intact.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Canary.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir); // running outside the repo tree is a test-env error
        var path = Path.Combine(dir!.FullName, "workloads", "qualia-web", "workload.json");
        Assert.True(File.Exists(path), $"expected {path}");

        var config = WorkloadConfig.Parse(File.ReadAllText(path));
        var vm = new WorkloadEditorViewModel();
        vm.Load(config);

        using var doc = JsonDocument.Parse(vm.ToJson());
        var qc = doc.RootElement.GetProperty("qualiaConfig");
        Assert.Equal("preview", qc.GetProperty("viteScript").GetString());
        Assert.Equal(4173, qc.GetProperty("vitePort").GetInt32());
        Assert.Equal(9225, qc.GetProperty("cdpPort").GetInt32());
        Assert.Equal("C:\\Repos\\Qualia", qc.GetProperty("projectDir").GetString());
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
