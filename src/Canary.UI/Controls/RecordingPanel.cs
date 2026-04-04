using System.Text.Json;
using Canary.Input;

namespace Canary.UI.Controls;

/// <summary>
/// Panel for recording mouse/keyboard input for a test.
/// </summary>
internal sealed class RecordingPanel : UserControl
{
    private readonly ComboBox _workloadCombo;
    private readonly TextBox _windowTitleBox;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Label _eventCountLabel;
    private readonly Label _statusLabel;
    private InputRecorder? _recorder;

    public RecordingPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Height = 200,
            Padding = new Padding(15)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // Workload
        layout.Controls.Add(CreateLabel("Workload:"), 0, row);
        _workloadCombo = new ComboBox
        {
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        layout.Controls.Add(_workloadCombo, 1, row++);

        // Window title
        layout.Controls.Add(CreateLabel("Window Title:"), 0, row);
        _windowTitleBox = new TextBox
        {
            Width = 300,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        layout.Controls.Add(_windowTitleBox, 1, row++);

        // Buttons
        var btnPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _startButton = new Button
        {
            Text = "Start Recording",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(180, 50, 50),
            ForeColor = Color.White,
            Size = new Size(130, 30)
        };
        _startButton.Click += OnStart;

        _stopButton = new Button
        {
            Text = "Stop & Save",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Size = new Size(130, 30),
            Enabled = false
        };
        _stopButton.Click += OnStop;

        btnPanel.Controls.Add(_startButton);
        btnPanel.Controls.Add(_stopButton);
        layout.Controls.Add(btnPanel, 1, row++);

        // Status
        _eventCountLabel = new Label
        {
            Text = "Events: 0",
            ForeColor = Color.FromArgb(220, 180, 50),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            AutoSize = true
        };
        layout.Controls.Add(_eventCountLabel, 1, row++);

        _statusLabel = new Label
        {
            Text = "Ready to record.",
            ForeColor = Color.FromArgb(140, 140, 140),
            AutoSize = true
        };
        layout.Controls.Add(_statusLabel, 1, row);

        Controls.Add(layout);
    }

    public void SetWorkloads(IEnumerable<string> workloadNames)
    {
        _workloadCombo.Items.Clear();
        foreach (var name in workloadNames)
            _workloadCombo.Items.Add(name);

        if (_workloadCombo.Items.Count > 0)
            _workloadCombo.SelectedIndex = 0;
    }

    private void OnStart(object? sender, EventArgs e)
    {
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _statusLabel.Text = "Recording...";

        // Find the target window by title
        var hwnd = ViewportLocator.FindWindowByTitle(_windowTitleBox.Text);
        if (!ViewportLocator.IsValidTarget(hwnd))
        {
            _statusLabel.Text = "Error: Could not find target window.";
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            return;
        }

        var workloadName = _workloadCombo.SelectedItem?.ToString() ?? "unknown";
        _recorder = new InputRecorder(hwnd, workloadName, _windowTitleBox.Text);
        _recorder.StartRecording();
    }

    private void OnStop(object? sender, EventArgs e)
    {
        if (_recorder == null) return;

        var recording = _recorder.StopRecording();

        _startButton.Enabled = true;
        _stopButton.Enabled = false;
        _statusLabel.Text = $"Stopped. Captured {recording.Events.Count} events.";

        // Save dialog
        using var dialog = new SaveFileDialog
        {
            Filter = "Input Recording|*.input.json",
            FileName = $"{_workloadCombo.SelectedItem ?? "recording"}.input.json"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var json = JsonSerializer.Serialize(recording, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);
            _statusLabel.Text = $"Saved {recording.Events.Count} events to {dialog.FileName}";
        }

        _recorder = null;
    }

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        ForeColor = Color.FromArgb(180, 180, 180),
        Font = new Font("Segoe UI", 9.5f),
        AutoSize = true,
        Padding = new Padding(0, 6, 0, 0)
    };
}
