using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Canary;
using Canary.Agent.Penumbra;
using Canary.Agent.Qualia;
using Canary.Config;
using Canary.Orchestration;
using Canary.Reporting;
using Canary.Telemetry;
using Canary.UI.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

public enum TestRunnerState { Idle, Running, KeepingOpen }

public sealed partial class ProgressCard : ObservableObject
{
    public required string TestName { get; init; }
    public required string CheckpointName { get; init; }
    [ObservableProperty] private string _status = "pending";
    [ObservableProperty] private string _statusColor = "#969696";
    [ObservableProperty] private string? _vlmPrompt;
    [ObservableProperty] private string? _vlmReasoning;
    [ObservableProperty] private string? _imagePath;
    public string Key => $"{TestName}/{CheckpointName}";
}

public partial class TestRunnerViewModel : ObservableObject, ITestProgressEvents
{
    private CancellationTokenSource? _cts;
    private ProcessManager? _pm;
    private bool _keepOpenAfterRun;

    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ProgressCard> ProgressCards { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private TestRunnerState _state = TestRunnerState.Idle;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string? _suiteLabel;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMax = 1;

    [ObservableProperty]
    private ModeOverride _modeOverride = ModeOverride.None;

    public Func<IntPtr?>? GetMainWindowHandleAsync { get; set; }
    public Func<SuiteResult, Task>? OnRunCompletedAsync { get; set; }

    // Phase 5: set by MainWindow so AbortHotkey can arm against the
    // main window HWND for the duration of a run.
    public Action? OnRunStarted { get; set; }
    public Action? OnRunFinished { get; set; }

    public bool HasActiveProcesses => _pm != null;

    [RelayCommand]
    public async Task RunAsync(RunRequest request)
    {
        if (State != TestRunnerState.Idle) return;
        LogLines.Clear();
        ProgressCards.Clear();
        ProgressValue = 0;
        ProgressMax = Math.Max(1, request.Tests.Count);
        State = TestRunnerState.Running;
        StatusText = $"Running {request.Tests.Count} test(s)...";
        SuiteLabel = request.SuiteName != null
            ? $"Suite: {request.SuiteName} ({request.Tests.Count} tests){(request.UseSharedMode ? " [shared instance]" : string.Empty)}"
            : null;

        _cts = new CancellationTokenSource();
        _pm = new ProcessManager();
        _keepOpenAfterRun = false;
        OnRunStarted?.Invoke();

        var logger = new AvaloniaTestLogger(verbose: true);
        logger.MessageLogged += msg => Append(msg);
        logger.StatusLogged += (symbol, msg, _) =>
        {
            Append($"  {symbol} {msg}");
            if (ProgressValue < ProgressMax) ProgressValue++;
        };
        logger.SummaryLogged += msg => Append(msg);

        try
        {
            var runner = new TestRunner(_pm, request.WorkloadsDir, logger)
            {
                Progress = this,
                ModeOverride = ModeOverride,
            };

            Append(request.SuiteName != null ? $"Starting suite '{request.SuiteName}'..." : "Starting test suite...");

            SuiteResult suite;
            if (request.Workload.AgentType == "penumbra-cdp")
                suite = await Task.Run(() => RunPenumbraAsync(request, runner, logger, _cts.Token), _cts.Token).ConfigureAwait(true);
            else if (request.Workload.AgentType == "qualia-cdp")
                suite = await Task.Run(() => RunQualiaAsync(request, runner, logger, _cts.Token), _cts.Token).ConfigureAwait(true);
            else if (request.UseSharedMode)
                suite = await Task.Run(() => runner.RunSharedSuiteAsync(request.Workload, request.Tests, _cts.Token), _cts.Token).ConfigureAwait(true);
            else
                suite = await Task.Run(() => runner.RunSuiteAsync(request.Workload, request.Tests, _cts.Token), _cts.Token).ConfigureAwait(true);

            StatusText = $"Done: {suite.Passed} passed, {suite.Failed} failed, {suite.Crashed} crashed";
            ProgressValue = ProgressMax;

            _keepOpenAfterRun |= request.SuiteKeepOpen && request.Tests.Any(t => t.KeepOpenOnFailure
                && suite.TestResults.Any(r => r.TestName == t.Name && r.Status is TestStatus.Failed or TestStatus.Crashed));

            foreach (var r in suite.TestResults)
            {
                if (r.Status == TestStatus.Crashed && !string.IsNullOrEmpty(r.ErrorMessage))
                    Append($"CRASH {r.TestName}: {r.ErrorMessage}");
            }

            try
            {
                var resultsDir = Path.Combine(request.WorkloadsDir, request.Workload.Name, "results");
                Directory.CreateDirectory(resultsDir);
                var htmlPath = Path.Combine(resultsDir, "report.html");
                await HtmlReportGenerator.SaveAsync(suite, request.Workload.DisplayName, htmlPath).ConfigureAwait(true);
                Append($"Report saved: {htmlPath}");
            }
            catch (Exception ex)
            {
                Append($"Warning: Could not save report — {ex.Message}");
            }

            if (OnRunCompletedAsync != null) await OnRunCompletedAsync(suite).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled by user.";
            Append("Test run cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Append($"ERROR: {ex.Message}");
        }
        finally
        {
            if (_keepOpenAfterRun)
            {
                StatusText += " — App kept open for inspection";
                Append("App kept open for inspection (keepOpenOnFailure). Click Close App to tear down.");
                State = TestRunnerState.KeepingOpen;
            }
            else
            {
                _pm?.KillAll();
                _pm = null;
                State = TestRunnerState.Idle;
            }
            _cts?.Dispose();
            _cts = null;
            OnRunFinished?.Invoke();
        }
    }

    private bool CanStop() => State == TestRunnerState.Running || State == TestRunnerState.KeepingOpen;

    [RelayCommand(CanExecute = nameof(CanStop))]
    public void Stop()
    {
        _cts?.Cancel();
        _pm?.KillAll();
        _pm = null;
        StatusText = State == TestRunnerState.KeepingOpen ? "Workload closed." : "Stopping…";
        Append(State == TestRunnerState.KeepingOpen ? "Workload closed via Stop." : "Stop requested — killing processes…");
        if (State == TestRunnerState.KeepingOpen) State = TestRunnerState.Idle;
    }

    /// <summary>
    /// On-demand desktop screenshot — captures the whole virtual screen to
    /// %APPDATA%\Canary\captures\<timestamp>.png. Always enabled (even when
    /// no test is running) so the operator can grab warning balloons that
    /// appear between tests, or capture an arbitrary moment of Rhino's state.
    ///
    /// Companion to the auto-fullscreen-per-checkpoint capture wired into
    /// TestRunner — that one fires at checkpoint boundaries; this one fires
    /// when the operator clicks.
    /// </summary>
    [RelayCommand]
    public void CaptureScreen()
    {
        try
        {
            var label = State == TestRunnerState.Running ? "running" : "idle";
            var path = Services.DesktopCapture.NewCapturePath(label);
            var (savedPath, w, h) = Services.DesktopCapture.Capture(path);
            Append($"📷 Captured screen → {savedPath} ({w}×{h})");
            StatusText = $"Captured screen: {Path.GetFileName(savedPath)}";
        }
        catch (Exception ex)
        {
            Append($"Capture failed: {ex.Message}");
            StatusText = "Capture failed";
        }
    }

    private async Task<SuiteResult> RunQualiaAsync(RunRequest request, TestRunner runner, ITestLogger logger, CancellationToken ct)
    {
        var configPath = Path.Combine(request.WorkloadsDir, request.Workload.Name, "workload.json");
        var qConfig = await QualiaWorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
        logger.Log("Initializing Qualia CDP bridge agent...");
        using var agent = new QualiaBridgeAgent(qConfig.QualiaConfig);
        try
        {
            await agent.InitializeAsync(ct).ConfigureAwait(false);
            logger.Log("Qualia bridge agent ready.");
            return await runner.RunAgentSuiteAsync(request.Workload, request.Tests, agent, ct).ConfigureAwait(false);
        }
        finally
        {
            logger.Log("Shutting down Qualia bridge agent...");
        }
    }

    private async Task<SuiteResult> RunPenumbraAsync(RunRequest request, TestRunner runner, ITestLogger logger, CancellationToken ct)
    {
        var configPath = Path.Combine(request.WorkloadsDir, request.Workload.Name, "workload.json");
        var penConfig = await PenumbraWorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);

        logger.Log("Probing for existing Penumbra instance...");
        var probe = await PenumbraInstanceProbe.ProbeAsync(penConfig.PenumbraConfig.VitePort, penConfig.PenumbraConfig.CdpPort).ConfigureAwait(false);

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

            return await runner.RunAgentSuiteAsync(request.Workload, request.Tests, agent, ct).ConfigureAwait(false);
        }
        finally
        {
            logger.Log("Shutting down Penumbra bridge agent...");
            agent.Dispose();
        }
    }

