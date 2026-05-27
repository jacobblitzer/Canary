using Canary.Session;

namespace Canary.UI.Panels;

public sealed class SessionsPastSubPanel : UserControl
{
    private readonly SplitContainer _split;
    private readonly ListView _runList;
    private readonly TextBox _preview;
    private readonly ToolStripStatusLabel _status;
    private readonly TextBox _filterBox;

    private string? _workloadsDir;
    private List<SessionRow> _allRows = new();

    public SessionsPastSubPanel()
    {
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(45, 45, 48),
            ColumnCount = 3,
            Padding = new Padding(6, 4, 6, 4),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var label = new Label { Text = "Filter:", AutoSize = true, ForeColor = Color.FromArgb(220, 220, 220), Padding = new Padding(0, 6, 6, 0) };
        _filterBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.FixedSingle,
        };
        _filterBox.TextChanged += (_, _) => ApplyFilter();
        var refreshBtn = new Button { Text = "Refresh", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
        refreshBtn.Click += (_, _) => Reload();
        top.Controls.Add(label, 0, 0);
        top.Controls.Add(_filterBox, 1, 0);
        top.Controls.Add(refreshBtn, 2, 0);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 460,
            BackColor = Color.FromArgb(45, 45, 48),
            SplitterWidth = 6,
        };

        _runList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None,
        };
        _runList.Columns.Add("Started (UTC)", 150);
        _runList.Columns.Add("Workload", 90);
        _runList.Columns.Add("Session", 180);
        _runList.Columns.Add("Caps", 50);
        _runList.SelectedIndexChanged += (_, _) => LoadSelected();

        _preview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9.5f),
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(200, 200, 200),
            BorderStyle = BorderStyle.None,
        };

        _split.Panel1.Controls.Add(_runList);
        _split.Panel2.Controls.Add(_preview);

        var statusStrip = new StatusStrip { BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, SizingGrip = false };
        _status = new ToolStripStatusLabel("Past sessions — no workloads dir yet") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusStrip.Items.Add(_status);

        Controls.Add(_split);
        Controls.Add(statusStrip);
        Controls.Add(top);
    }

    public void SetWorkloadsDir(string? workloadsDir)
    {
        _workloadsDir = workloadsDir;
        Reload();
    }

    internal void Reload()
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
        var needle = _filterBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(needle)
            ? _allRows.ToList()
            : _allRows.Where(r =>
                  r.Workload.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                  r.SessionId.Contains(needle, StringComparison.OrdinalIgnoreCase))
              .ToList();

        _runList.BeginUpdate();
        _runList.Items.Clear();
        foreach (var r in filtered)
        {
            var item = new ListViewItem(r.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss"));
            item.SubItems.Add(r.Workload);
            item.SubItems.Add(r.SessionId);
            item.SubItems.Add(r.Captures.ToString());
            item.Tag = r;
            _runList.Items.Add(item);
        }
        _runList.EndUpdate();
        _status.Text = $"{filtered.Count} session(s) shown · {_allRows.Count} total · workloadsDir: {_workloadsDir}";
    }

    private void LoadSelected()
    {
        if (_runList.SelectedItems.Count == 0) { _preview.Text = string.Empty; return; }
        if (_runList.SelectedItems[0].Tag is not SessionRow row) return;
        var reportPath = SessionPaths.ReportPath(row.SessionDir);
        try { _preview.Text = File.Exists(reportPath) ? File.ReadAllText(reportPath) : "(SESSION_REPORT.md missing)"; }
        catch (Exception ex) { _preview.Text = $"(failed to read report: {ex.Message})"; }
    }

    internal sealed class SessionRow
    {
        public string Workload { get; init; } = string.Empty;
        public string SessionId { get; init; } = string.Empty;
        public DateTime StartedUtc { get; init; }
        public int Captures { get; init; }
        public string SessionDir { get; init; } = string.Empty;
    }
}
