using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Canary.Config;
using Canary.UI.Avalonia.Services;
using Canary.UI.Avalonia.ViewModels.Editors;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

/// <summary>
/// Phase 14.1 — the inline details panel that opens when an operator
/// single-clicks a Test node in the workload tree. Wraps the existing
/// <see cref="TestEditorViewModel"/> so its tabbed editor renders inline
/// (no modal <c>EditorHostWindow</c>) and adds header-row action buttons
/// (Run / Approve / Open in Explorer) bound back through callbacks to the
/// parent <see cref="TestsViewModel"/>. Persistence reuses the same
/// <see cref="TestEditorViewModel.SaveRequested"/> event the modal flow uses;
/// the parent re-loads the tree after the JSON write.
/// </summary>
public sealed partial class TestDetailsViewModel : ObservableObject
{
    public TestEditorViewModel Editor { get; } = new();
    /// <summary>Phase 14.3 — past runs tab.</summary>
    public PastRunsViewModel PastRuns { get; } = new();

    [ObservableProperty]
    private string _headerName = string.Empty;

    [ObservableProperty]
    private string _headerSubtitle = string.Empty;

    [ObservableProperty]
    private string? _statusText;

    private TestDefinition? _definition;
    private WorkloadExplorer.WorkloadEntry? _workload;
    private string? _workloadsDir;

    // Callbacks supplied by the parent TestsViewModel so action buttons in
    // the details panel route back into the same Run / Approve / OpenInExplorer
    // implementations the right-click context menu uses. Keeps the details
    // panel testable without depending on the full TestsViewModel surface.
    public Func<TestDefinition, Task>? RunAsync { get; set; }
    public Action<TestDefinition>? Approve { get; set; }
    public Action<TestDefinition>? OpenInExplorer { get; set; }
    public Func<string, Task>? SaveJsonToDiskAsync { get; set; }

    public TestDetailsViewModel()
    {
        Editor.SaveRequested += async json =>
        {
            if (SaveJsonToDiskAsync == null) return;
            try
            {
                await SaveJsonToDiskAsync(json).ConfigureAwait(true);
                StatusText = "Saved.";
            }
            catch (Exception ex)
            {
                StatusText = "Save failed: " + ex.Message;
            }
        };
    }

    public void Load(TestDefinition definition, WorkloadExplorer.WorkloadEntry workload, string workloadsDir)
    {
        _definition = definition;
        _workload = workload;
        _workloadsDir = workloadsDir;
        Editor.Load(definition);
        HeaderName = definition.Name;
        HeaderSubtitle = $"{workload.Config.DisplayName}  ·  {definition.Workload}";
        StatusText = null;
        // Phase 14.3 — bind the past-runs tab to this test's results directory.
        // Fire-and-forget the async scan; UI shows "(no past runs found)" until
        // the scan completes.
        _ = PastRuns.SetContextAsync(workloadsDir, workload.Config.Name, definition.Name);
    }

    [RelayCommand]
    public async Task RunSelectedAsync()
    {
        if (_definition != null && RunAsync != null)
            await RunAsync(_definition).ConfigureAwait(true);
    }

    [RelayCommand]
    public void ApproveSelected()
    {
        if (_definition != null && Approve != null) Approve(_definition);
    }

    [RelayCommand]
    public void OpenSelectedInExplorer()
    {
        if (_definition != null && OpenInExplorer != null) OpenInExplorer(_definition);
    }
}
