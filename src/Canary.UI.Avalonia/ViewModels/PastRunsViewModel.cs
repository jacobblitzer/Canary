using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Canary.UI.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

/// <summary>
/// Phase 14.3 — backs the Past Runs tab inside <c>TestDetailsView</c>. Lists
/// every <c>runs/&lt;ts&gt;/result.json</c> for the currently-selected test
/// (newest first). Selecting a row populates the embedded
/// <see cref="ResultsViewerViewModel"/> with that run's checkpoints +
/// baseline/candidate/diff/GIF artifacts. Approve / Reject from the viewer
/// writes the baseline to disk via <c>BaselineManager.ApproveCheckpoint</c>
/// using the timestamp directory as the suite name (the BaselineManager
/// already supports past-run paths via that mechanism — Phase 14.3 is the
/// missing UI entry point).
/// </summary>
public sealed partial class PastRunsViewModel : ObservableObject
{
    public ObservableCollection<PastRunsScanner.PastRunRow> Rows { get; } = new();

    public ResultsViewerViewModel Results { get; } = new();

    [ObservableProperty]
    private PastRunsScanner.PastRunRow? _selectedRun;

    [ObservableProperty]
    private string _statusText = "(no test selected)";

    private string? _workloadsDir;
    private string? _workloadName;
    private string? _testName;

    public async Task SetContextAsync(string workloadsDir, string workloadName, string testName)
    {
        _workloadsDir = workloadsDir;
        _workloadName = workloadName;
        _testName = testName;
        Results.SetContext(workloadsDir, workloadName, suiteName: null);
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        if (_workloadsDir == null || _workloadName == null || _testName == null)
        {
            Rows.Clear();
            StatusText = "(no test selected)";
            return;
        }
        var rows = await PastRunsScanner.ScanAsync(_workloadsDir, _workloadName, _testName).ConfigureAwait(true);
        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);
        StatusText = rows.Count == 0
            ? "No past runs found. Run the test once to populate."
            : $"{rows.Count} past run(s).";
    }

    partial void OnSelectedRunChanged(PastRunsScanner.PastRunRow? value)
    {
        if (value == null) return;
        // The orchestrator writes baselines under `runs/<ts>/baselines/`
        // when BaselineManager is given suiteName=<timestamp>. Use the
        // timestamp dir name as the suite-name override so Approve targets
        // the right past-run directory rather than the test's top-level
        // baselines folder.
        if (_workloadsDir != null && _workloadName != null)
            Results.SetContext(_workloadsDir, _workloadName, suiteName: value.TimestampDir);
        _ = Results.LoadFromPathAsync(value.ResultJsonPath);
    }
}
