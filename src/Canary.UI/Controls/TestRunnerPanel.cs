using Canary;
using Canary.Config;
using Canary.Orchestration;
using Canary.UI.Services;

namespace Canary.UI.Controls;

/// <summary>
/// Panel that runs tests with live progress display.
/// </summary>
internal sealed class TestRunnerPanel : UserControl
{
    private readonly ListBox _logList;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Button _stopButton;
    private CancellationTokenSource? _cts;

    public event Action<SuiteResult>? RunCompleted;

    public TestRunnerPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(10)
        };

        _statusLabel = new Label
        {
            Text = "Ready",
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 10f),
            AutoSize = true,
            Location = new Point(10, 5)
        };

        _stopButton = new Button
        {
            Text = "Stop",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(180, 50, 50),
            ForeColor = Color.White,
            Size = new Size(80, 30),
            Enabled = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(topPanel.Width - 100, 5)
        };
        _stopButton.Click += (_, _) => _cts?.Cancel();

        topPanel.Controls.Add(_statusLabel);
        topPanel.Controls.Add(_stopButton);

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 8,
            Style = ProgressBarStyle.Continuous
        };

        _logList = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(200, 200, 200),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9f),
            IntegralHeight = false
        };

        Controls.Add(_logList);
        Controls.Add(_progressBar);
        Controls.Add(topPanel);
    }

    public async Task RunAsync(
        WorkloadConfig workload,
        IReadOnlyList<TestDefinition> tests,
        string workloadsDir)
    {
        _logList.Items.Clear();
        _progressBar.Value = 0;
        _progressBar.Maximum = tests.Count;
        _stopButton.Enabled = true;
        _statusLabel.Text = $"Running {tests.Count} test(s)...";

        _cts = new CancellationTokenSource();

        var logger = new GuiTestLogger(this, verbose: true);
        logger.MessageLogged += msg =>
        {
            _logList.Items.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            _logList.TopIndex = _logList.Items.Count - 1;
        };
        logger.StatusLogged += (symbol, msg, _) =>
        {
            _logList.Items.Add($"  {symbol} {msg}");
            _logList.TopIndex = _logList.Items.Count - 1;
            if (_progressBar.Value < _progressBar.Maximum)
                _progressBar.Value++;
        };
        logger.SummaryLogged += msg =>
        {
            _logList.Items.Add(msg);
            _logList.TopIndex = _logList.Items.Count - 1;
        };

        try
        {
            var pm = new ProcessManager();
            var runner = new TestRunner(pm, workloadsDir, logger);

            var suite = await Task.Run(
                () => runner.RunSuiteAsync(workload, tests, _cts.Token),
                _cts.Token).ConfigureAwait(true);

            _statusLabel.Text = $"Done: {suite.Passed} passed, {suite.Failed} failed";
            _progressBar.Value = _progressBar.Maximum;
            RunCompleted?.Invoke(suite);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Cancelled by user.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _stopButton.Enabled = false;
            _cts.Dispose();
            _cts = null;
        }
    }
}
