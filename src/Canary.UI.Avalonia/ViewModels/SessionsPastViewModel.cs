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
