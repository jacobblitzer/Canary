using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Canary.UI.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

/// <summary>
/// Feedback 2026-06-10-run-history-log-window — the docked Run History pane
/// at the bottom of the main window (operator chose "docked companion pane"
/// over a nav tab, 2026-06-11). A chronological, all-workloads log of past
/// runs that stays visible while working in any tab. Collapsible to its
/// header strip; refreshes on workload-dir load and after every in-UI run.
/// Double-click a row to open the run's REPORT.md (falls back to the run dir).
/// </summary>
public partial class RunHistoryViewModel : ObservableObject
{
    public ObservableCollection<RunHistoryScanner.RunHistoryRow> Rows { get; } = new();

    [ObservableProperty]
    private RunHistoryScanner.RunHistoryRow? _selectedRow;

    [ObservableProperty]
    private string _statusText = "(no workloads loaded)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private bool _isExpanded = true;

    public string HeaderText => IsExpanded ? "▾ Run History" : "▸ Run History";

    private string? _workloadsDir;

    public void SetWorkloadsDir(string workloadsDir)
    {
        _workloadsDir = workloadsDir;
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_workloadsDir == null) return;
        // ConfigureAwait(true): Rows is UI-bound; mutate on the UI thread only
        // (same rule as WorkloadTreeViewModel.LoadAsync — Avalonia does not
        // marshal collection-change notifications).
        var rows = await RunHistoryScanner.ScanAsync(_workloadsDir).ConfigureAwait(true);
        Rows.Clear();
        foreach (var row in rows) Rows.Add(row);
        StatusText = rows.Count == 0 ? "no runs recorded yet" : $"{rows.Count} runs";
    }

    [RelayCommand]
    public void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>Double-click → REPORT.md in the default editor; run dir in Explorer when the report is missing.</summary>
    [RelayCommand]
    public void OpenSelected()
    {
        var row = SelectedRow;
        if (row == null) return;
        try
        {
            if (File.Exists(row.ReportPath))
                Process.Start(new ProcessStartInfo { FileName = row.ReportPath, UseShellExecute = true });
            else if (Directory.Exists(row.RunDirectory))
                Process.Start(new ProcessStartInfo { FileName = row.RunDirectory, UseShellExecute = true });
        }
        catch { /* shell open fails silently */ }
    }

    [RelayCommand]
    public void OpenSelectedInExplorer()
    {
        var row = SelectedRow;
        if (row == null || !Directory.Exists(row.RunDirectory)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = row.RunDirectory, UseShellExecute = true });
        }
        catch { /* shell open fails silently */ }
    }
}
