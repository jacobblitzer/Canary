namespace Canary.UI.Panels;

// Phase 7 / design §C9 — Settings nav tab. Shows the UI-mode toggle
// (Stabilization / Maturation per §C9) plus placeholders for the
// retention slider and Tier 3 toggle (the latter actually wires in
// Phase 8). Persistence is intentionally NOT wired in Phase 7 — the
// radio state lives in memory only; Phase 8 hooks it up to a per-user
// settings store.
public sealed class SettingsPanel : UserControl
{
    public enum UiMode { Stabilization, Maturation }
    public UiMode CurrentMode { get; private set; } = UiMode.Stabilization;

    private readonly RadioButton _stabilizationRadio;
    private readonly RadioButton _maturationRadio;
    private readonly Label _statusLabel;

    public SettingsPanel()
    {
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);

        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20),
            BackColor = Color.FromArgb(30, 30, 30),
        };
        for (int i = 0; i < 5; i++) container.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Canary — Settings",
            AutoSize = true,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 220, 220),
        };

        var modeBox = new GroupBox
        {
            Text = "UI mode (design §C9)",
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Padding = new Padding(10),
            Margin = new Padding(0, 12, 0, 0),
            BackColor = Color.FromArgb(37, 37, 38),
        };
        var modeLayout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            BackColor = Color.FromArgb(37, 37, 38),
        };
        _stabilizationRadio = new RadioButton
        {
            Text = "Stabilization — debugging-focused; VLM and pixel-diff under Past Runs",
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Checked = true,
        };
        _maturationRadio = new RadioButton
        {
            Text = "Maturation — regression-focused; VLM and pixel-diff in main nav",
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
        };
        _stabilizationRadio.CheckedChanged += (_, _) => { if (_stabilizationRadio.Checked) { CurrentMode = UiMode.Stabilization; UpdateStatus(); } };
        _maturationRadio.CheckedChanged += (_, _) => { if (_maturationRadio.Checked) { CurrentMode = UiMode.Maturation; UpdateStatus(); } };
        modeLayout.Controls.Add(_stabilizationRadio);
        modeLayout.Controls.Add(_maturationRadio);
        modeBox.Controls.Add(modeLayout);

        var deferralBox = new GroupBox
        {
            Text = "Coming in Phase 8 (polish)",
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Padding = new Padding(10),
            Margin = new Padding(0, 12, 0, 0),
            BackColor = Color.FromArgb(37, 37, 38),
        };
        var deferLayout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            BackColor = Color.FromArgb(37, 37, 38),
        };
        deferLayout.Controls.Add(new CheckBox { Text = "Tier 3 heuristic: show all dev-server-likely processes", AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Enabled = false });
        deferLayout.Controls.Add(new Label { Text = "Retention: 14 days (configurable in Phase 8)", AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Padding = new Padding(0, 8, 0, 0) });
        deferLayout.Controls.Add(new Label { Text = "Settings persistence: not wired in Phase 7 — radio state is in-memory only.", AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Padding = new Padding(0, 4, 0, 0) });
        deferralBox.Controls.Add(deferLayout);

        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 220, 130),
            Padding = new Padding(0, 12, 0, 0),
        };
        UpdateStatus();

        container.Controls.Add(title, 0, 0);
        container.Controls.Add(modeBox, 0, 1);
        container.Controls.Add(deferralBox, 0, 2);
        container.Controls.Add(_statusLabel, 0, 3);

        Controls.Add(container);
    }

    private void UpdateStatus()
    {
        _statusLabel.Text = $"UI mode: {CurrentMode} (in-memory; not persisted)";
    }
}