    private void Append(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
        else
        {
            Dispatcher.UIThread.Post(() => LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}"));
        }
    }

    // ITestProgressEvents — marshals to UI thread.
    public void OnTestStarted(string testName) => Post(() => { /* no test-level card in v1 */ });

    public void OnCheckpointStarted(string testName, string checkpointName, string? vlmDescription)
        => Post(() =>
        {
            var card = FindOrCreate(testName, checkpointName);
            card.Status = "running";
            card.StatusColor = "#FFDC50";
            card.VlmPrompt = vlmDescription;
        });

    public void OnScreenshotCaptured(string testName, string checkpointName, string imagePath)
        => Post(() =>
        {
            var card = FindOrCreate(testName, checkpointName);
            card.ImagePath = imagePath;
        });

    public void OnVlmEvaluating(string testName, string checkpointName, string prompt)
        => Post(() =>
        {
            var card = FindOrCreate(testName, checkpointName);
            card.Status = "VLM evaluating…";
            card.StatusColor = "#96C8FF";
            card.VlmPrompt = prompt;
        });

    public void OnVlmVerdict(string testName, string checkpointName, bool passed, double confidence, string reasoning)
        => Post(() =>
        {
            var card = FindOrCreate(testName, checkpointName);
            card.Status = passed ? $"PASS ({confidence:P0})" : $"FAIL ({confidence:P0})";
            card.StatusColor = passed ? "#50C850" : "#DC3C3C";
            card.VlmReasoning = reasoning;
        });

    public void OnTestCompleted(string testName, TestStatus status, double durationSeconds)
        => Post(() => Append($"  {testName} → {status} ({durationSeconds:F1}s)"));

    public void OnTelemetry(TelemetryRecord record) { /* no-op for now; Telemetry tab tails the NDJSON */ }

    private ProgressCard FindOrCreate(string testName, string checkpointName)
    {
        var key = $"{testName}/{checkpointName}";
        var card = ProgressCards.FirstOrDefault(c => c.Key == key);
        if (card == null)
        {
            card = new ProgressCard { TestName = testName, CheckpointName = checkpointName };
            ProgressCards.Add(card);
        }
        return card;
    }

    private static void Post(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }
}

public sealed class RunRequest
{
    public required WorkloadConfig Workload { get; init; }
    public required IReadOnlyList<TestDefinition> Tests { get; init; }
    public required string WorkloadsDir { get; init; }
    public string? SuiteName { get; init; }
    public bool UseSharedMode { get; init; }
    public bool SuiteKeepOpen { get; init; }
}
