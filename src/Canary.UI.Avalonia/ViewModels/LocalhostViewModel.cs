using System.Collections.ObjectModel;
using Avalonia.Threading;
using Canary.Localhost;
using Canary.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

public sealed class LocalhostRow
{
    public required string Port { get; init; }
    public required string Pid { get; init; }
    public required string ProcessName { get; init; }
    public required string Provenance { get; init; }
    public required string StartedDisplay { get; init; }
    public required string Path { get; init; }
    public int? RawPort { get; init; }   // null for heuristic rows (no bound port)
    public PortProvenance ProvenanceValue { get; init; }
}

public partial class LocalhostViewModel : ObservableObject, IDisposable
{
    private readonly LocalhostManager _manager = new();
    private readonly DispatcherTimer _timer;
    private CanarySettings _settings;

    public ObservableCollection<LocalhostRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(KillSelectedCommand))]
    private LocalhostRow? _selectedRow;

    [ObservableProperty]
    private bool _showTier3;

    [ObservableProperty]
    private string _statusText = "Loading…";

    public Func<string, Task<bool>>? ConfirmKillAsync { get; set; }

    public LocalhostViewModel()
    {
        _settings = CanarySettings.Load();
        _showTier3 = _settings.ShowTier3Processes;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public void StartPolling() => _timer.Start();
    public void StopPolling() => _timer.Stop();

    public void SetFastPolling() => _timer.Interval = TimeSpan.FromSeconds(2);
    public void SetSlowPolling() => _timer.Interval = TimeSpan.FromSeconds(30);

    partial void OnShowTier3Changed(bool value)
    {
        _settings.ShowTier3Processes = value;
        try { _settings.Save(); } catch { }
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            var entries = await _manager.EnumeratePortsAsync(LocalhostManager.DefaultPorts).ConfigureAwait(true);
            Rows.Clear();
            foreach (var e in entries)
            {
                Rows.Add(new LocalhostRow
                {
                    Port = e.Port.ToString(),
                    Pid = e.Pid?.ToString() ?? "—",
                    ProcessName = e.ProcessName ?? "—",
                    Provenance = e.Provenance.ToString(),
                    StartedDisplay = e.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—",
                    Path = e.CommandLine ?? "—",
                    RawPort = e.Port,
                    ProvenanceValue = e.Provenance,
                });
            }

            int heuristicCount = 0;
            if (ShowTier3)
            {
                var seenPids = entries.Where(x => x.Pid.HasValue).Select(x => x.Pid!.Value).ToHashSet();
                foreach (var h in HeuristicProcessLister.Enumerate())
                {
                    if (seenPids.Contains(h.Pid)) continue;
                    Rows.Add(new LocalhostRow
                    {
                        Port = "—",
                        Pid = h.Pid.ToString(),
                        ProcessName = h.Name,
                        Provenance = PortProvenance.DevServerHeuristic.ToString(),
                        StartedDisplay = h.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—",
                        Path = h.MainWindowTitle ?? "(heuristic — may be false positive)",
                        RawPort = null,
                        ProvenanceValue = PortProvenance.DevServerHeuristic,
                    });
                    heuristicCount++;
                }
            }

            var heuristicSuffix = ShowTier3 ? $" + {heuristicCount} heuristic" : string.Empty;
            StatusText = $"{entries.Count} listening{heuristicSuffix} · refreshed {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText = $"Refresh failed: {ex.Message}";
        }
    }

    private bool CanKillSelected() => SelectedRow?.RawPort != null;

    [RelayCommand(CanExecute = nameof(CanKillSelected))]
    private async Task KillSelectedAsync()
    {
        var row = SelectedRow;
        if (row?.RawPort == null) return;

        var prompt = row.ProvenanceValue == PortProvenance.CanaryHarness
            ? $"This is Canary's OWN listener (PID {row.Pid}). Kill?"
            : $"Kill PID {row.Pid} ({row.ProcessName}) holding port {row.Port}?";

        if (ConfirmKillAsync != null)
        {
            var ok = await ConfirmKillAsync(prompt).ConfigureAwait(true);
            if (!ok) return;
        }

        var killed = await _manager.KillByPortAsync(row.RawPort.Value).ConfigureAwait(true);
        StatusText = killed
            ? $"Killed PID {row.Pid} on port {row.Port}."
            : $"Kill on port {row.Port} did not succeed.";
        await RefreshAsync().ConfigureAwait(true);
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
