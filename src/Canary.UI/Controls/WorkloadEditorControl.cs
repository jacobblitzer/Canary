using System.Text.Json;
using Canary.Agent.Penumbra;
using Canary.Config;

namespace Canary.UI.Controls;

/// <summary>
/// Editor for creating and editing workload configurations.
/// Toggles between standard (pipe-based) fields and Penumbra CDP fields
/// based on the selected agent type.
/// </summary>
internal sealed class WorkloadEditorControl : UserControl
{
    // Shared fields
    private readonly TextBox _nameBox;
    private readonly TextBox _displayNameBox;
    private readonly ComboBox _agentTypeCombo;
    private readonly NumericUpDown _timeoutBox;
    private readonly ErrorProvider _errorProvider;

    // Standard (pipe-based) fields
    private readonly Panel _standardFieldsPanel;
    private readonly TextBox _appPathBox;
    private readonly TextBox _appArgsBox;
    private readonly TextBox _pipeNameBox;
    private readonly TextBox _windowTitleBox;

    // Penumbra CDP fields
    private readonly Panel _penumbraFieldsPanel;
    private readonly TextBox _projectDirBox;
    private readonly NumericUpDown _vitePortBox;
    private readonly NumericUpDown _cdpPortBox;
    private readonly ComboBox _backendCombo;
    private readonly NumericUpDown _canvasWidthBox;
    private readonly NumericUpDown _canvasHeightBox;
    private readonly NumericUpDown _stabilizeMsBox;
    private readonly Button _probeButton;
    private readonly Label _probeStatusLabel;

    public event Action<string>? SaveRequested;

