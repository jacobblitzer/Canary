using Canary.Settings;

namespace Canary.UI.Panels;

// Phase 7 + Phase 8 / design §C9 — Settings nav tab. Stabilization /
// Maturation radio (§C9), Tier 3 toggle (§C7), retention threshold
// (§C2). Phase 8 wires persistence: changes save to
// %LocalAppData%\Canary\settings.json via CanarySettings.Save.
public sealed class SettingsPanel : UserControl
{
    public enum UiMode { Stabilization, Maturation }
    public UiMode CurrentMode { get; private set; } = UiMode.Stabilization;

    private CanarySettings _settings;
    private readonly RadioButton _stabilizationRadio;
    private readonly RadioButton _maturationRadio;
    private readonly CheckBox _tier3Check;
    private readonly NumericUpDown _retentionInput;
    private readonly Label _statusLabel;

    public event Action<CanarySettings>? SettingsChanged;

    public SettingsPanel()
    {
        _settings = CanarySettings.Load();
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
            Checked = !string.Equals(_settings.UiMode, "maturation", StringComparison.OrdinalIgnoreCase),
        };
        _maturationRadio = new RadioButton
        {
            Text = "Maturation — regression-focused; VLM and pixel-diff in main nav (panels NOT in v1 — toggle only)",
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Checked = string.Equals(_settings.UiMode, "maturation", StringComparison.OrdinalIgnoreCase),
        };
        CurrentMode = _maturationRadio.Checked ? UiMode.Maturation : UiMode.Stabilization;
        _stabilizationRadio.CheckedChanged += (_, _) => { if (_stabilizationRadio.Checked) { CurrentMode = UiMode.Stabilization; _settings.UiMode = "stabilization"; PersistAndNotify(); } };
        _maturationRadio.CheckedChanged += (_, _) => { if (_maturationRadio.Checked) { CurrentMode = UiMode.Maturation; _settings.UiMode = "maturation"; PersistAndNotify(); } };
        modeLayout.Controls.Add(_stabilizationRadio);
        modeLayout.Controls.Add(_maturationRadio);
        modeBox.Controls.Add(modeLayout);

        var deferralBox = new GroupBox
        {
            Text = "Localhost + retention (Phase 8)",
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
        _tier3Check = new CheckBox
        {
            Text = "Tier 3 heuristic: show all dev-server-likely processes (node/deno/bun/python/dotnet/cargo/tauri/...)",
            AutoSize = true,
            ForeColor = Color.FromArgb(220, 220, 220),
            Checked = _settings.ShowTier3Processes,
        };
        _tier3Check.CheckedChanged += (_, _) => { _settings.ShowTier3Processes = _tier3Check.Checked; PersistAndNotify(); };

        var retentionRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Padding = new Padding(0, 8, 0, 0), BackColor = Color.FromArgb(37, 37, 38) };
        retentionRow.Controls.Add(new Label { Text = "Per-run dir retention (days):", AutoSize = true, ForeColor = Color.FromArgb(220, 220, 220), Padding = new Padding(0, 4, 6, 0) });
        _retentionInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 365,
            Value = Math.Clamp(_settings.RetentionDays, 1, 365),
            Width = 70,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.FromArgb(220, 220, 220),
        };
        _retentionInput.ValueChanged += (_, _) => { _settings.RetentionDays = (int)_retentionInput.Value; PersistAndNotify(); };
        retentionRow.Controls.Add(_retentionInput);

        deferLayout.Controls.Add(_tier3Check);
        deferLayout.Controls.Add(retentionRow);
        deferLayout.Controls.Add(new Label { Text = $"Settings file: {CanarySettings.SettingsFilePath}", AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150), Padding = new Padding(0, 8, 0, 0) });
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

    private void PersistAndNotify()
    {
        try
        {
            _settings.Save();
            _statusLabel.Text = $"Saved · UI mode: {CurrentMode} · Tier 3: {_settings.ShowTier3Processes} · retention: {_settings.RetentionDays}d";
            SettingsChanged?.Invoke(_settings);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Save failed: {ex.Message}";
        }
    }

    private void UpdateStatus()
    {
        _statusLabel.Text = $"UI mode: {CurrentMode} · Tier 3: {_settings.ShowTier3Processes} · retention: {_settings.RetentionDays}d";
    }
}
