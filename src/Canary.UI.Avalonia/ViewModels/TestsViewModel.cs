using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Canary.Config;
using Canary.Orchestration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

public partial class TestsViewModel : ObservableObject
{
    public WorkloadTreeViewModel Tree { get; }
    public WelcomeViewModel Welcome { get; } = new();
    public TestRunnerViewModel Runner { get; } = new();
    public ResultsViewerViewModel Results { get; } = new();
    public RecordingViewModel Recording { get; } = new();
    // Phase 14.1 — inline side panel for single-click-on-test. Reused for the
    // lifetime of the TestsViewModel; Load() rebinds it to whichever test was
    // selected. Suite and Workload details remain Welcome-placeholder until
    // their dedicated panels land in a Phase 14.1b follow-up.
    public TestDetailsViewModel TestDetails { get; } = new();

    [ObservableProperty]
    private ObservableObject _activeContent;

    [ObservableProperty]
    private ModeOverride _modeOverride = ModeOverride.None;

    // Run-scoped toggle. Lives on the singleton TestsViewModel, so it persists
    // across nav and does NOT reset like the per-test editor's keepOpenOnFailure.
    // When true, the target app (e.g. Rhino) is left running after the run — pass
    // OR fail — so the operator can inspect the actual canvas/viewport. Tear it
    // down with the Stop button.
    [ObservableProperty]
    private bool _keepAppOpen;

    // View-supplied delegates for the Phase 5 context-menu commands so
    // the VM stays testable without opening real dialog windows.
    public Func<TestDefinition, Task>? EditTestAsync { get; set; }
    public Func<SuiteDefinition, Task>? EditSuiteAsync { get; set; }
    public Func<WorkloadConfig, Task>? EditWorkloadAsync { get; set; }
    public Func<string, Task<string?>>? PromptForTestNameAsync { get; set; }
    public Func<string, Task<bool>>? ConfirmAsync { get; set; }

