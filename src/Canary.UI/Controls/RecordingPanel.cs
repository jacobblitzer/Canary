using System.Diagnostics;
using System.Text.Json;
using Canary.Config;
using Canary.Input;
using Canary.Orchestration;

namespace Canary.UI.Controls;

/// <summary>
/// Panel for recording mouse/keyboard input for a test.
/// Launches the target app (same as test runner) then records against it.
/// </summary>
internal sealed class RecordingPanel : UserControl
{
    private readonly ComboBox _workloadCombo;
    private readonly TextBox _testNameBox;
    private readonly Label _windowInfoLabel;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Label _eventCountLabel;
    private readonly Label _statusLabel;
    private readonly RichTextBox _logBox;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private InputRecorder? _recorder;
    private Process? _launchedProcess;
    private AbortOverlayForm? _overlay;
    private int _lastLoggedCount;
    private string? _workloadsDir;
    private readonly Dictionary<string, WorkloadConfig> _workloadConfigs = new();

    /// <summary>Fired after a recording is saved so the tree can refresh.</summary>
    public event Action? RecordingSaved;

    public RecordingPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);

        var topLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(15)
        };
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // Workload
        topLayout.Controls.Add(CreateLabel("Workload:"), 0, row);
        _workloadCombo = new ComboBox
        {
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        topLayout.Controls.Add(_workloadCombo, 1, row++);

        // Test name
        topLayout.Controls.Add(CreateLabel("Test Name:"), 0, row);
        _testNameBox = new TextBox
        {
            Width = 300,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "my-test"
        };
        topLayout.Controls.Add(_testNameBox, 1, row++);

        // Window info (read-only, shows what will be launched)
        topLayout.Controls.Add(CreateLabel("Target App:"), 0, row);
        _windowInfoLabel = new Label
        {
            Text = "Select a workload above",
            ForeColor = Color.FromArgb(140, 140, 140),
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0)
        };
        topLayout.Controls.Add(_windowInfoLabel, 1, row++);

        // Buttons
        var btnPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 4, 0, 4)
        };

        _startButton = new Button
        {
            Text = "Launch App && Record",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(180, 50, 50),
            ForeColor = Color.White,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 4, 10, 4)
        };
        _startButton.Click += OnStart;

        _stopButton = new Button
        {
            Text = "Stop && Save",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 4, 10, 4),
            Enabled = false
        };
        _stopButton.Click += OnStop;

        btnPanel.Controls.Add(_startButton);
        btnPanel.Controls.Add(_stopButton);
        topLayout.Controls.Add(btnPanel, 1, row++);

        // Event count + status
        var statusPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0)
        };

        _eventCountLabel = new Label
        {
            Text = "Events: 0",
            ForeColor = Color.FromArgb(220, 180, 50),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 20, 0)
        };

        _statusLabel = new Label
        {
            Text = "Select a workload, enter a test name, then click Launch App & Record.",
            ForeColor = Color.FromArgb(140, 140, 140),
            AutoSize = true,
            Margin = new Padding(0, 3, 0, 0)
        };

        statusPanel.Controls.Add(_eventCountLabel);
        statusPanel.Controls.Add(_statusLabel);
        topLayout.Controls.Add(statusPanel, 1, row);

        // Event log console
        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(180, 180, 180),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f),
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };

        Controls.Add(_logBox);
        Controls.Add(topLayout);

        // Timer to update event count + log while recording
        _updateTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _updateTimer.Tick += OnUpdateTick;

        // Update window info when workload selection changes
        _workloadCombo.SelectedIndexChanged += (_, _) => UpdateWindowInfo();
    }

    public void SetWorkloads(IEnumerable<WorkloadConfig> configs)
    {
        _workloadCombo.Items.Clear();
        _workloadConfigs.Clear();
        foreach (var config in configs)
        {
            _workloadConfigs[config.Name] = config;
            _workloadCombo.Items.Add(config.Name);
        }

        if (_workloadCombo.Items.Count > 0)
            _workloadCombo.SelectedIndex = 0;
    }

    public void SetWorkloadsDir(string dir)
    {
        _workloadsDir = dir;
    }

    private void UpdateWindowInfo()
    {
        var name = _workloadCombo.SelectedItem?.ToString();
        if (name != null && _workloadConfigs.TryGetValue(name, out var config))
        {
            _windowInfoLabel.Text = $"{config.DisplayName} — {config.AppPath} {config.AppArgs}";
            _windowInfoLabel.ForeColor = Color.FromArgb(180, 180, 180);
        }
    }

    private WorkloadConfig? GetSelectedConfig()
    {
        var name = _workloadCombo.SelectedItem?.ToString();
        if (name != null && _workloadConfigs.TryGetValue(name, out var config))
            return config;
        return null;
    }

    private async void OnStart(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_testNameBox.Text))
        {
            _statusLabel.Text = "Enter a test name first.";
            return;
        }

        var config = GetSelectedConfig();
        if (config == null)
        {
            _statusLabel.Text = "Select a workload first.";
            return;
        }

        _logBox.Clear();
        _lastLoggedCount = 0;
        _startButton.Enabled = false;
        _testNameBox.Enabled = false;
        _workloadCombo.Enabled = false;

        AddLog($"Launching {config.DisplayName}...");
        AddLog($"  Path: {config.AppPath} {config.AppArgs}");
        _statusLabel.Text = $"Launching {config.DisplayName}...";
        _statusLabel.ForeColor = Color.FromArgb(220, 180, 50);

        try
        {
            // Launch the app the same way the test runner does
            _launchedProcess = AppLauncher.Launch(config);
            AddLog($"Process started: PID {_launchedProcess.Id}");

            // Wait for the main window to appear
            AddLog("Waiting for application window...");
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(config.StartupTimeoutMs);
            IntPtr hwnd = IntPtr.Zero;

            while (DateTime.UtcNow < deadline)
            {
                _launchedProcess.Refresh();
                hwnd = _launchedProcess.MainWindowHandle;
                if (ViewportLocator.IsValidTarget(hwnd))
                    break;

                await Task.Delay(500).ConfigureAwait(true);
            }

            if (!ViewportLocator.IsValidTarget(hwnd))
            {
                AddLog("ERROR: Application window did not appear within timeout.");
                _statusLabel.Text = "App window not found. Try increasing startup timeout.";
                ResetUI();
                return;
            }

            AddLog($"Window found: 0x{hwnd:X}");

            // Position target window deterministically
            AddLog("Positioning target window...");
            WindowPositioner.PositionTargetWindow(hwnd);

            // Move Canary UI out of the way
            var mainForm = FindForm();
            if (mainForm != null)
                WindowPositioner.PositionCanaryWindow(mainForm.Handle, mainForm.Width, mainForm.Height);

            await Task.Delay(500).ConfigureAwait(true);

            var bounds = ViewportLocator.GetViewportBounds(hwnd);
            AddLog($"Viewport: {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y})");

            // Small delay to let the app fully settle
            AddLog("Waiting for app to settle...");
            await Task.Delay(2000).ConfigureAwait(true);

            // Bring the app to the foreground
            SetForegroundWindow(hwnd);
            await Task.Delay(300).ConfigureAwait(true);

            // Show abort overlay
            _overlay = new AbortOverlayForm(hwnd, "RECORDING");
            _overlay.Aborted += () => BeginInvoke(new Action(() => OnStop(null, EventArgs.Empty)));
            _overlay.Show();

            // Register global Pause hotkey
            if (mainForm is MainForm mf)
            {
                var hotkey = mf.RegisterAbortHotkey();
                hotkey.AbortRequested += () => BeginInvoke(new Action(() => OnStop(null, EventArgs.Empty)));
            }

            _stopButton.Enabled = true;
            _stopButton.BackColor = Color.FromArgb(180, 50, 50);
            _statusLabel.Text = "Recording... interact with the target window. Click Stop & Save when done.";

            // Move cursor to center of viewport so every recording starts from the same spot
            WindowPositioner.MoveCursorToHome(bounds);
            await Task.Delay(100).ConfigureAwait(true);

            AddLog("Recording started — interact with the app window.");
            AddLog($"Test: {_testNameBox.Text}");

            var workloadName = config.Name;
            _recorder = new InputRecorder(hwnd, workloadName, config.WindowTitle);
            _recorder.StartRecording();
            _updateTimer.Start();
        }
        catch (Exception ex)
        {
            AddLog($"ERROR: {ex.Message}");
            _statusLabel.Text = $"Launch failed: {ex.Message}";
            ResetUI();
        }
    }

    private void OnStop(object? sender, EventArgs e)
    {
        _updateTimer.Stop();
        if (_recorder == null) return;

        AddLog("Stopping recording...");
        var recording = _recorder.StopRecording();

        ResetUI();
        _eventCountLabel.Text = $"Events: {recording.Events.Count}";
        _statusLabel.Text = $"Captured {recording.Events.Count} events ({recording.Metadata.DurationMs / 1000.0:F1}s).";
        _statusLabel.ForeColor = Color.FromArgb(140, 140, 140);

        AddLog($"Recording stopped: {recording.Events.Count} events, {recording.Metadata.DurationMs / 1000.0:F1}s duration.");

        // Auto-save to the workload's recordings directory
        var testName = _testNameBox.Text.Trim();
        var workloadName = _workloadCombo.SelectedItem?.ToString() ?? "unknown";

        if (_workloadsDir != null)
        {
            var recordingsDir = Path.Combine(_workloadsDir, workloadName, "recordings");
            Directory.CreateDirectory(recordingsDir);
            var savePath = Path.Combine(recordingsDir, $"{testName}.input.json");

            var json = JsonSerializer.Serialize(recording, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(savePath, json);
            AddLog($"Saved to: {savePath}");
            _statusLabel.Text = $"Saved {recording.Events.Count} events to {Path.GetFileName(savePath)}";
            RecordingSaved?.Invoke();
        }
        else
        {
            AddLog("WARNING: No workloads directory set — recording not saved.");
            _statusLabel.Text = "Recording not saved (no workloads directory).";
        }

        _recorder = null;

        // Kill the launched app so it doesn't interfere with subsequent test runs
        if (_launchedProcess != null)
        {
            try
            {
                if (!_launchedProcess.HasExited)
                {
                    _launchedProcess.Kill(entireProcessTree: true);
                    _launchedProcess.WaitForExit(5000);
                }
            }
            catch { }
            _launchedProcess = null;
        }
    }

    private void ResetUI()
    {
        _startButton.Enabled = true;
        _testNameBox.Enabled = true;
        _workloadCombo.Enabled = true;
        _stopButton.Enabled = false;
        _stopButton.BackColor = Color.FromArgb(60, 60, 60);

        _overlay?.Close();
        _overlay = null;

        (FindForm() as MainForm)?.UnregisterAbortHotkey();
    }

    private void OnUpdateTick(object? sender, EventArgs e)
    {
        if (_recorder == null) return;

        var count = _recorder.EventCount;
        _eventCountLabel.Text = $"Events: {count}";

        var newEvents = count - _lastLoggedCount;
        if (newEvents > 0)
        {
            AddLog($"+{newEvents} events (total: {count})");
            _lastLoggedCount = count;
        }
    }

    private void AddLog(string message)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss.f}] {message}{Environment.NewLine}");
        _logBox.ScrollToCaret();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _recorder?.Dispose();

            // Safety net: kill launched process if still alive
            if (_launchedProcess != null && !_launchedProcess.HasExited)
            {
                try { _launchedProcess.Kill(entireProcessTree: true); } catch { }
            }
        }
        base.Dispose(disposing);
    }

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        ForeColor = Color.FromArgb(180, 180, 180),
        Font = new Font("Segoe UI", 9.5f),
        AutoSize = true,
        Padding = new Padding(0, 6, 0, 0)
    };

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
