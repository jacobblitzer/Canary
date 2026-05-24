using Canary.Telemetry;

namespace Canary.UI.Panels;

// Phase 7 / design §C4 + §C8 — Telemetry nav tab. Tails the latest
// per-suite telemetry.ndjson written by Phase 2's NdjsonFileSink and
// renders the records color-coded by Kind. Polls the file's
// LastWriteTimeUtc every 2s; on change, re-reads the last N lines.
//
// Simplification vs the design §C8 description: this panel reads
// from disk rather than subscribing to the live EventStreamSink. The
// EventStreamSink fan-out hook is wired in Phase 2 but the
// cross-process bridge for a running CLI run is not (would require
// IPC). Disk-tail covers the operator's primary use case (review the
// last run) and updates within seconds of new records being written.
public sealed class TelemetryPanel : UserControl
{
    private readonly ListView _list;
    private readonly Label _pathLabel;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ComboBox _sourceFilter;

    private string? _workloadsDir;
    private string? _currentFile;
    private DateTime _lastSeenWriteUtc;
    private const int MaxLines = 500;

    public TelemetryPanel()
    {
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(45, 45, 48),
            ColumnCount = 4,
            Padding = new Padding(6, 4, 6, 4),
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var label = new Label { Text = "Tailing:", AutoSize = true, ForeColor = Color.FromArgb(220, 220, 220), Padding = new Padding(0, 6, 6, 0) };
        _pathLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(150, 200, 255), AutoEllipsis = true, Padding = new Padding(0, 6, 0, 0) };
        var filterLabel = new Label { Text = "Source:", AutoSize = true, ForeColor = Color.FromArgb(220, 220, 220), Padding = new Padding(8, 6, 6, 0) };
        _sourceFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.FromArgb(220, 220, 220),
            Width = 120,
        };
        _sourceFilter.Items.AddRange(new object[] { "(all)", "penumbra", "qualia", "rhino", "canary-harness" });
        _sourceFilter.SelectedIndex = 0;
        _sourceFilter.SelectedIndexChanged += (_, _) => RefreshTelemetry();
        toolbar.Controls.Add(label, 0, 0);
        toolbar.Controls.Add(_pathLabel, 1, 0);
        toolbar.Controls.Add(filterLabel, 2, 0);
        toolbar.Controls.Add(_sourceFilter, 3, 0);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9.0f),
        };
        _list.Columns.Add("Time", 110);
        _list.Columns.Add("Kind", 90);
        _list.Columns.Add("Source", 100);
        _list.Columns.Add("Level", 60);
        _list.Columns.Add("Data", 800);

        Controls.Add(_list);
        Controls.Add(toolbar);

        _timer = new System.Windows.Forms.Timer { Interval = 2_000 };
        _timer.Tick += (_, _) => RefreshTelemetry();
        VisibleChanged += (_, _) =>
        {
            if (Visible) { RefreshTelemetry(); _timer.Start(); }
            else _timer.Stop();
        };
    }

    public void SetWorkloadsDir(string? workloadsDir)
    {
        _workloadsDir = workloadsDir;
        RefreshTelemetry();
    }

    private void RefreshTelemetry()
    {
        var path = FindMostRecentTelemetryFile();
        if (path == null)
        {
            _pathLabel.Text = "(no telemetry.ndjson found under workloads/*/results/)";
            return;
        }

        _pathLabel.Text = path;
        var writeUtc = File.GetLastWriteTimeUtc(path);
        if (path == _currentFile && writeUtc == _lastSeenWriteUtc) return;

        _currentFile = path;
        _lastSeenWriteUtc = writeUtc;

        IEnumerable<string> lines;
        try
        {
            // FileShare.ReadWrite so we don't block the writing CLI.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var all = new List<string>();
            string? line;
            while ((line = sr.ReadLine()) != null) all.Add(line);
            lines = all.TakeLast(MaxLines).ToList();
        }
        catch (Exception ex)
        {
            _pathLabel.Text = $"(read failed: {ex.Message})";
            return;
        }

        var filterSource = _sourceFilter.SelectedItem?.ToString();
        if (filterSource == "(all)") filterSource = null;

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var raw in lines)
        {
            var record = TelemetryRecord.FromJson(raw);
            if (record == null) continue;
            if (filterSource != null && !string.Equals(record.Source, filterSource, StringComparison.OrdinalIgnoreCase)) continue;

            var item = new ListViewItem(record.T.ToString("HH:mm:ss.fff"));
            item.SubItems.Add(record.Kind.ToString());
            item.SubItems.Add(record.Source ?? "—");
            item.SubItems.Add(record.Level ?? "—");
            item.SubItems.Add(SummarizeData(record));
            item.ForeColor = LevelColor(record.Level);
            _list.Items.Add(item);
        }
        if (_list.Items.Count > 0) _list.EnsureVisible(_list.Items.Count - 1);
        _list.EndUpdate();
    }

    private string? FindMostRecentTelemetryFile()
    {
        if (string.IsNullOrEmpty(_workloadsDir) || !Directory.Exists(_workloadsDir)) return null;

        var newest = (DateTime: DateTime.MinValue, Path: (string?)null);
        foreach (var f in Directory.EnumerateFiles(_workloadsDir, "telemetry.ndjson", SearchOption.AllDirectories))
        {
            var t = File.GetLastWriteTimeUtc(f);
            if (t > newest.DateTime) newest = (t, f);
        }
        return newest.Path;
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

    private static Color LevelColor(string? level) => level?.ToLowerInvariant() switch
    {
        "error" => Color.FromArgb(255, 120, 100),
        "warn" => Color.FromArgb(255, 220, 80),
        "debug" => Color.FromArgb(150, 150, 150),
        _ => Color.FromArgb(220, 220, 220),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