    public TestsViewModel()
    {
        Tree = new WorkloadTreeViewModel();
        _activeContent = Welcome;
        Runner.OnRunCompletedAsync = OnRunCompletedAsync;
        Recording.RecordingSaved += async () =>
        {
            if (Tree.WorkloadsDir != null)
            {
                await Tree.LoadAsync(Tree.WorkloadsDir).ConfigureAwait(true);
            }
        };

        // Phase 14.1 — bind details panel callbacks back to this VM so the
        // header-row buttons reuse the same Run / Approve / OpenInExplorer
        // implementations as the context-menu commands. Persistence goes
        // through SaveTestDefinitionToDiskAsync, mirroring the modal flow.
        TestDetails.RunAsync = async td =>
        {
            // Run a single-test request (the same code path RunSelectionAsync
            // uses for a Test node, factored to avoid pulling SelectedNode
            // out from under the user mid-click).
            await RunSingleTestAsync(td).ConfigureAwait(true);
        };
        TestDetails.Approve = td =>
        {
            if (Tree.WorkloadsDir == null) return;
            var workload = Tree.SelectedNode?.OwningWorkload;
            if (workload == null) return;
            try { BaselineManager.ApproveTest(Tree.WorkloadsDir, workload.Config.Name, td.Name); }
            catch { /* surfaced via next run */ }
        };
        TestDetails.OpenInExplorer = td =>
        {
            var workload = Tree.SelectedNode?.OwningWorkload;
            if (workload == null) return;
            var path = Path.Combine(workload.Directory, "tests", $"{td.Name}.json");
            try
            {
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });
            }
            catch { /* shell open fails silently */ }
        };
        TestDetails.SaveJsonToDiskAsync = async json =>
        {
            var workload = Tree.SelectedNode?.OwningWorkload;
            if (workload == null || Tree.WorkloadsDir == null) return;
            // Editor.BuildDefinition was already called inside the editor;
            // it raised SaveRequested with the serialized JSON. Persist + reload.
            var def = JsonSerializer.Deserialize<TestDefinition>(json);
            if (def == null) return;
            var path = Path.Combine(workload.Directory, "tests", $"{def.Name}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            await Tree.LoadAsync(Tree.WorkloadsDir).ConfigureAwait(true);
        };

        // Selection-change observer — when the operator single-clicks a node,
        // dispatch the right details view into ActiveContent. Test → inline
        // editor. Suite / Workload / Group nodes stay on Welcome (their
        // dedicated panels land in a future micro-checkpoint).
        Tree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(WorkloadTreeViewModel.SelectedNode)) return;
            RouteSelectionToActiveContent();
            // Phase 14.1 also fixes right-click: the [RelayCommand] auto-generated
            // commands here didn't notify when SelectedNode changed (no
            // CanExecute was wired), so the context menu items were always-on
            // but operated on a stale / null selection. Now we explicitly
            // refresh execution state every selection change.
            RunSelectionCommand.NotifyCanExecuteChanged();
            EditSelectionCommand.NotifyCanExecuteChanged();
            ApproveSelectionCommand.NotifyCanExecuteChanged();
            OpenInExplorerCommand.NotifyCanExecuteChanged();
            CreateTestFromRecordingCommand.NotifyCanExecuteChanged();
        };
    }

    private void RouteSelectionToActiveContent()
    {
        var node = Tree.SelectedNode;
        if (node == null)
        {
            ActiveContent = Welcome;
            return;
        }
        // Don't yank the user out of the Runner mid-run or off a fresh results
        // viewer. Selection still updates Tree.SelectedNode, but the visible
        // panel stays put. Operator selects another test → switches.
        if (ActiveContent == Runner) return;

        if (node.Kind == WorkloadNodeKind.Test
            && node.Payload is TestDefinition td
            && node.OwningWorkload != null
            && Tree.WorkloadsDir != null)
        {
            TestDetails.Load(td, node.OwningWorkload, Tree.WorkloadsDir);
            ActiveContent = TestDetails;
            return;
        }
        // Suite / Workload / *Group nodes — return to Welcome for now. The
        // dedicated SuiteDetails / WorkloadDetails panels are deferred to a
        // Phase 14.1b follow-up; users can still right-click → Edit them.
        ActiveContent = Welcome;
    }

    private async Task RunSingleTestAsync(TestDefinition td)
    {
        var workload = Tree.SelectedNode?.OwningWorkload;
        if (workload == null || Tree.WorkloadsDir == null) return;
        ActiveContent = Runner;
        Runner.ModeOverride = ModeOverride;
        var request = new RunRequest
        {
            Workload = workload.Config,
            Tests = new[] { td },
            WorkloadsDir = Tree.WorkloadsDir,
            SuiteName = null,
            UseSharedMode = false,
            SuiteKeepOpen = false,
            ForceKeepOpen = KeepAppOpen,
        };
        await Runner.RunCommand.ExecuteAsync(request).ConfigureAwait(true);
    }

    public async Task LoadWorkloadsAsync(string workloadsDir)
    {
        await Tree.LoadAsync(workloadsDir).ConfigureAwait(true);
        Recording.SetWorkloadsDir(workloadsDir);
        Recording.SetWorkloads(Tree.LoadedWorkloads.Select(w => w.Config));
    }

    public bool CanRunSelection => Tree.SelectedNode is { } node
        && (node.Kind == WorkloadNodeKind.Test || node.Kind == WorkloadNodeKind.Suite);

    [RelayCommand]
    public async Task RunSelectionAsync()
    {
        var node = Tree.SelectedNode;
        if (node?.OwningWorkload == null || Tree.WorkloadsDir == null) return;

        IReadOnlyList<TestDefinition> tests;
        string? suiteName = null;
        bool useSharedMode = false;
        bool suiteKeepOpen = false;

        if (node.Kind == WorkloadNodeKind.Test && node.Payload is TestDefinition test)
        {
            tests = new[] { test };
        }
        else if (node.Kind == WorkloadNodeKind.Suite && node.Payload is SuiteDefinition suite)
        {
            var suiteTests = node.OwningWorkload.Tests
                .Where(t => suite.Tests.Contains(t.Name))
                .ToList();
            tests = suiteTests;
            suiteName = suite.Name;
            useSharedMode = suiteTests.Count > 0 && suiteTests.All(t => t.RunMode == "shared");
            suiteKeepOpen = suite.KeepOpen;
        }
        else return;

        ActiveContent = Runner;
        Runner.ModeOverride = ModeOverride;
        var request = new RunRequest
        {
            Workload = node.OwningWorkload.Config,
            Tests = tests,
            WorkloadsDir = Tree.WorkloadsDir,
            SuiteName = suiteName,
            UseSharedMode = useSharedMode,
            SuiteKeepOpen = suiteKeepOpen,
            ForceKeepOpen = KeepAppOpen,
        };
        await Runner.RunCommand.ExecuteAsync(request).ConfigureAwait(true);
    }

    [RelayCommand]
    public void ShowRecording() => ActiveContent = Recording;

    [RelayCommand]
    public void ShowWelcome() => ActiveContent = Welcome;

    [RelayCommand]
    public async Task EditSelectionAsync()
    {
        var node = Tree.SelectedNode;
        if (node == null) return;
        if (node.Kind == WorkloadNodeKind.Test && node.Payload is TestDefinition td && EditTestAsync != null)
            await EditTestAsync(td).ConfigureAwait(true);
        else if (node.Kind == WorkloadNodeKind.Suite && node.Payload is SuiteDefinition sd && EditSuiteAsync != null)
            await EditSuiteAsync(sd).ConfigureAwait(true);
        else if (node.Kind == WorkloadNodeKind.Workload && node.OwningWorkload != null && EditWorkloadAsync != null)
            await EditWorkloadAsync(node.OwningWorkload.Config).ConfigureAwait(true);
    }

    [RelayCommand]
    public void ApproveSelection()
    {
        var node = Tree.SelectedNode;
        if (node?.OwningWorkload == null || Tree.WorkloadsDir == null) return;
        try
        {
            if (node.Kind == WorkloadNodeKind.Test && node.Payload is TestDefinition td)
            {
                BaselineManager.ApproveTest(Tree.WorkloadsDir, node.OwningWorkload.Config.Name, td.Name);
            }
            else if (node.Kind == WorkloadNodeKind.Suite && node.Payload is SuiteDefinition sd)
            {
                foreach (var name in sd.Tests)
                {
                    try { BaselineManager.ApproveTest(Tree.WorkloadsDir, node.OwningWorkload.Config.Name, name, sd.Name); }
                    catch { /* missing candidates for some tests — keep going */ }
                }
            }
        }
        catch { /* surfaced via the operator's next run */ }
    }

    [RelayCommand]
    public void OpenInExplorer()
    {
        var node = Tree.SelectedNode;
        string? path = null;
        if (node?.OwningWorkload != null && Tree.WorkloadsDir != null)
        {
            path = node.Kind switch
            {
                WorkloadNodeKind.Workload => node.OwningWorkload.Directory,
                WorkloadNodeKind.Test when node.Payload is TestDefinition td => Path.Combine(node.OwningWorkload.Directory, "tests", $"{td.Name}.json"),
                WorkloadNodeKind.Suite when node.Payload is SuiteDefinition sd => Path.Combine(node.OwningWorkload.Directory, "suites", $"{sd.Name}.json"),
                WorkloadNodeKind.Recording when node.Payload is string p => p,
                _ => node.OwningWorkload.Directory,
            };
        }
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
        }
        catch { /* shell open fails silently */ }
    }

    [RelayCommand]
    public async Task CreateTestFromRecordingAsync()
    {
        var node = Tree.SelectedNode;
        if (node?.OwningWorkload == null || node.Payload is not string recordingPath) return;
        if (Tree.WorkloadsDir == null || PromptForTestNameAsync == null) return;

        var suggested = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(recordingPath));
        var name = await PromptForTestNameAsync(suggested).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;

        await CreateTestFromRecordingFileAsync(name, recordingPath, node.OwningWorkload).ConfigureAwait(true);
    }

    public async Task<string?> CreateTestFromRecordingFileAsync(string testName, string recordingPath, Services.WorkloadExplorer.WorkloadEntry workload)
    {
        if (Tree.WorkloadsDir == null) return null;
        var testsDir = Path.Combine(workload.Directory, "tests");
        Directory.CreateDirectory(testsDir);
        var def = new TestDefinition
        {
            Name = testName,
            Workload = workload.Config.Name,
            Description = $"Created from recording {Path.GetFileName(recordingPath)}",
            Recording = Path.GetFileName(recordingPath),
        };
        var path = Path.Combine(testsDir, $"{testName}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true }));
        await Tree.LoadAsync(Tree.WorkloadsDir).ConfigureAwait(true);
        return path;
    }

    private async Task OnRunCompletedAsync(SuiteResult suite)
    {
        var node = Tree.SelectedNode;
        if (node?.OwningWorkload == null || Tree.WorkloadsDir == null) return;

        Results.SetContext(Tree.WorkloadsDir, node.OwningWorkload.Config.Name,
            node.Kind == WorkloadNodeKind.Suite && node.Payload is SuiteDefinition s ? s.Name : null);

        if (suite.TestResults.Count == 1)
        {
            Results.LoadResult(suite.TestResults[0]);
        }
        else
        {
            Results.LoadSuiteResult(suite, node.Label);
        }
        ActiveContent = Results;
        await Task.CompletedTask;
    }
}