    public WorkloadEditorControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);
        AutoScroll = true;

        _errorProvider = new ErrorProvider { BlinkStyle = ErrorBlinkStyle.NeverBlink };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(15)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // ── Shared fields (always visible) ──────────────────────────────

        // Name
        layout.Controls.Add(CreateLabel("Name:"), 0, row);
        _nameBox = CreateTextBox();
        layout.Controls.Add(_nameBox, 1, row++);

        // Display Name
        layout.Controls.Add(CreateLabel("Display Name:"), 0, row);
        _displayNameBox = CreateTextBox();
        layout.Controls.Add(_displayNameBox, 1, row++);

        // Agent Type
        layout.Controls.Add(CreateLabel("Agent Type:"), 0, row);
        _agentTypeCombo = new ComboBox
        {
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        _agentTypeCombo.Items.AddRange(new object[] { "rhino", "penumbra-cdp", "wpf", "electron", "custom" });
        _agentTypeCombo.SelectedIndex = 0;
        _agentTypeCombo.SelectedIndexChanged += OnAgentTypeChanged;
        layout.Controls.Add(_agentTypeCombo, 1, row++);

        // Startup Timeout
        layout.Controls.Add(CreateLabel("Startup Timeout:"), 0, row);
        var timeoutPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _timeoutBox = new NumericUpDown
        {
            Value = 30000,
            Minimum = 1000,
            Maximum = 300000,
            Increment = 1000,
            Width = 100,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        timeoutPanel.Controls.Add(_timeoutBox);
        timeoutPanel.Controls.Add(new Label { Text = "ms", ForeColor = Color.FromArgb(140, 140, 140), AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
        layout.Controls.Add(timeoutPanel, 1, row++);

        // ── Standard (pipe-based) fields panel ──────────────────────────

        _standardFieldsPanel = new Panel { AutoSize = true, Dock = DockStyle.Fill };
        var stdLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true
        };
        stdLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        stdLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int sRow = 0;

        // Executable
        stdLayout.Controls.Add(CreateLabel("Executable:"), 0, sRow);
        var appPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _appPathBox = CreateTextBox();
        _appPathBox.Width = 300;
        var browseBtn = new Button
        {
            Text = "...",
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        browseBtn.Click += OnBrowseApp;
        appPanel.Controls.Add(_appPathBox);
        appPanel.Controls.Add(browseBtn);
        stdLayout.Controls.Add(appPanel, 1, sRow++);

        // Arguments
        stdLayout.Controls.Add(CreateLabel("Arguments:"), 0, sRow);
        _appArgsBox = CreateTextBox();
        stdLayout.Controls.Add(_appArgsBox, 1, sRow++);

        // Pipe Name
        stdLayout.Controls.Add(CreateLabel("Pipe Name:"), 0, sRow);
        _pipeNameBox = CreateTextBox();
        stdLayout.Controls.Add(_pipeNameBox, 1, sRow++);

        // Window Title
        stdLayout.Controls.Add(CreateLabel("Window Title:"), 0, sRow);
        _windowTitleBox = CreateTextBox();
        stdLayout.Controls.Add(_windowTitleBox, 1, sRow++);

        _standardFieldsPanel.Controls.Add(stdLayout);
        layout.Controls.Add(_standardFieldsPanel, 0, row);
        layout.SetColumnSpan(_standardFieldsPanel, 2);
        row++;

        // ── Penumbra CDP fields panel ───────────────────────────────────

        _penumbraFieldsPanel = new Panel { AutoSize = true, Dock = DockStyle.Fill, Visible = false };
        var penLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true
        };
        penLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        penLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int pRow = 0;

        // Project Dir
        penLayout.Controls.Add(CreateLabel("Project Dir:"), 0, pRow);
        var dirPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _projectDirBox = CreateTextBox();
        _projectDirBox.Width = 300;
        var browseDirBtn = new Button
        {
            Text = "...",
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        browseDirBtn.Click += OnBrowseProjectDir;
        dirPanel.Controls.Add(_projectDirBox);
        dirPanel.Controls.Add(browseDirBtn);
        penLayout.Controls.Add(dirPanel, 1, pRow++);

        // Vite Port
        penLayout.Controls.Add(CreateLabel("Vite Port:"), 0, pRow);
        _vitePortBox = CreateNumericUpDown(3000, 1, 65535);
        penLayout.Controls.Add(_vitePortBox, 1, pRow++);

        // CDP Port
        penLayout.Controls.Add(CreateLabel("CDP Port:"), 0, pRow);
        _cdpPortBox = CreateNumericUpDown(9222, 1, 65535);
        penLayout.Controls.Add(_cdpPortBox, 1, pRow++);

        // Backend
        penLayout.Controls.Add(CreateLabel("Backend:"), 0, pRow);
        _backendCombo = new ComboBox
        {
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        _backendCombo.Items.AddRange(new object[] { "webgpu", "webgl2" });
        _backendCombo.SelectedIndex = 0;
        penLayout.Controls.Add(_backendCombo, 1, pRow++);

        // Canvas Size
        penLayout.Controls.Add(CreateLabel("Canvas:"), 0, pRow);
        var canvasPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _canvasWidthBox = CreateNumericUpDown(960, 64, 7680);
        _canvasHeightBox = CreateNumericUpDown(540, 64, 4320);
        canvasPanel.Controls.Add(_canvasWidthBox);
        canvasPanel.Controls.Add(new Label { Text = "x", ForeColor = Color.FromArgb(140, 140, 140), AutoSize = true, Padding = new Padding(2, 4, 2, 0) });
        canvasPanel.Controls.Add(_canvasHeightBox);
        penLayout.Controls.Add(canvasPanel, 1, pRow++);

        // Stabilize Ms
        penLayout.Controls.Add(CreateLabel("Stabilize Ms:"), 0, pRow);
        _stabilizeMsBox = CreateNumericUpDown(500, 0, 10000);
        penLayout.Controls.Add(_stabilizeMsBox, 1, pRow++);

        // Detect Instance
        penLayout.Controls.Add(CreateLabel("Detect Instance:"), 0, pRow);
        var probePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _probeButton = new Button
        {
            Text = "Probe",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 2, 8, 2)
        };
        _probeButton.Click += OnProbeInstance;
        _probeStatusLabel = new Label
        {
            Text = "Not checked",
            ForeColor = Color.FromArgb(140, 140, 140),
            AutoSize = true,
            Padding = new Padding(4, 4, 0, 0)
        };
        probePanel.Controls.Add(_probeButton);
        probePanel.Controls.Add(_probeStatusLabel);
        penLayout.Controls.Add(probePanel, 1, pRow++);

        _penumbraFieldsPanel.Controls.Add(penLayout);
        layout.Controls.Add(_penumbraFieldsPanel, 0, row);
        layout.SetColumnSpan(_penumbraFieldsPanel, 2);
        row++;

        // ── Save button ─────────────────────────────────────────────────

        var saveButton = new Button
        {
            Text = "Save Workload Config",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 4, 12, 4),
            Font = new Font("Segoe UI", 10f)
        };
        saveButton.Click += OnSave;
        layout.Controls.Add(saveButton, 1, row);

        Controls.Add(layout);
    }

    public void LoadConfig(WorkloadConfig config)
    {
        _nameBox.Text = config.Name;
        _displayNameBox.Text = config.DisplayName;
        _appPathBox.Text = config.AppPath;
        _appArgsBox.Text = config.AppArgs;
        _pipeNameBox.Text = config.PipeName;
        _timeoutBox.Value = Math.Clamp(config.StartupTimeoutMs, 1000, 300000);
        _windowTitleBox.Text = config.WindowTitle;

        for (int i = 0; i < _agentTypeCombo.Items.Count; i++)
        {
            if (string.Equals(_agentTypeCombo.Items[i]?.ToString(), config.AgentType, StringComparison.OrdinalIgnoreCase))
            {
                _agentTypeCombo.SelectedIndex = i;
                break;
            }
        }
    }

    /// <summary>
    /// Load Penumbra-specific config fields from a workload.json file.
    /// </summary>
    public void LoadPenumbraConfig(string workloadJsonPath)
    {
        try
        {
            var json = File.ReadAllText(workloadJsonPath);
            var config = JsonSerializer.Deserialize<PenumbraWorkloadConfig>(json);
            if (config?.PenumbraConfig == null) return;

            var pc = config.PenumbraConfig;
            _projectDirBox.Text = pc.ProjectDir;
            _vitePortBox.Value = Math.Clamp(pc.VitePort, 1, 65535);
            _cdpPortBox.Value = Math.Clamp(pc.CdpPort, 1, 65535);
            _canvasWidthBox.Value = Math.Clamp(pc.DefaultCanvasWidth, 64, 7680);
            _canvasHeightBox.Value = Math.Clamp(pc.DefaultCanvasHeight, 64, 4320);
            _stabilizeMsBox.Value = Math.Clamp(pc.DefaultStabilizeMs, 0, 10000);

            for (int i = 0; i < _backendCombo.Items.Count; i++)
            {
                if (string.Equals(_backendCombo.Items[i]?.ToString(), pc.DefaultBackend, StringComparison.OrdinalIgnoreCase))
                {
                    _backendCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        catch
        {
            // If the file doesn't have a penumbraConfig section, that's fine
        }
    }

    public WorkloadConfig BuildConfig()
    {
        return new WorkloadConfig
        {
            Name = _nameBox.Text.Trim(),
            DisplayName = _displayNameBox.Text.Trim(),
            AppPath = _appPathBox.Text.Trim(),
            AppArgs = _appArgsBox.Text.Trim(),
            AgentType = _agentTypeCombo.SelectedItem?.ToString() ?? "custom",
            PipeName = _pipeNameBox.Text.Trim(),
            StartupTimeoutMs = (int)_timeoutBox.Value,
            WindowTitle = _windowTitleBox.Text.Trim()
        };
    }

    private void OnAgentTypeChanged(object? sender, EventArgs e)
    {
        var isPenumbra = _agentTypeCombo.SelectedItem?.ToString() == "penumbra-cdp";
        _standardFieldsPanel.Visible = !isPenumbra;
        _penumbraFieldsPanel.Visible = isPenumbra;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        _errorProvider.Clear();

        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            _errorProvider.SetError(_nameBox, "Name is required");
            return;
        }

        var agentType = _agentTypeCombo.SelectedItem?.ToString() ?? "custom";

        string json;
        if (agentType == "penumbra-cdp")
        {
            var penConfig = new PenumbraWorkloadConfig
            {
                Name = _nameBox.Text.Trim(),
                DisplayName = _displayNameBox.Text.Trim(),
                AgentType = agentType,
                StartupTimeoutMs = (int)_timeoutBox.Value,
                PenumbraConfig = new PenumbraConfig
                {
                    ProjectDir = _projectDirBox.Text.Trim(),
                    VitePort = (int)_vitePortBox.Value,
                    CdpPort = (int)_cdpPortBox.Value,
                    DefaultBackend = _backendCombo.SelectedItem?.ToString() ?? "webgpu",
                    DefaultCanvasWidth = (int)_canvasWidthBox.Value,
                    DefaultCanvasHeight = (int)_canvasHeightBox.Value,
                    DefaultStabilizeMs = (int)_stabilizeMsBox.Value
                }
            };
            json = JsonSerializer.Serialize(penConfig, new JsonSerializerOptions { WriteIndented = true });
        }
        else
        {
            var config = BuildConfig();
            json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        }

        SaveRequested?.Invoke(json);
    }

    private async void OnProbeInstance(object? sender, EventArgs e)
    {
        _probeButton.Enabled = false;
        _probeStatusLabel.Text = "Probing...";
        _probeStatusLabel.ForeColor = Color.FromArgb(140, 140, 140);

        try
        {
            var vitePort = (int)_vitePortBox.Value;
            var cdpPort = (int)_cdpPortBox.Value;

            var result = await PenumbraInstanceProbe.ProbeAsync(vitePort, cdpPort).ConfigureAwait(true);

            if (result.PenumbraReady)
            {
                _probeStatusLabel.Text = $"Ready (backend={result.RendererBackend})";
                _probeStatusLabel.ForeColor = Color.FromArgb(80, 200, 80);
            }
            else if (result.ViteRunning || result.CdpAvailable)
            {
                var parts = new List<string>();
                if (result.ViteRunning) parts.Add("Vite");
                if (result.CdpAvailable) parts.Add("CDP");
                _probeStatusLabel.Text = $"Partial: {string.Join(" + ", parts)} (Penumbra not ready)";
                _probeStatusLabel.ForeColor = Color.FromArgb(220, 180, 50);
            }
            else
            {
                _probeStatusLabel.Text = "No instance detected";
                _probeStatusLabel.ForeColor = Color.FromArgb(200, 80, 80);
            }
        }
        catch (Exception ex)
        {
            _probeStatusLabel.Text = $"Error: {ex.Message}";
            _probeStatusLabel.ForeColor = Color.FromArgb(200, 80, 80);
        }
        finally
        {
            _probeButton.Enabled = true;
        }
    }

    private void OnBrowseApp(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Executables|*.exe|All Files|*.*"
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            _appPathBox.Text = dialog.FileName;
    }

    private void OnBrowseProjectDir(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Penumbra project directory",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            _projectDirBox.Text = dialog.SelectedPath;
    }

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        ForeColor = Color.FromArgb(180, 180, 180),
        Font = new Font("Segoe UI", 9.5f),
        AutoSize = true,
        Padding = new Padding(0, 6, 0, 0)
    };

    private static TextBox CreateTextBox() => new()
    {
        BackColor = Color.FromArgb(50, 50, 50),
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Width = 350
    };

    private static NumericUpDown CreateNumericUpDown(int defaultValue, int min, int max) => new()
    {
        Value = defaultValue,
        Minimum = min,
        Maximum = max,
        Width = 100,
        BackColor = Color.FromArgb(50, 50, 50),
        ForeColor = Color.White
    };
}
