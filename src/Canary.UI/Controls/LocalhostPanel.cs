using Canary.Localhost;
using Canary.Settings;

namespace Canary.UI.Controls;

// Phase 4 / §C7 Tier 1 — interim drop-in panel surfacing the
// LocalhostManager enumeration. Phase 7 migrates this into a proper
// nav tab; this version is a self-contained UserControl that can be
// hosted in a popup form OR slotted into a TabPage with no further
// changes.
//
// Polling cadence: 2s when foregrounded, 30s when backgrounded —
// timer interval flipped via the Activated / Deactivate events on the
// containing form (a runtime concern; Phase 7 wires it properly).
public sealed class LocalhostPanel : UserControl
{
    private readonly LocalhostManager _manager = new();
    private readonly ListView _list;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Button _refreshBtn;
    private readonly Button _killBtn;
    private readonly CheckBox _tier3Toggle;
    private readonly ToolStripStatusLabel _status;
    private bool _showTier3;

    public LocalhostPanel()
    {
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(45, 45, 48),
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(6, 4, 6, 4),
        };
        _refreshBtn = new Button { Text = "Refresh", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
        _killBtn = new Button { Text = "Kill selected", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(120, 50, 50), ForeColor = Color.White, Enabled = false };
        _refreshBtn.Click += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        _killBtn.Click += async (_, _) => await KillSelectedAsync().ConfigureAwait(true);
        toolbar.Controls.Add(_refreshBtn);
        toolbar.Controls.Add(_killBtn);

        // Phase 8 / §C7 Tier 3 — opt-in toggle. Reads initial state from
        // CanarySettings (operator persists via the Settings tab); also
        // togglable inline for quick preview without leaving the panel.
        var settings = CanarySettings.Load();
        _showTier3 = settings.ShowTier3Processes;
        _tier3Toggle = new CheckBox { Text = "Show all dev-server-likely processes (Tier 3 — may include false positives)", AutoSize = true, ForeColor = Color.FromArgb(220, 220, 220), Checked = _showTier3, Padding = new Padding(8, 2, 0, 0) };
        _tier3Toggle.CheckedChanged += async (_, _) => { _showTier3 = _tier3Toggle.Checked; await RefreshAsync().ConfigureAwait(true); };
        toolbar.Controls.Add(_tier3Toggle);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None,
        };
        _list.Columns.Add("Port", 70);
        _list.Columns.Add("PID", 80);
        _list.Columns.Add("Process", 200);
        _list.Columns.Add("Provenance", 140);
        _list.Columns.Add("Started (UTC)", 180);
        _list.Columns.Add("Path", 400);
        _list.SelectedIndexChanged += (_, _) => _killBtn.Enabled = _list.SelectedItems.Count > 0;

        var statusStrip = new StatusStrip { BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, SizingGrip = false };
        _status = new ToolStripStatusLabel("Loading…") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusStrip.Items.Add(_status);

        Controls.Add(_list);
        Controls.Add(statusStrip);
        Controls.Add(toolbar);

        _timer = new System.Windows.Forms.Timer { Interval = 2_000 };
        _timer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(true);

        VisibleChanged += (_, _) =>
        {
            if (Visible) _timer.Start();
            else _timer.Stop();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _ = RefreshAsync();
        _timer.Start();
    }

    // Switch to the slow cadence when the host form deactivates and back to
    // 2s when it activates. Phase 7's nav-tab refactor will own this more
    // properly; this interim wiring is opt-in by the host calling SetFast /
    // SetSlow at the right times.
    public void SetFastPolling() { _timer.Interval = 2_000; }
    public void SetSlowPolling() { _timer.Interval = 30_000; }

    private async Task RefreshAsync()
    {
        try
        {
            var entries = await _manager.EnumeratePortsAsync(LocalhostManager.DefaultPorts).ConfigureAwait(true);
            _list.BeginUpdate();
            _list.Items.Clear();

            foreach (var e in entries)
            {
                var item = new ListViewItem(e.Port.ToString());
                item.SubItems.Add(e.Pid?.ToString() ?? "—");
                item.SubItems.Add(e.ProcessName ?? "—");
                item.SubItems.Add(e.Provenance.ToString());
                item.SubItems.Add(e.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—");
                item.SubItems.Add(e.CommandLine ?? "—");
                item.Tag = e;
                _list.Items.Add(item);
            }

            int tier3Count = 0;
            if (_showTier3)
            {
                // Phase 8 / §C7 Tier 3 — append heuristic processes that
                // aren't already in the Tier 1/2 enumeration. Loud caveat
                // via the "DevServerHeuristic" provenance label.
                var seenPids = entries.Where(e => e.Pid.HasValue).Select(e => e.Pid!.Value).ToHashSet();
                foreach (var h in HeuristicProcessLister.Enumerate())
                {
                    if (seenPids.Contains(h.Pid)) continue;
                    var item = new ListViewItem("—");  // no port (heuristic — port unknown)
                    item.SubItems.Add(h.Pid.ToString());
                    item.SubItems.Add(h.Name);
                    item.SubItems.Add(PortProvenance.DevServerHeuristic.ToString());
                    item.SubItems.Add(h.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—");
                    item.SubItems.Add(h.MainWindowTitle ?? "(heuristic — may be false positive)");
                    item.ForeColor = Color.FromArgb(180, 180, 180);
                    _list.Items.Add(item);
                    tier3Count++;
                }
            }

            _list.EndUpdate();
            var tier3Suffix = _showTier3 ? $" + {tier3Count} heuristic" : "";
            _status.Text = $"{entries.Count} listening{tier3Suffix} · refreshed {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Refresh failed: {ex.Message}";
        }
    }

    private async Task KillSelectedAsync()
    {
        if (_list.SelectedItems.Count == 0) return;
        if (_list.SelectedItems[0].Tag is not PortEntry e) return;

        var msg = e.Provenance == PortProvenance.CanaryHarness
            ? $"This is Canary's OWN listener (PID {e.Pid}). Kill?"
            : $"Kill PID {e.Pid} ({e.ProcessName ?? "unknown"}) holding port {e.Port}?";
        if (MessageBox.Show(this, msg, "Confirm kill", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        var ok = await _manager.KillByPortAsync(e.Port).ConfigureAwait(true);
        _status.Text = ok ? $"Killed PID {e.Pid} on port {e.Port}." : $"Kill on port {e.Port} did not succeed.";
        await RefreshAsync().ConfigureAwait(true);
    }

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
