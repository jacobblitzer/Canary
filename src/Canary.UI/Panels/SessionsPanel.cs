using Canary.Config;

namespace Canary.UI.Panels;

public sealed class SessionsPanel : UserControl
{
    private readonly TabControl _tabs;
    private readonly SessionsLiveSubPanel _live;
    private readonly SessionsPastSubPanel _past;

    public SessionsPanel()
    {
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Appearance = TabAppearance.FlatButtons,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(120, 28),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Padding = new Point(10, 4),
        };

        _live = new SessionsLiveSubPanel { Dock = DockStyle.Fill };
        _past = new SessionsPastSubPanel { Dock = DockStyle.Fill };

        var liveTab = new TabPage("Live") { BackColor = Color.FromArgb(30, 30, 30) };
        liveTab.Controls.Add(_live);
        var pastTab = new TabPage("Past sessions") { BackColor = Color.FromArgb(30, 30, 30) };
        pastTab.Controls.Add(_past);
        _tabs.TabPages.Add(liveTab);
        _tabs.TabPages.Add(pastTab);
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab == pastTab) _past.Reload();
        };

        Controls.Add(_tabs);
    }

    public void SetWorkloads(string? workloadsDir, IEnumerable<WorkloadConfig> workloads)
    {
        _live.SetWorkloads(workloadsDir, workloads);
        _past.SetWorkloadsDir(workloadsDir);
    }

    public bool ProcessHotkeyMessage(ref Message m) => _live.ProcessHotkeyMessage(ref m);
}
