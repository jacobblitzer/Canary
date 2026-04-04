using System.Text.Json;
using Canary.Config;

namespace Canary.UI.Controls;

/// <summary>
/// Editor for creating and editing workload configurations.
/// </summary>
internal sealed class WorkloadEditorControl : UserControl
{
    private readonly TextBox _nameBox;
    private readonly TextBox _displayNameBox;
    private readonly TextBox _appPathBox;
    private readonly TextBox _appArgsBox;
    private readonly ComboBox _agentTypeCombo;
    private readonly TextBox _pipeNameBox;
    private readonly NumericUpDown _timeoutBox;
    private readonly TextBox _windowTitleBox;
    private readonly ErrorProvider _errorProvider;

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

        // Name
        layout.Controls.Add(CreateLabel("Name:"), 0, row);
        _nameBox = CreateTextBox();
        layout.Controls.Add(_nameBox, 1, row++);

        // Display Name
        layout.Controls.Add(CreateLabel("Display Name:"), 0, row);
        _displayNameBox = CreateTextBox();
        layout.Controls.Add(_displayNameBox, 1, row++);

        // App Path
        layout.Controls.Add(CreateLabel("Executable:"), 0, row);
        var appPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _appPathBox = CreateTextBox();
        _appPathBox.Width = 300;
        var browseBtn = new Button
        {
            Text = "...",
            Size = new Size(30, 23),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        browseBtn.Click += OnBrowseApp;
        appPanel.Controls.Add(_appPathBox);
        appPanel.Controls.Add(browseBtn);
        layout.Controls.Add(appPanel, 1, row++);

        // App Args
        layout.Controls.Add(CreateLabel("Arguments:"), 0, row);
        _appArgsBox = CreateTextBox();
        layout.Controls.Add(_appArgsBox, 1, row++);

        // Agent Type
        layout.Controls.Add(CreateLabel("Agent Type:"), 0, row);
        _agentTypeCombo = new ComboBox
        {
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        _agentTypeCombo.Items.AddRange(new object[] { "rhino", "wpf", "electron", "custom" });
        _agentTypeCombo.SelectedIndex = 0;
        layout.Controls.Add(_agentTypeCombo, 1, row++);

        // Pipe Name
        layout.Controls.Add(CreateLabel("Pipe Name:"), 0, row);
        _pipeNameBox = CreateTextBox();
        layout.Controls.Add(_pipeNameBox, 1, row++);

        // Timeout
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

        // Window Title
        layout.Controls.Add(CreateLabel("Window Title:"), 0, row);
        _windowTitleBox = CreateTextBox();
        layout.Controls.Add(_windowTitleBox, 1, row++);

        // Save button
        var saveButton = new Button
        {
            Text = "Save Workload Config",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            Size = new Size(180, 35),
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

    private void OnSave(object? sender, EventArgs e)
    {
        _errorProvider.Clear();

        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            _errorProvider.SetError(_nameBox, "Name is required");
            return;
        }

        var config = BuildConfig();
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        SaveRequested?.Invoke(json);
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
}
