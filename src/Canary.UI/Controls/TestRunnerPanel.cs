using Canary;
using Canary.Agent.Penumbra;
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
    private readonly Label _suiteLabel;
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
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 4, 8, 4),
            WrapContents = true
        };

        _statusLabel = new Label
        {
            Text = "Ready",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 10f),
            AutoSize = true,
            Margin = new Padding(0, 4, 12, 0)
        };

        _suiteLabel = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(150, 220, 130),
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Margin = new Padding(0, 5, 12, 0),
            Visible = false
        };

        _stopButton = new Button
        {
            Text = "Stop (Pause)",
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
        topPanel.Controls.Add(_suiteLabel);
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

    /// <summary>
    /// Kills all tracked processes (e.g. Rhino left open via keepOpen).
    /// Called from the toolbar "Close Workload" button.
    /// </summary>
    public void ForceKillProcesses()
    {
        _cts?.Cancel();
        _pm?.KillAll();
        _pm = null;
        _stopButton.Enabled = false;
        _stopButton.Text = "Stop (Pause)";
        _statusLabel.Text += " — Workload closed.";
        AddLog("Workload closed via toolbar.");
    }

    /// <summary>Returns true when a ProcessManager is active (processes may be running).</summary>
    public bool HasActiveProcesses => _pm != null;

    private void AddLog(string message)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _logBox.ScrollToCaret();
    }

    public async Task RunAsync(
        WorkloadConfig workload,
        IReadOnlyList<TestDefinition> tests,
        string workloadsDir,
        string? workloadJsonPath = null,
        string? suiteName = null,
        bool useSharedMode = false,
        bool suiteKeepOpen = false)
    {
        _logBox.Clear();
        _progressBar.Value = 0;
        _progressBar.Maximum = tests.Count;
        _stopButton.Enabled = true;
        _statusLabel.Text = $"Running {tests.Count} test(s)...";

        // Show suite info
        if (suiteName != null)
        {
            var modeLabel = useSharedMode ? " [shared instance]" : "";
            _suiteLabel.Text = $"Suite: {suiteName} ({tests.Count} tests){modeLabel}";
            _suiteLabel.Visible = true;
        }

        _cts = new CancellationTokenSource();
        _pm = new ProcessManager();

        var logger = new GuiTestLogger(this, verbose: true);
        logger.MessageLogged += msg =>
        {
            AddLog(msg);
            // Detect VLM evaluation in progress
            if (msg.Contains("VLM evaluating", StringComparison.OrdinalIgnoreCase))
                _statusLabel.Text = "VLM evaluating...";
        };
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
        bool keepOpen = suiteKeepOpen;

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

            AddLog(suiteName != null ? $"Starting suite '{suiteName}'..." : "Starting test suite...");

            SuiteResult suite;
            if (workload.AgentType == "penumbra-cdp")
                suite = await Task.Run(
                    () => RunPenumbraSuiteAsync(workload, tests, runner, workloadsDir, logger, _cts.Token),
                    _cts.Token).ConfigureAwait(true);
            else if (useSharedMode)
                suite = await Task.Run(
                    () => runner.RunSharedSuiteAsync(workload, tests, _cts.Token),
                    _cts.Token).ConfigureAwait(true);
            else
                suite = await Task.Run(
                    () => runner.RunSuiteAsync(workload, tests, _cts.Token),
                    _cts.Token).ConfigureAwait(true);

            _statusLabel.Text = $"Done: {suite.Passed} passed, {suite.Failed} failed, {suite.Crashed} crashed";
            _progressBar.Value = _progressBar.Maximum;

            keepOpen |= tests.Any(t => t.KeepOpenOnFailure
                && suite.TestResults.Any(r => r.TestName == t.Name
                    && r.Status is TestStatus.Failed or TestStatus.Crashed));

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
                AddLog($"Warning: Could not save report \u2014 {ex.Message}");
            }

            // Persist result JSON for each test (feeds ResultsHistory.ScanAsync)
            foreach (var result in suite.TestResults)
            {
                try
                {
                    var resultDir = Path.Combine(workloadsDir, workload.Name, "results", result.TestName);
                    Directory.CreateDirectory(resultDir);
                    var resultPath = Path.Combine(resultDir, "result.json");
                    await TestResultSerializer.SaveAsync(result, resultPath).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    AddLog($"Warning: Could not save result for '{result.TestName}' \u2014 {ex.Message}");
                }
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

            if (keepOpen)
            {
                _statusLabel.Text += " — App kept open for inspection";
                AddLog("App kept open for inspection (keepOpenOnFailure). Click Stop to close.");
                _stopButton.Enabled = true;
                _stopButton.Text = "Close App";
                _stopButton.Click -= OnStop;
                _stopButton.Click += (_, _) =>
                {
                    _pm?.KillAll();
                    _pm = null;
                    _stopButton.Enabled = false;
                    _stopButton.Text = "Stop (Pause)";
                };
            }
            else
            {
                _pm?.KillAll();
                _pm = null;
                _stopButton.Enabled = false;
            }

            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task<SuiteResult> RunPenumbraSuiteAsync(
        WorkloadConfig workload,
        IReadOnlyList<TestDefinition> tests,
        TestRunner runner,
        string workloadsDir,
        ITestLogger logger,
        CancellationToken ct)
    {
        // Load the Penumbra-specific config from workload.json
        var configPath = Path.Combine(workloadsDir, workload.Name, "workload.json");
        var penConfig = await PenumbraWorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);

        // Probe for an already-running instance
        logger.Log("Probing for existing Penumbra instance...");
        var probe = await PenumbraInstanceProbe.ProbeAsync(
            penConfig.PenumbraConfig.VitePort,
            penConfig.PenumbraConfig.CdpPort).ConfigureAwait(false);

        var agent = new PenumbraBridgeAgent(penConfig.PenumbraConfig);
        try
        {
            if (probe.PenumbraReady && probe.PageWebSocketUrl != null && probe.ViteUrl != null)
            {
                logger.Log($"Reusing existing instance (Vite={probe.ViteUrl}, backend={probe.RendererBackend})");
                await agent.InitializeFromExistingAsync(probe.PageWebSocketUrl, probe.ViteUrl, ct).ConfigureAwait(false);
            }
            else
            {
                logger.Log("No existing instance found — launching fresh Vite + Chrome...");
                await agent.InitializeAsync(ct).ConfigureAwait(false);
            }

            return await runner.RunAgentSuiteAsync(workload, tests, agent, ct).ConfigureAwait(false);
        }
        finally
        {
            logger.Log("Shutting down Penumbra bridge agent...");
            agent.Dispose();
        }
    }
}
