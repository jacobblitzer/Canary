using System.Collections.ObjectModel;
using Avalonia.Threading;
using Canary.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Canary.UI.Avalonia.ViewModels;

public sealed class TelemetryRow
{
    public required string Time { get; init; }
    public required string Kind { get; init; }
    public required string Source { get; init; }
    public required string Level { get; init; }
    public required string Data { get; init; }
    public string LevelColor { get; init; } = "#DCDCDC";
}

public partial class TelemetryViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;
    private string? _workloadsDir;
    private string? _currentFile;
    private DateTime _lastSeenWriteUtc;
    private const int MaxLines = 500;

    public ObservableCollection<TelemetryRow> Rows { get; } = new();
    public IReadOnlyList<string> SourceFilters { get; } = new[] { "(all)", "penumbra", "qualia", "rhino", "canary-harness" };

    [ObservableProperty]
    private string _selectedSource = "(all)";

    [ObservableProperty]
    private string _tailingPath = "(no telemetry.ndjson found under workloads/*/results/)";

    public TelemetryViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshTelemetry();
    }

    partial void OnSelectedSourceChanged(string value) => RefreshTelemetry();

    public void SetWorkloadsDir(string? workloadsDir)
    {
        _workloadsDir = workloadsDir;
        _currentFile = null;
        _lastSeenWriteUtc = DateTime.MinValue;
        RefreshTelemetry();
    }

    public void StartPolling()
    {
        RefreshTelemetry();
        _timer.Start();
    }

    public void StopPolling() => _timer.Stop();

    public void RefreshTelemetry()
    {
        var path = FindMostRecentTelemetryFile();
        if (path == null)
        {
            TailingPath = "(no telemetry.ndjson found under workloads/*/results/)";
            return;
        }

        TailingPath = path;
        var writeUtc = File.GetLastWriteTimeUtc(path);
        // Always re-filter when the source changes; only short-circuit when nothing changed.
        if (path == _currentFile && writeUtc == _lastSeenWriteUtc && Rows.Count > 0) return;

        _currentFile = path;
        _lastSeenWriteUtc = writeUtc;

        List<string> lines;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var all = new List<string>();
            string? line;
            while ((line = sr.ReadLine()) != null) all.Add(line);
            lines = all.TakeLast(MaxLines).ToList();
        }
        catch (Exception ex)
        {
            TailingPath = $"(read failed: {ex.Message})";
            return;
        }

        var filterSource = SelectedSource == "(all)" ? null : SelectedSource;

        Rows.Clear();
        foreach (var raw in lines)
        {
            var record = TelemetryRecord.FromJson(raw);
            if (record == null) continue;
            if (filterSource != null && !string.Equals(record.Source, filterSource, StringComparison.OrdinalIgnoreCase)) continue;

            Rows.Add(new TelemetryRow
            {
                Time = record.T.ToString("HH:mm:ss.fff"),
                Kind = record.Kind.ToString(),
                Source = record.Source ?? "—",
                Level = record.Level ?? "—",
                Data = SummarizeData(record),
                LevelColor = LevelHex(record.Level),
            });
        }
    }

    private string? FindMostRecentTelemetryFile()
    {
        if (string.IsNullOrEmpty(_workloadsDir) || !Directory.Exists(_workloadsDir)) return null;
        DateTime newestUtc = DateTime.MinValue;
        string? newestPath = null;
        foreach (var f in Directory.EnumerateFiles(_workloadsDir, "telemetry.ndjson", SearchOption.AllDirectories))
        {
            var t = File.GetLastWriteTimeUtc(f);
            if (t > newestUtc) { newestUtc = t; newestPath = f; }
        }
        return newestPath;
    }

    private static string SummarizeData(TelemetryRecord record)
    {
        try
        {
            if (record.Data == null) return string.Empty;
            return System.Text.Json.JsonSerializer.Serialize(record.Data, TelemetryRecord.SerializerOptions);
        }
        catch { return "(unserializable)"; }
    }

    private static string LevelHex(string? level) => level?.ToLowerInvariant() switch
    {
        "error" => "#FF7864",
        "warn" => "#FFDC50",
        "debug" => "#969696",
        _ => "#DCDCDC",
    };

    public void Dispose() => _timer.Stop();
}
