using Canary.Orchestration;

namespace Canary.UI.Panels;

// Phase 7 / design §C8 + §C2 — past-results browser. SplitContainer
// with the run list on the left (workload + test + verdict + when) and
// REPORT.md preview on the right. Reads per-run dirs produced by
// Phase 3 (runs/<timestamp>/REPORT.md + result.json).
public sealed class PastRunsPanel : UserControl
{
    private readonly SplitContainer _split;
    private readonly ListView _runList;
    private readonly TextBox _preview;
    private readonly ToolStripStatusLabel _status;
    private readonly TextBox _filterBox;

    private string? _workloadsDir;
    private List<RunRow> _allRuns = new();

    public PastRunsPanel()
    {
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);

        // Top filter strip
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

        // Body split
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
        _runList.Columns.Add("When (UTC)", 150);
        _runList.Columns.Add("Workload", 90);
        _runList.Columns.Add("Test", 200);
        _runList.Columns.Add("Verdict", 80);
        _runList.SelectedIndexChanged += (_, _) => LoadSelectedReport();

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

        // Status strip
        var statusStrip = new StatusStrip { BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, SizingGrip = false };
        _status = new ToolStripStatusLabel("Past Runs — no workloads dir yet") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusStrip.Items.Add(_status);

        Controls.Add(_split);
        Controls.Add(statusStrip);
        Controls.Add(top);
    }

    // Called by the host when the workloads dir becomes known (after
    // AutoDetectWorkloadsDir in MainForm). Re-scans + repopulates.
    public void SetWorkloadsDir(string? workloadsDir)
    {
        _workloadsDir = workloadsDir;
        Reload();
    }

    private void Reload()
    {
        _allRuns.Clear();
        _runList.Items.Clear();
        if (string.IsNullOrEmpty(_workloadsDir) || !Directory.Exists(_workloadsDir))
        {
            _status.Text = "No workloads dir loaded.";
            return;
        }

        foreach (var workloadDir in Directory.EnumerateDirectories(_workloadsDir))
        {
            var workload = Path.GetFileName(workloadDir);
            var results = Path.Combine(workloadDir, "results");
            if (!Directory.Exists(results)) continue;

            foreach (var report in Directory.EnumerateFiles(results, "REPORT.md", SearchOption.AllDirectories))
            {
                var runDir = Path.GetDirectoryName(report)!;
                var runId = Path.GetFileName(runDir);
                var testDir = Path.GetDirectoryName(Path.GetDirectoryName(runDir))!;
                var testName = Path.GetFileName(testDir);
                _allRuns.Add(new RunRow
                {
                    WhenUtc = File.GetLastWriteTimeUtc(report),
                    Workload = workload,
                    TestName = testName,
                    Verdict = ParseVerdict(report),
                    RunId = runId,
                    ReportPath = report,
                });
            }
        }

        _allRuns = _allRuns.OrderByDescending(r => r.WhenUtc).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var needle = _filterBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(needle)
            ? _allRuns
            : _allRuns.Where(r =>
                  r.Workload.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                  r.TestName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                  r.Verdict.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                  r.RunId.Contains(needle, StringComparison.OrdinalIgnoreCase))
              .ToList();

        _runList.BeginUpdate();
        _runList.Items.Clear();
        foreach (var r in filtered)
        {
            var item = new ListViewItem(r.WhenUtc.ToString("yyyy-MM-dd HH:mm:ss"));
            item.SubItems.Add(r.Workload);
            item.SubItems.Add(r.TestName);
            item.SubItems.Add(r.Verdict);
            item.Tag = r;
            item.ForeColor = VerdictColor(r.Verdict);
            _runList.Items.Add(item);
        }
        _runList.EndUpdate();
        _status.Text = $"{filtered.Count} run(s) shown · {_allRuns.Count} total · workloadsDir: {_workloadsDir}";
    }

    private void LoadSelectedReport()
    {
        if (_runList.SelectedItems.Count == 0) { _preview.Text = string.Empty; return; }
        if (_runList.SelectedItems[0].Tag is not RunRow row) return;
        try { _preview.Text = File.ReadAllText(row.ReportPath); }
        catch (Exception ex) { _preview.Text = $"(failed to read REPORT.md: {ex.Message})"; }
    }

    private static Color VerdictColor(string verdict) => verdict.ToUpperInvariant() switch
    {
        "PASS" or "PASSED" => Color.FromArgb(150, 220, 130),
        "FAIL" or "FAILED" => Color.FromArgb(255, 120, 100),
        "CRASH" or "CRASHED" => Color.FromArgb(255, 80, 200),
        "NEW" => Color.FromArgb(255, 220, 80),
        _ => Color.FromArgb(200, 200, 200),
    };

    // First line of REPORT.md: "# Canary run — <test> — <VERDICT>"
    internal static string ParseVerdict(string reportPath)
    {
        try
        {
            using var sr = new StreamReader(reportPath);
            var line = sr.ReadLine() ?? string.Empty;
            var lastDash = line.LastIndexOf('—');
            return lastDash < 0 ? "?" : line[(lastDash + 1)..].Trim();
        }
        catch { return "?"; }
    }

    internal sealed class RunRow
    {
        public DateTime WhenUtc { get; init; }
        public string Workload { get; init; } = string.Empty;
        public string TestName { get; init; } = string.Empty;
        public string Verdict { get; init; } = string.Empty;
        public string RunId { get; init; } = string.Empty;
        public string ReportPath { get; init; } = string.Empty;
    }
}
