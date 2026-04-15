using Canary;
using Canary.Config;
using Canary.Orchestration;
using Canary.Reporting;
using Canary.UI.Services;

namespace Canary.UI.Controls;

/// <summary>
/// Panel that runs tests with live progress display.
/// Log output is selectable and copyable (Ctrl+A, Ctrl+C).
/// </summary>
internal sealed class TestRunnerPanel : UserControl
{
    private readonly RichTextBox _logBox;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Button _stopButton;
    private CancellationTokenSource? _cts;
    private ProcessManager? _pm;

    public event Action<SuiteResult>? RunCompleted;

    /// <summary>Returns the full log text for display after the run completes.</summary>
    public string LogText => _logBox.Text;

    public TestRunnerPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 4, 8, 4),
            WrapContents = false
        };

        _statusLabel = new Label
        {
            Text = "Ready",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 10f),
            AutoSize = true,
            Margin = new Padding(0, 4, 12, 0)
        };

        _stopButton = new Button
        {
            Text = "Stop",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(180, 50, 50),
            ForeColor = Color.White,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 2, 10, 2),
            Enabled = false
        };
        _stopButton.Click += OnStop;

        topPanel.Controls.Add(_statusLabel);
        topPanel.Controls.Add(_stopButton);

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 8,
            Style = ProgressBarStyle.Continuous
        };

        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(200, 200, 200),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9f),
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };

        Controls.Add(_logBox);
        Controls.Add(_progressBar);
        Controls.Add(topPanel);
    }

    private void OnStop(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        _pm?.KillAll();
        _statusLabel.Text = "Stopping...";
        AddLog("Stop requested — killing processes...");
    }

    private void AddLog(string message)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _logBox.ScrollToCaret();
    }

    public async Task RunAsync(
        WorkloadConfig workload,
        IReadOnlyList<TestDefinition> tests,
        string workloadsDir)
    {
        _logBox.Clear();
        _progressBar.Value = 0;
        _progressBar.Maximum = tests.Count;
        _stopButton.Enabled = true;
        _statusLabel.Text = $"Running {tests.Count} test(s)...";

        _cts = new CancellationTokenSource();
        _pm = new ProcessManager();

        var logger = new GuiTestLogger(this, verbose: true);
        logger.MessageLogged += msg => AddLog(msg);
        logger.StatusLogged += (symbol, msg, _) =>
        {
            _logBox.AppendText($"  {symbol} {msg}{Environment.NewLine}");
            _logBox.ScrollToCaret();
            if (_progressBar.Value < _progressBar.Maximum)
                _progressBar.Value++;
        };
        logger.SummaryLogged += msg =>
        {
            _logBox.AppendText($"{msg}{Environment.NewLine}");
            _logBox.ScrollToCaret();
        };

        AbortOverlayForm? overlay = null;

        // Position Canary UI to the right of the target window
        var mainForm = FindForm();
        if (mainForm != null)
            Canary.Input.WindowPositioner.PositionCanaryWindow(mainForm.Handle, mainForm.Width, mainForm.Height);

        // Register global Pause abort hotkey
        if (mainForm is MainForm mf)
        {
            var hotkey = mf.RegisterAbortHotkey();
            hotkey.AbortRequested += () => BeginInvoke(new Action(() =>
            {
                _cts?.Cancel();
                _pm?.KillAll();
                _statusLabel.Text = "Aborted via Pause key.";
                AddLog("Aborted via Pause key.");
            }));
        }

        try
        {
            var runner = new TestRunner(_pm, workloadsDir, logger);

            // Show overlay when target window is found
            runner.OnTargetWindowFound = hwnd =>
            {
                try
                {
                    BeginInvoke(() =>
                    {
                        overlay = new AbortOverlayForm(hwnd, "RUNNING");
                        overlay.Aborted += () =>
                        {
                            _cts?.Cancel();
                            _pm?.KillAll();
                        };
                        overlay.Show();
                    });
                }
                catch { /* form may be disposed */ }
            };

            AddLog("Starting test suite...");

            var suite = await Task.Run(
                () => runner.RunSuiteAsync(workload, tests, _cts.Token),
                _cts.Token).ConfigureAwait(true);

            _statusLabel.Text = $"Done: {suite.Passed} passed, {suite.Failed} failed, {suite.Crashed} crashed";
            _progressBar.Value = _progressBar.Maximum;

            foreach (var r in suite.TestResults)
            {
                if (r.Status == TestStatus.Crashed && !string.IsNullOrEmpty(r.ErrorMessage))
                    AddLog($"CRASH {r.TestName}: {r.ErrorMessage}");
            }

            // Generate HTML report
            try
            {
                var resultsDir = Path.Combine(workloadsDir, workload.Name, "results");
                Directory.CreateDirectory(resultsDir);
                var htmlPath = Path.Combine(resultsDir, "report.html");
                await HtmlReportGenerator.SaveAsync(suite, workload.DisplayName, htmlPath).ConfigureAwait(true);
                AddLog($"Report saved: {htmlPath}");
            }
            catch (Exception ex)
            {
                AddLog($"Warning: Could not save report — {ex.Message}");
            }

            RunCompleted?.Invoke(suite);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Cancelled by user.";
            AddLog("Test run cancelled.");
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
            AddLog($"ERROR: {ex.Message}");
        }
        finally
        {
            overlay?.Close();
            overlay = null;
            (FindForm() as MainForm)?.UnregisterAbortHotkey();

            _pm.KillAll();
            _pm = null;
            _stopButton.Enabled = false;
            _cts.Dispose();
            _cts = null;
        }
    }
}
