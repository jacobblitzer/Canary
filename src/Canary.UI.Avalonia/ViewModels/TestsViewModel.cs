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

    [ObservableProperty]
    private ObservableObject _activeContent;

    [ObservableProperty]
    private ModeOverride _modeOverride = ModeOverride.None;

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
            // Mirrors MainForm.OnRunTests: all-shared-test-runMode → shared
            // instance for the suite. KeepOpen is the suite's own bool.
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
        };
        await Runner.RunCommand.ExecuteAsync(request).ConfigureAwait(true);
    }

    [RelayCommand]
    public void ShowRecording()
    {
        ActiveContent = Recording;
    }

    [RelayCommand]
    public void ShowWelcome()
    {
        ActiveContent = Welcome;
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
