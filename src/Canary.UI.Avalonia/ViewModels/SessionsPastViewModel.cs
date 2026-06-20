using System.Collections.ObjectModel;
using Canary.Session;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

public sealed class SessionRow
{
    public required string Workload { get; init; }
    public required string SessionId { get; init; }
    public required DateTime StartedUtc { get; init; }
    public required int Captures { get; init; }
    public required string SessionDir { get; init; }
    public string StartedDisplay => StartedUtc.ToString("yyyy-MM-dd HH:mm:ss");
}

public partial class SessionsPastViewModel : ObservableObject
{
    private string? _workloadsDir;
    private List<SessionRow> _allRows = new();

    public ObservableCollection<SessionRow> Rows { get; } = new();

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private SessionRow? _selectedRow;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private string _statusText = "Past sessions — no workloads dir yet";

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedRowChanged(SessionRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedSessionId));
        OnPropertyChanged(nameof(SelectedSessionDir));
        if (value == null) { Preview = string.Empty; return; }
        var reportPath = SessionPaths.ReportPath(value.SessionDir);
        try
        {
            Preview = File.Exists(reportPath)
                ? File.ReadAllText(reportPath)
                : "(SESSION_REPORT.md missing)";
        }
        catch (Exception ex)
        {
            Preview = $"(failed to read report: {ex.Message})";
        }
    }

    /// <summary>Operator UX (2026-06-19): expose the selected row's id + dir as bindable strings, with
    /// Copy commands the View can wire to clipboard. Solves "I can't copy/paste the address" — the
    /// DataGrid cell didn't expose selectable text, so the path was visible but ungrabbable.</summary>
    public bool HasSelection => SelectedRow != null;
    public string SelectedSessionId => SelectedRow?.SessionId ?? string.Empty;
    public string SelectedSessionDir => SelectedRow?.SessionDir ?? string.Empty;

    [RelayCommand]
    private async Task CopySessionId()
    {
        if (SelectedRow == null) return;
        await SetClipboardAsync(SelectedRow.SessionId);
        StatusText = $"Copied session id: {SelectedRow.SessionId}";
    }

    [RelayCommand]
    private async Task CopySessionDir()
    {
        if (SelectedRow == null) return;
        await SetClipboardAsync(SelectedRow.SessionDir);
        StatusText = $"Copied session dir: {SelectedRow.SessionDir}";
    }

    private static async Task SetClipboardAsync(string text)
    {
        try
        {
            // Namespace `Canary.UI.Avalonia` would shadow `Avalonia.*` — use global:: to escape.
            var topLevel = global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? global::Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow)
                : null;
            if (topLevel?.Clipboard != null) await topLevel.Clipboard.SetTextAsync(text);
        }
        catch { /* best-effort */ }
    }

    public void SetWorkloadsDir(string? workloadsDir)
    {
        _workloadsDir = workloadsDir;
        Reload();
    }

    [RelayCommand]
    public void Reload()
    {
        _allRows = ScanRows(_workloadsDir).OrderByDescending(r => r.StartedUtc).ToList();
        ApplyFilter();
    }

    internal static List<SessionRow> ScanRows(string? workloadsDir)
    {
        var rows = new List<SessionRow>();
        if (string.IsNullOrEmpty(workloadsDir) || !Directory.Exists(workloadsDir)) return rows;

        foreach (var wDir in Directory.EnumerateDirectories(workloadsDir))
        {
            var workload = Path.GetFileName(wDir);
            var sessionsDir = Path.Combine(wDir, SessionPaths.SessionsSubdir);
            if (!Directory.Exists(sessionsDir)) continue;

            foreach (var sDir in Directory.EnumerateDirectories(sessionsDir))
            {
                var data = SessionReportWriter.TryReadJson(sDir);
                if (data == null) continue;
                rows.Add(new SessionRow
                {
                    Workload = workload,
                    SessionId = data.SessionId,
                    StartedUtc = data.StartedAtUtc,
                    Captures = data.Captures.Count,
                    SessionDir = sDir,
                });
            }
        }
        return rows;
    }

    private void ApplyFilter()
    {
        var needle = Filter.Trim();
        var filtered = string.IsNullOrEmpty(needle)
            ? _allRows.ToList()
            : _allRows.Where(r =>
                r.Workload.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                r.SessionId.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();

        Rows.Clear();
        foreach (var r in filtered) Rows.Add(r);
        StatusText = $"{filtered.Count} session(s) shown · {_allRows.Count} total · workloadsDir: {_workloadsDir ?? "(none)"}";
    }
}
