using System.Diagnostics;
using Canary.Agent;
using Canary.Comparison;
using Canary.Config;
using Canary.Input;
using Canary.Reporting;
using Canary.Telemetry;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Canary.Orchestration;

/// <summary>
/// Orchestrates the full test lifecycle: launch app -> connect -> setup -> replay -> capture -> compare.
/// </summary>
/// <summary>
/// Effective comparison mode for a checkpoint, after applying the
/// <see cref="TestRunner.ModeOverride"/> CLI flag and any per-checkpoint
/// <c>mode</c> override in the test JSON.
/// </summary>
public enum CheckpointMode
{
    /// <summary>Pixel-diff against a stored baseline.</summary>
    PixelDiff,
    /// <summary>VLM (vision-language model) evaluation against a description.</summary>
    Vlm,
    /// <summary>
    /// Capture-only: save the screenshot as the candidate and run NO comparison
    /// (neither pixel-diff nor VLM). Not a verification — used to record images for
    /// manual review without a verdict. Opted in via a checkpoint <c>mode = "capture"</c>
    /// (aliases: "none", "off"); wins over the <c>--mode</c> override.
    /// </summary>
    Capture,
}

/// <summary>
/// Optional runtime override of every checkpoint's comparison mode. Set by
/// the <c>--mode</c> CLI flag.
/// </summary>
public enum ModeOverride
{
    /// <summary>No override. Each checkpoint's <c>mode</c> field is honoured.</summary>
    None,
    /// <summary>Force pixel-diff for all checkpoints (regression mode).</summary>
    PixelDiff,
    /// <summary>Force VLM for all checkpoints (correctness mode).</summary>
    Vlm,
    /// <summary>Run each checkpoint twice — once pixel-diff, once VLM.</summary>
    Both,
}

public sealed class TestRunner
{
    private readonly ProcessManager _processManager;
    private readonly string _workloadsDir;
    private readonly ITestLogger _logger;
    private readonly PixelDiffComparer _pixelDiff = new();
    private readonly SsimComparer _ssim = new();
    private IVlmProvider? _vlmProvider;
    private Config.VlmConfig? _vlmConfig;
    private string? _currentVlmDescription;

    /// <summary>
    /// Structured event sink for GUI consumers (progress feed, per-checkpoint
    /// cards). Defaults to a no-op; set via property after construction.
    /// </summary>
    public ITestProgressEvents Progress { get; set; } = NullTestProgressEvents.Instance;

    /// <summary>
    /// Callback invoked when the target window handle is found.
    /// Called from the test thread — callers must marshal to UI thread if needed.
    /// </summary>
    public Action<IntPtr>? OnTargetWindowFound { get; set; }

    /// <summary>
    /// Runtime override for checkpoint comparison mode. Defaults to
    /// <see cref="ModeOverride.None"/>, which honours each checkpoint's
    /// own <c>mode</c> field. Set via the <c>canary run --mode</c> CLI flag.
    /// Per-checkpoint <c>mode = "vlm"</c> still wins over a
    /// <see cref="ModeOverride.PixelDiff"/> override (explicit beats implicit).
    /// </summary>
    public ModeOverride ModeOverride { get; set; } = ModeOverride.None;

    /// <summary>
    /// Phase 2 / §C1: per-run telemetry sink. Defaults to a no-op so
    /// callers that don't care can ignore it. RunCommand sets this to a
    /// per-suite NdjsonFileSink. CDP bridge agents that implement
    /// <see cref="ITelemetryAware"/> get the same sink registered before
    /// their InitializeAsync so console + network + log records flow
    /// through the same file.
    /// </summary>
    public ITelemetrySink TelemetrySink { get; set; } = NullTelemetrySink.Instance;

    public TestRunner(ProcessManager processManager, string workloadsDir, ITestLogger logger)
    {
        _processManager = processManager;
        _workloadsDir = workloadsDir;
        _logger = logger;
    }

    /// <summary>
    /// Run a single test definition against the target workload.
    /// </summary>
    public async Task<TestResult> RunTestAsync(
        TestDefinition testDef,
        WorkloadConfig workload,
        CancellationToken cancellationToken,
        string? suiteName = null)
    {
        var sw = Stopwatch.StartNew();
        var startedUtc = DateTime.UtcNow;
        var result = new TestResult
        {
            TestName = testDef.Name,
            Workload = workload.Name,
            Status = TestStatus.Passed
        };
        string? testDirSaved = null;

        Process? appProcess = null;
        HarnessClient? client = null;
        Task? watchdogTask = null;
        var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool appDead = false;

        // Pre-create the test directory + emit OnTestDirectoryReady BEFORE app launch
        // so operator-triggered "📷 Capture Screen" clicks during the launch / setup /
        // GUID-warning phase route into <testDir>\manual-captures\ instead of falling
        // back to %APPDATA%\Canary\captures\. Phase 4.6.E.A.3.
        {
            var earlyDir = GetTestDirectory(workload.Name, testDef.Name, suiteName);
            Directory.CreateDirectory(earlyDir);
            testDirSaved = earlyDir;
            Progress?.OnTestDirectoryReady(testDef.Name, earlyDir);
        }

        try
        {
            // 1. Launch application
            _logger.Log($"Launching {workload.DisplayName}...");
            appProcess = AppLauncher.Launch(workload);
            _processManager.Track(appProcess);

            var pipeName = $"{workload.PipeName}-{appProcess.Id}";

            // 2. Connect to agent (waits for pipe to appear during app startup).
            // 120s per-call timeout is generous for cold-start ops like
            // OpenGrasshopperDefinition (first GH load discovers all plugins
            // on the UI thread; can take 30-60s before reply gets sent).
            _logger.Log($"Connecting to agent on pipe '{pipeName}' (timeout: {workload.StartupTimeoutMs}ms)...");
            client = new HarnessClient(pipeName, TimeSpan.FromSeconds(120));
            await client.ConnectAsync(workload.StartupTimeoutMs, cancellationToken).ConfigureAwait(false);
            _logger.Log("Agent connected.");

            // 3. Verify heartbeat
            _logger.Log("Sending heartbeat...");
            var hb = await client.HeartbeatAsync(cancellationToken).ConfigureAwait(false);
            _logger.Log($"Heartbeat: ok={hb.Ok}");
            if (!hb.Ok)
            {
                result.Status = TestStatus.Crashed;
                result.ErrorMessage = "Agent heartbeat returned ok=false.";
                result.Duration = sw.Elapsed;
                return result;
            }

            // 4. Start watchdog
            var watchdog = new Watchdog(new HarnessClientHeartbeatSource(client));
            watchdog.OnAppDead += () => appDead = true;
            watchdogTask = watchdog.RunAsync(watchdogCts.Token);

            // 5. Send setup commands if test defines them
            if (testDef.Setup != null)
            {
                _logger.Log("Sending setup commands...");
                await SendSetupCommandsAsync(client, testDef.Setup, workload, cancellationToken).ConfigureAwait(false);
                _logger.Log("Setup commands complete.");
            }

            // 5b. Run pre-checkpoint actions (Phase 13.2 — CPig workload).
            // Each action's `type` is dispatched directly to the agent over
            // the named pipe. Failures abort the test with status=Crashed.
            if (testDef.Actions.Count > 0)
            {
                _logger.Log($"Running {testDef.Actions.Count} pre-checkpoint action(s)...");
                foreach (var action in testDef.Actions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var parameters = action.AsParameters();
                    _logger.Log($"  action: {action.Type}");
                    var resp = await client!.ExecuteAsync(action.Type, parameters, cancellationToken).ConfigureAwait(false);
                    if (!resp.Success)
                    {
                        result.Status = TestStatus.Crashed;
                        result.ErrorMessage = $"Action '{action.Type}' failed: {resp.Message}";
                        result.Duration = sw.Elapsed;
                        return result;
                    }
                }
                _logger.Log("Actions complete.");
            }

            // 5c. Initialize VLM provider if any checkpoint uses vlm mode
            InitVlmProviderIfNeeded(testDef);

            // 6. Process checkpoints (capture + compare) — testDir was pre-created above.
            var testDir = testDirSaved!;

            // Default capture size from WindowPositioner; updated after positioning
            var captureWidth = WindowPositioner.TargetWidth;
            var captureHeight = WindowPositioner.TargetHeight;

            if (!string.IsNullOrWhiteSpace(testDef.Recording))
            {
                // Path A: Replay recording with timed checkpoints
                var recordingPath = ResolveRecordingPath(workload.Name, testDef.Recording);
                if (!File.Exists(recordingPath))
                {
                    result.Status = TestStatus.Crashed;
                    result.ErrorMessage = $"Recording not found: {recordingPath}";
                    result.Duration = sw.Elapsed;
                    return result;
                }

                _logger.Log($"Loading recording: {recordingPath}");
                var recording = await InputRecording.LoadAsync(recordingPath).ConfigureAwait(false);
                _logger.Log($"Recording: {recording.Events.Count} events, {recording.Metadata.DurationMs}ms");

                // Find the target window for input injection — use the launched process's own window
                appProcess!.Refresh();
                var targetHwnd = appProcess.MainWindowHandle;
                if (!ViewportLocator.IsValidTarget(targetHwnd))
                {
                    // Fallback: search by title
                    targetHwnd = ViewportLocator.FindWindowByTitle(workload.WindowTitle);
                }
                if (!ViewportLocator.IsValidTarget(targetHwnd))
                {
                    result.Status = TestStatus.Crashed;
                    result.ErrorMessage = $"Target window not found for process {appProcess.Id}";
                    result.Duration = sw.Elapsed;
                    return result;
                }

                // Position target window deterministically
                WindowPositioner.PositionTargetWindow(targetHwnd);
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                OnTargetWindowFound?.Invoke(targetHwnd);

                var replayBounds = ViewportLocator.GetViewportBounds(targetHwnd);
                captureWidth = replayBounds.Width;
                captureHeight = replayBounds.Height;
                _logger.Log($"Replay target: 0x{targetHwnd:X} ({replayBounds.Width}x{replayBounds.Height})");

                // Build checkpoint time → checkpoint list mapping
                var checkpointsByTime = new Dictionary<long, List<TestCheckpoint>>();
                foreach (var cp in testDef.Checkpoints)
                {
                    if (!checkpointsByTime.TryGetValue(cp.AtTimeMs, out var list))
                    {
                        list = new List<TestCheckpoint>();
                        checkpointsByTime[cp.AtTimeMs] = list;
                    }
                    list.Add(cp);
                }

                var checkpointTimes = checkpointsByTime.Keys;

                // Checkpoint callback: capture + compare at each timed checkpoint
                async Task OnCheckpointReached(long timeMs)
                {
                    if (appDead) return;
                    if (!checkpointsByTime.TryGetValue(timeMs, out var checkpoints)) return;

                    foreach (var checkpoint in checkpoints)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _logger.Log($"Checkpoint '{checkpoint.Name}' at {timeMs}ms");

                        await DispatchClientCheckpointAsync(
                            client!, checkpoint, testDir, captureWidth, captureHeight, result, cancellationToken).ConfigureAwait(false);
                    }
                }

                // Move cursor to center of viewport so replay starts from the same spot as recording
                WindowPositioner.MoveCursorToHome(replayBounds);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                var replayer = new InputReplayer(recording, replayBounds, 1.0, checkpointTimes, OnCheckpointReached, targetHwnd);
                _logger.Log("Replaying recorded input...");
                await replayer.ReplayAsync(cancellationToken).ConfigureAwait(false);
                _logger.Log("Replay complete.");
            }
            else
            {
                // Path B: No recording — position window and capture checkpoints immediately
                appProcess!.Refresh();
                var targetHwnd = appProcess.MainWindowHandle;
                if (ViewportLocator.IsValidTarget(targetHwnd))
                {
                    WindowPositioner.PositionTargetWindow(targetHwnd);
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    OnTargetWindowFound?.Invoke(targetHwnd);
                }

                foreach (var checkpoint in testDef.Checkpoints)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (appDead)
                    {
                        result.Status = TestStatus.Crashed;
                        result.ErrorMessage = "Application crashed during test.";
                        break;
                    }

                    await DispatchClientCheckpointAsync(
                        client!, checkpoint, testDir, captureWidth, captureHeight, result, cancellationToken).ConfigureAwait(false);
                }
            }

            // 6b. Evaluate asserts after checkpoints (Phase 13.2). Asserts
            // surface logic failures that don't visually change the canvas
            // (e.g. CPig's Slop component reports SlopSuccess=False without
            // changing any rendered geometry). Failed asserts flip a
            // pixel-diff-passed test to Failed.
            if (testDef.Asserts.Count > 0 && !appDead)
            {
                _logger.Log($"Evaluating {testDef.Asserts.Count} assert(s)...");
                foreach (var assert in testDef.Asserts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (ok, message) = await EvaluateClientAssertAsync(client!, assert, cancellationToken).ConfigureAwait(false);
                    if (ok)
                    {
                        _logger.Log($"  + {assert.Type} {assert.Nickname}");
                    }
                    else
                    {
                        _logger.Log($"  - {assert.Type} {assert.Nickname}: {message}");
                        if (result.Status == TestStatus.Passed || result.Status == TestStatus.New)
                            result.Status = TestStatus.Failed;
                        result.ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage)
                            ? $"Assert failed: {message}"
                            : result.ErrorMessage + $"; {message}";
                    }
                }
            }

            // 7. Build composite image if there are checkpoint results
            if (result.CheckpointResults.Count > 0)
            {
                try
                {
                    result.CompositeImagePath = await BuildCompositeAsync(result, testDir).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Log($"Warning: Failed to build composite image: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Status = TestStatus.Crashed;
            result.ErrorMessage = "Test cancelled by user.";
        }
        catch (Exception ex)
        {
            result.Status = TestStatus.Crashed;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            watchdogCts.Cancel();
            if (watchdogTask != null)
            {
                try { await watchdogTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            watchdogCts.Dispose();
            client?.Dispose();
        }

        result.Duration = sw.Elapsed;
        if (testDirSaved != null)
            await SavePerRunArtifactsAsync(result, workload, testDirSaved, startedUtc, DateTime.UtcNow).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Run all tests for a workload.
    /// </summary>
    public async Task<SuiteResult> RunSuiteAsync(
        WorkloadConfig workload,
        IReadOnlyList<TestDefinition> tests,
        CancellationToken cancellationToken,
        string? suiteName = null)
    {
        var suite = new SuiteResult();

        foreach (var test in tests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await RunTestAsync(test, workload, cancellationToken, suiteName).ConfigureAwait(false);
            suite.TestResults.Add(result);

            var (symbol, level) = result.Status switch
            {
                TestStatus.Passed => ("PASS", TestStatusLevel.Pass),
                TestStatus.Failed => ("FAIL", TestStatusLevel.Fail),
                TestStatus.Crashed => ("CRASH", TestStatusLevel.Crash),
                TestStatus.New => ("NEW", TestStatusLevel.New),
                _ => ("???", TestStatusLevel.Info)
            };

            var hasVlm = result.CheckpointResults.Any(c => c.VlmDescription != null);
            var maxDiff = result.CheckpointResults.Count > 0
                ? result.CheckpointResults.Max(c => c.DiffPercentage)
                : 0;

            var statusDetail = hasVlm && maxDiff == 0
                ? $"{result.TestName} (VLM)"
                : $"{result.TestName} ({maxDiff:P1} max diff)";
            _logger.LogStatus(symbol, statusDetail, level);

            // Verbose: show per-checkpoint details
            if (_logger.Verbose)
            {
                foreach (var cp in result.CheckpointResults)
                {
                    var cpSymbol = cp.Status == TestStatus.Passed ? "  +" : "  -";
                    if (cp.VlmDescription != null)
                        _logger.Log($"{cpSymbol} {cp.Name}: VLM conf={cp.VlmConfidence:F2} — {Truncate(cp.VlmReasoning ?? "")}");
                    else
                        _logger.Log($"{cpSymbol} {cp.Name}: diff={cp.DiffPercentage:P2}, ssim={cp.SsimScore:F4}, tol={cp.Tolerance:P2}");
                    if (!string.IsNullOrEmpty(cp.ErrorMessage))
                        _logger.Log($"    Error: {cp.ErrorMessage}");
                }
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage))
                _logger.Log($"  Error: {result.ErrorMessage}");
        }

        // Summary always prints (even in quiet mode)
        _logger.LogSummary($"Results: {suite.Passed} passed, {suite.Failed} failed, {suite.Crashed} crashed, {suite.New} new");
        return suite;
    }

    /// <summary>
    /// Run a list of tests sharing one app instance. Launches the workload app once,
    /// runs the first test's setup commands (e.g. open fixture), then for each test
    /// executes only its actions + checkpoints + asserts. Tests in this path should
    /// have <c>runMode = "shared"</c> and start their actions with a cleanup step.
    /// </summary>
    public async Task<SuiteResult> RunSharedSuiteAsync(
        WorkloadConfig workload,
        IReadOnlyList<TestDefinition> tests,
        CancellationToken cancellationToken)
    {
        var suite = new SuiteResult();
        if (tests.Count == 0) return suite;

        Process? appProcess = null;
        HarnessClient? client = null;
        Task? watchdogTask = null;
        var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool appDead = false;

        try
        {
            _logger.Log($"Launching {workload.DisplayName} (shared session for {tests.Count} test(s))...");
            appProcess = AppLauncher.Launch(workload);
            _processManager.Track(appProcess);

            var pipeName = $"{workload.PipeName}-{appProcess.Id}";
            _logger.Log($"Connecting to agent on pipe '{pipeName}' (timeout: {workload.StartupTimeoutMs}ms)...");
            client = new HarnessClient(pipeName, TimeSpan.FromSeconds(120));
            await client.ConnectAsync(workload.StartupTimeoutMs, cancellationToken).ConfigureAwait(false);
            _logger.Log("Agent connected.");

            var hb = await client.HeartbeatAsync(cancellationToken).ConfigureAwait(false);
            _logger.Log($"Heartbeat: ok={hb.Ok}");
            if (!hb.Ok)
            {
                _logger.Log("Agent heartbeat failed; aborting shared suite.");
                foreach (var t in tests)
                    suite.TestResults.Add(new TestResult { TestName = t.Name, Workload = workload.Name, Status = TestStatus.Crashed, ErrorMessage = "Agent heartbeat returned ok=false." });
                return suite;
            }

            var watchdog = new Watchdog(new HarnessClientHeartbeatSource(client));
            watchdog.OnAppDead += () => appDead = true;
            watchdogTask = watchdog.RunAsync(watchdogCts.Token);

            // One-time fixture open from the first test's setup.
            var firstSetup = tests[0].Setup;
            if (firstSetup != null)
            {
                _logger.Log("Sending setup commands (one-time)...");
                await SendSetupCommandsAsync(client, firstSetup, workload, cancellationToken).ConfigureAwait(false);
                _logger.Log("Setup commands complete.");
            }

            appProcess.Refresh();
            var hwnd = appProcess.MainWindowHandle;
            if (ViewportLocator.IsValidTarget(hwnd))
            {
                WindowPositioner.PositionTargetWindow(hwnd);
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                OnTargetWindowFound?.Invoke(hwnd);
            }

            var captureWidth = WindowPositioner.TargetWidth;
            var captureHeight = WindowPositioner.TargetHeight;

            // Per-test loop — actions, checkpoints, asserts.
            foreach (var test in tests)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sw = Stopwatch.StartNew();
                var result = new TestResult
                {
                    TestName = test.Name,
                    Workload = workload.Name,
                    Status = TestStatus.Passed
                };

                if (appDead)
                {
                    result.Status = TestStatus.Crashed;
                    result.ErrorMessage = "Application died earlier in shared session.";
                    suite.TestResults.Add(result);
                    LogTestStatus(result);
                    continue;
                }

                // Pre-create + emit testDir before per-test actions so manual
                // captures during action execution route correctly. Phase 4.6.E.A.3.
                var sharedTestDir = GetTestDirectory(workload.Name, test.Name);
                Directory.CreateDirectory(sharedTestDir);
                Progress?.OnTestDirectoryReady(test.Name, sharedTestDir);

                try
                {
                    if (test.Actions.Count > 0)
                    {
                        _logger.Log($"[{test.Name}] running {test.Actions.Count} action(s)...");
                        foreach (var action in test.Actions)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var parameters = action.AsParameters();
                            _logger.Log($"  action: {action.Type}");
                            var resp = await client!.ExecuteAsync(action.Type, parameters, cancellationToken).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(resp.Message) && action.Type == "WaitForGrasshopperSolution")
                                _logger.Log($"    {resp.Message}");
                            if (!resp.Success)
                            {
                                result.Status = TestStatus.Crashed;
                                result.ErrorMessage = $"Action '{action.Type}' failed: {resp.Message}";
                                break;
                            }
                        }
                    }

                    if (result.Status != TestStatus.Crashed)
                    {
                        InitVlmProviderIfNeeded(test);

                        var testDir = sharedTestDir;

                        foreach (var checkpoint in test.Checkpoints)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (appDead)
                            {
                                result.Status = TestStatus.Crashed;
                                result.ErrorMessage = "Application crashed during test.";
                                break;
                            }

                            await DispatchClientCheckpointAsync(
                                client!, checkpoint, testDir, captureWidth, captureHeight, result, cancellationToken).ConfigureAwait(false);
                        }

                        if (test.Asserts.Count > 0 && !appDead)
                        {
                            _logger.Log($"[{test.Name}] evaluating {test.Asserts.Count} assert(s)...");
                            foreach (var assert in test.Asserts)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var (ok, message) = await EvaluateClientAssertAsync(client!, assert, cancellationToken).ConfigureAwait(false);
                                if (ok)
                                {
                                    _logger.Log($"  + {assert.Type} {assert.Nickname}");
                                }
                                else
                                {
                                    _logger.Log($"  - {assert.Type} {assert.Nickname}: {message}");
                                    if (result.Status == TestStatus.Passed || result.Status == TestStatus.New)
                                        result.Status = TestStatus.Failed;
                                    result.ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage)
                                        ? $"Assert failed: {message}"
                                        : result.ErrorMessage + $"; {message}";
                                }
                            }
                        }

                        if (result.CheckpointResults.Count > 0)
                        {
                            try
                            {
                                result.CompositeImagePath = await BuildCompositeAsync(result, testDir).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Log($"Warning: composite build failed: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Status = TestStatus.Crashed;
                    result.ErrorMessage = ex.Message;
                }

                result.Duration = sw.Elapsed;
                suite.TestResults.Add(result);
                LogTestStatus(result);
            }
        }
        catch (OperationCanceledException)
        {
            // Pending tests were never started — record them as Crashed.
        }
        finally
        {
            watchdogCts.Cancel();
            if (watchdogTask != null)
            {
                try { await watchdogTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            watchdogCts.Dispose();
            client?.Dispose();
        }

        _logger.LogSummary($"Results: {suite.Passed} passed, {suite.Failed} failed, {suite.Crashed} crashed, {suite.New} new");
        return suite;
    }

    private void LogTestStatus(TestResult result)
    {
        var (symbol, level) = result.Status switch
        {
            TestStatus.Passed => ("PASS", TestStatusLevel.Pass),
            TestStatus.Failed => ("FAIL", TestStatusLevel.Fail),
            TestStatus.Crashed => ("CRASH", TestStatusLevel.Crash),
            TestStatus.New => ("NEW", TestStatusLevel.New),
            _ => ("???", TestStatusLevel.Info)
        };
        var hasVlm = result.CheckpointResults.Any(c => c.VlmDescription != null);
        var maxDiff = result.CheckpointResults.Count > 0
            ? result.CheckpointResults.Max(c => c.DiffPercentage)
            : 0;
        var statusDetail = hasVlm && maxDiff == 0
            ? $"{result.TestName} (VLM)"
            : $"{result.TestName} ({maxDiff:P1} max diff)";
        _logger.LogStatus(symbol, statusDetail, level);
        if (!string.IsNullOrEmpty(result.ErrorMessage))
            _logger.Log($"  Error: {result.ErrorMessage}");
    }

    /// <summary>
    /// Run a test using an in-process ICanaryAgent (e.g., PenumbraBridgeAgent).
    /// The agent must already be initialized. The caller is responsible for disposal.
    /// Used for CDP-based agents that manage their own app lifecycle.
    /// </summary>
    public async Task<TestResult> RunAgentTestAsync(
        TestDefinition testDef,
        WorkloadConfig workload,
        ICanaryAgent agent,
        CancellationToken cancellationToken,
        string? suiteName = null)
    {
        var sw = Stopwatch.StartNew();
        var startedUtc = DateTime.UtcNow;
        string? testDirSaved = null;
        var result = new TestResult
        {
            TestName = testDef.Name,
            Workload = workload.Name,
            Status = TestStatus.Passed
        };

        Progress.OnTestStarted(testDef.Name);

        // Pre-create + emit testDir before heartbeat/setup so manual captures
        // during the early test phase route correctly. Phase 4.6.E.A.3.
        {
            var earlyDir = GetTestDirectory(workload.Name, testDef.Name, suiteName);
            Directory.CreateDirectory(earlyDir);
            testDirSaved = earlyDir;
            Progress?.OnTestDirectoryReady(testDef.Name, earlyDir);
        }

        try
        {
            // 1. Verify heartbeat
            _logger.Log("Sending heartbeat...");
            var hb = await agent.HeartbeatAsync().ConfigureAwait(false);
            _logger.Log($"Heartbeat: ok={hb.Ok}");
            if (!hb.Ok)
            {
                result.Status = TestStatus.Crashed;
                result.ErrorMessage = "Agent heartbeat returned ok=false.";
                result.Duration = sw.Elapsed;
                return result;
            }

            // 2. Send setup commands via agent
            if (testDef.Setup != null)
            {
                _logger.Log("Sending setup commands...");
                await SendAgentSetupAsync(agent, testDef.Setup, cancellationToken).ConfigureAwait(false);
                _logger.Log("Setup commands complete.");
            }

            // 2b. Run pre-checkpoint actions (Phase 13.2 — CPig workload).
            // Each action's `type` is dispatched directly to the agent;
            // failures abort the test.
            if (testDef.Actions.Count > 0)
            {
                _logger.Log($"Running {testDef.Actions.Count} pre-checkpoint action(s)...");
                foreach (var action in testDef.Actions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var parameters = action.AsParameters();
                    _logger.Log($"  action: {action.Type}");
                    var resp = await agent.ExecuteAsync(action.Type, parameters).ConfigureAwait(false);
                    if (!resp.Success)
                    {
                        result.Status = TestStatus.Crashed;
                        result.ErrorMessage = $"Action '{action.Type}' failed: {resp.Message}";
                        result.Duration = sw.Elapsed;
                        return result;
                    }
                }
                _logger.Log("Actions complete.");
            }

            // 2c. Initialize VLM provider if any checkpoint uses vlm mode
            InitVlmProviderIfNeeded(testDef);

            // 3. Process checkpoints with camera positioning — testDir was pre-created above.
            var testDir = testDirSaved!;

            var captureWidth = testDef.Setup?.Canvas?.Width ?? 960;
            var captureHeight = testDef.Setup?.Canvas?.Height ?? 540;

            foreach (var checkpoint in testDef.Checkpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Set camera if checkpoint specifies one
                if (checkpoint.Camera != null)
                {
                    _logger.Log($"Setting camera: az={checkpoint.Camera.Azimuth}, el={checkpoint.Camera.Elevation}, dist={checkpoint.Camera.Distance}");
                    await agent.ExecuteAsync("SetCamera", new Dictionary<string, string>
                    {
                        ["azimuth"] = checkpoint.Camera.Azimuth.ToString(),
                        ["elevation"] = checkpoint.Camera.Elevation.ToString(),
                        ["distance"] = checkpoint.Camera.Distance.ToString(),
                        ["stabilizeMs"] = (checkpoint.StabilizeMs ?? 500).ToString()
                    }).ConfigureAwait(false);
                }

                await DispatchAgentCheckpointAsync(
                    agent, checkpoint, testDef.Name, testDir, captureWidth, captureHeight, result, cancellationToken).ConfigureAwait(false);
            }

            // 3b. Evaluate asserts after the last checkpoint (Phase 13.2).
            // Even if pixel diff passed, an assert failure flips the test to Failed.
            if (testDef.Asserts.Count > 0)
            {
                _logger.Log($"Evaluating {testDef.Asserts.Count} assert(s)...");
                foreach (var assert in testDef.Asserts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (ok, message) = await EvaluateAssertAsync(agent, assert, cancellationToken).ConfigureAwait(false);
                    if (ok)
                    {
                        _logger.Log($"  + {assert.Type} {assert.Nickname}");
                    }
                    else
                    {
                        _logger.Log($"  - {assert.Type} {assert.Nickname}: {message}");
                        if (result.Status == TestStatus.Passed || result.Status == TestStatus.New)
                            result.Status = TestStatus.Failed;
                        result.ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage)
                            ? $"Assert failed: {message}"
                            : result.ErrorMessage + $"; {message}";
                    }
                }
            }

            // 4. Build composite image
            if (result.CheckpointResults.Count > 0)
            {
                try
                {
                    result.CompositeImagePath = await BuildCompositeAsync(result, testDir).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Log($"Warning: Failed to build composite image: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Status = TestStatus.Crashed;
            result.ErrorMessage = "Test cancelled by user.";
        }
        catch (Exception ex)
        {
            result.Status = TestStatus.Crashed;
            result.ErrorMessage = ex.Message;
        }

        result.Duration = sw.Elapsed;
        if (testDirSaved != null)
            await SavePerRunArtifactsAsync(result, workload, testDirSaved, startedUtc, DateTime.UtcNow).ConfigureAwait(false);
        Progress?.OnTestCompleted(testDef.Name, result.Status, sw.Elapsed.TotalSeconds);
        return result;
    }

    /// <summary>
    /// Run all tests for a workload using an in-process agent.
    /// The agent must already be initialized. Caller is responsible for disposal.
    /// </summary>
    public async Task<SuiteResult> RunAgentSuiteAsync(
        WorkloadConfig workload,
        IReadOnlyList<TestDefinition> tests,
        ICanaryAgent agent,
        CancellationToken cancellationToken,
        string? suiteName = null)
    {
        var suite = new SuiteResult();

        foreach (var test in tests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await RunAgentTestAsync(test, workload, agent, cancellationToken, suiteName).ConfigureAwait(false);
            suite.TestResults.Add(result);

            var (symbol, level) = result.Status switch
            {
                TestStatus.Passed => ("PASS", TestStatusLevel.Pass),
                TestStatus.Failed => ("FAIL", TestStatusLevel.Fail),
                TestStatus.Crashed => ("CRASH", TestStatusLevel.Crash),
                TestStatus.New => ("NEW", TestStatusLevel.New),
                _ => ("???", TestStatusLevel.Info)
            };

            var hasVlm = result.CheckpointResults.Any(c => c.VlmDescription != null);
            var maxDiff = result.CheckpointResults.Count > 0
                ? result.CheckpointResults.Max(c => c.DiffPercentage)
                : 0;

            var statusDetail = hasVlm && maxDiff == 0
                ? $"{result.TestName} (VLM)"
                : $"{result.TestName} ({maxDiff:P1} max diff)";
            _logger.LogStatus(symbol, statusDetail, level);

            if (_logger.Verbose)
            {
                foreach (var cp in result.CheckpointResults)
                {
                    var cpSymbol = cp.Status == TestStatus.Passed ? "  +" : "  -";
                    if (cp.VlmDescription != null)
                        _logger.Log($"{cpSymbol} {cp.Name}: VLM conf={cp.VlmConfidence:F2} — {Truncate(cp.VlmReasoning ?? "")}");
                    else
                        _logger.Log($"{cpSymbol} {cp.Name}: diff={cp.DiffPercentage:P2}, ssim={cp.SsimScore:F4}, tol={cp.Tolerance:P2}");
                    if (!string.IsNullOrEmpty(cp.ErrorMessage))
                        _logger.Log($"    Error: {cp.ErrorMessage}");
                }
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage))
                _logger.Log($"  Error: {result.ErrorMessage}");
        }

        _logger.LogSummary($"Results: {suite.Passed} passed, {suite.Failed} failed, {suite.Crashed} crashed, {suite.New} new");
        return suite;
    }

    /// <summary>
    /// Phase 14.7 — per-checkpoint viewport override. When the checkpoint
    /// carries its own <see cref="TestCheckpoint.Viewport"/>, switch the
    /// active viewport via SetViewport just before capture and settle briefly.
    /// Pairs with the new 4-views-per-test pattern: four checkpoints in one
    /// test, each pinned to Front / Top / Right / Perspective.
    /// </summary>
    private static Dictionary<string, string>? BuildViewportParams(ViewportSetup? viewport)
    {
        if (viewport == null) return null;
        var vparams = new Dictionary<string, string>
        {
            ["projection"] = viewport.Projection,
            ["displayMode"] = viewport.DisplayMode
        };
        if (viewport.Width > 0) vparams["width"] = viewport.Width.ToString();
        if (viewport.Height > 0) vparams["height"] = viewport.Height.ToString();
        return vparams;
    }

    private async Task ApplyCheckpointViewportAsync(ICanaryAgent agent, TestCheckpoint checkpoint, CancellationToken ct)
    {
        var vparams = BuildViewportParams(checkpoint.Viewport);
        if (vparams == null) return;
        _logger.Log($"  switching viewport for checkpoint '{checkpoint.Name}': {vparams["projection"]} / {vparams["displayMode"]}");
        await agent.ExecuteAsync("SetViewport", vparams).ConfigureAwait(false);
        await Task.Delay(250, ct).ConfigureAwait(false);
    }

    private async Task ApplyCheckpointViewportAsync(HarnessClient client, TestCheckpoint checkpoint, CancellationToken ct)
    {
        var vparams = BuildViewportParams(checkpoint.Viewport);
        if (vparams == null) return;
        _logger.Log($"  switching viewport for checkpoint '{checkpoint.Name}': {vparams["projection"]} / {vparams["displayMode"]}");
        await client.ExecuteAsync("SetViewport", vparams, ct).ConfigureAwait(false);
        await Task.Delay(250, ct).ConfigureAwait(false);
    }

    private async Task SendAgentSetupAsync(ICanaryAgent agent, TestSetup setup, CancellationToken ct)
    {
        // Set canvas size if specified
        if (setup.Canvas != null)
        {
            await agent.ExecuteAsync("SetCanvasSize", new Dictionary<string, string>
            {
                ["width"] = setup.Canvas.Width.ToString(),
                ["height"] = setup.Canvas.Height.ToString()
            }).ConfigureAwait(false);
        }

        // Set backend if specified
        if (!string.IsNullOrWhiteSpace(setup.Backend))
        {
            await agent.ExecuteAsync("SetBackend", new Dictionary<string, string>
            {
                ["backend"] = setup.Backend
            }).ConfigureAwait(false);
        }

        // Load scene if specified — prefer SceneName (backend-independent)
        // over Index because the scene array differs between WebGL2 and WebGPU.
        if (setup.Scene != null)
        {
            if (!string.IsNullOrWhiteSpace(setup.Scene.SceneName))
            {
                await agent.ExecuteAsync("LoadSceneByName", new Dictionary<string, string>
                {
                    ["name"] = setup.Scene.SceneName
                }).ConfigureAwait(false);
            }
            else
            {
                await agent.ExecuteAsync("LoadScene", new Dictionary<string, string>
                {
                    ["index"] = setup.Scene.Index.ToString()
                }).ConfigureAwait(false);
            }
        }

        // Apply Penumbra display preset (Penumbra ADR 0011 / spec/PENUMBRA_WORKLOAD.md).
        // Resolves the named preset and dispatches LoadDisplayPreset on the agent.
        // The Penumbra-side handler (PenumbraBridgeAgent) calls
        // pass.loadDisplayPreset(name) which in turn merges the preset into
        // the renderer's DisplayState. Unknown names log a warning + no-op.
        if (!string.IsNullOrWhiteSpace(setup.DisplayPreset))
        {
            _logger.Log($"Applying display preset: {setup.DisplayPreset}");
            var presetResult = await agent.ExecuteAsync("LoadDisplayPreset", new Dictionary<string, string>
            {
                ["name"] = setup.DisplayPreset
            }).ConfigureAwait(false);
            _logger.Log($"  → {presetResult.Message}");
        }

        // Run setup commands
        foreach (var cmd in setup.Commands)
        {
            _logger.Log($"Running: {cmd}");
            var cmdResult = await agent.ExecuteAsync("RunCommand", new Dictionary<string, string>
            {
                ["command"] = cmd
            }).ConfigureAwait(false);
            _logger.Log($"  → {cmdResult.Message}");
        }

        // Let the app settle after setup
        await Task.Delay(1000, ct).ConfigureAwait(false);
    }

    private async Task<CheckpointResult> ProcessAgentCheckpointAsync(
        ICanaryAgent agent,
        TestCheckpoint checkpoint,
        string testName,
        string testDir,
        int captureWidth,
        int captureHeight,
        CancellationToken ct,
        CheckpointMode? forceMode = null)
    {
        var displayName = forceMode == CheckpointMode.Vlm && checkpoint.Mode != "vlm"
            ? $"{checkpoint.Name}-vlm"
            : checkpoint.Name;
        var cpResult = new CheckpointResult
        {
            Name = displayName,
            Tolerance = checkpoint.Tolerance
        };

        Progress.OnCheckpointStarted(testName, displayName, checkpoint.Description);

        try
        {
            var candidatePath = Path.Combine(testDir, "candidates", $"{checkpoint.Name}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);

            if (string.Equals(checkpoint.Source, "file", StringComparison.OrdinalIgnoreCase))
            {
                // File-source checkpoint: resolve a path from a GH panel, or a literal FilePath.
                string filePath;
                if (!string.IsNullOrWhiteSpace(checkpoint.PanelNickname))
                {
                    _logger.Log($"Reading file path from panel '{checkpoint.PanelNickname}'...");
                    var panelResp = await agent.ExecuteAsync("GrasshopperGetPanelText",
                        new Dictionary<string, string> { ["nickname"] = checkpoint.PanelNickname }).ConfigureAwait(false);
                    if (!panelResp.Success)
                    {
                        cpResult.Status = TestStatus.Crashed;
                        cpResult.ErrorMessage = $"Failed to read panel '{checkpoint.PanelNickname}': {panelResp.Message}";
                        return cpResult;
                    }
                    filePath = (panelResp.Data != null && panelResp.Data.TryGetValue("text", out var t) ? t : string.Empty).Trim();
                }
                else if (!string.IsNullOrWhiteSpace(checkpoint.FilePath))
                {
                    filePath = Environment.ExpandEnvironmentVariables(checkpoint.FilePath).Trim();
                }
                else
                {
                    cpResult.Status = TestStatus.Crashed;
                    cpResult.ErrorMessage = "File-source checkpoint requires 'panelNickname' or 'filePath'.";
                    return cpResult;
                }
                _logger.Log($"File-source path: {filePath}");

                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    cpResult.Status = TestStatus.Crashed;
                    cpResult.ErrorMessage = $"File-source path does not exist: '{filePath}'";
                    return cpResult;
                }

                File.Copy(filePath, candidatePath, overwrite: true);
                cpResult.CandidatePath = candidatePath;
                _logger.Log($"Copied render output to {candidatePath}");
            }
            else
            {
                // Default: viewport capture + full-screen sibling (Phase 4.6.E.A.2 —
                // catches warning balloons / modal toasts that the viewport-only capture misses).
                // Phase 4.6.F Session B: optional GIF capture. Two paths:
                //   (a) Scrub == null  → agent-side timer loop (N frames of whatever the
                //       viewport shows; useful only if the viewport changes on its own).
                //   (b) Scrub != null  → orchestrator-driven loop here (set slider →
                //       wait for solve → single-frame capture, repeat). True animated GIF.
                _logger.Log($"Capturing checkpoint: {checkpoint.Name}");
                await ApplyCheckpointViewportAsync(agent, checkpoint, ct).ConfigureAwait(false);
                // Per-checkpoint stabilize wait (any source / any agent). Previously stabilizeMs
                // was only forwarded as a parameter to SetCamera (above, line ~778) and was
                // silently dropped for non-Camera checkpoints — meaning bumping stabilizeMs in
                // the test JSON had ZERO effect on the actual capture for ICanaryAgent-driven
                // tests (Rhino, etc.). Critical for progressive-quality renderers like the
                // Penumbra GLSL conduit: the post-action capture would fire immediately while
                // the conduit was still at motion-mode 0.25× resolution, so atoms with thin
                // features randomly disappeared from the capture even though they rendered fine
                // interactively. During this Task.Delay the agent sends no JSON-RPC, so Rhino's
                // message loop is free to fire RhinoApp.Idle events → the conduit's progressive
                // controller ramps to 1.0× and converges before the capture grabs the frame.
                if (checkpoint.StabilizeMs.HasValue && checkpoint.StabilizeMs.Value > 0)
                {
                    _logger.Log($"  stabilizing {checkpoint.StabilizeMs.Value}ms before capture...");
                    await Task.Delay(checkpoint.StabilizeMs.Value, ct).ConfigureAwait(false);
                }
                var gifEnabled = checkpoint.Capture?.Gif == true;
                var scrub = checkpoint.Capture?.Scrub;
                var gifFrames = checkpoint.Capture?.FrameCount ?? 30;
                var gifInterval = checkpoint.Capture?.IntervalMs ?? 100;
                // Disable the agent-side timer loop when scrub is taking over.
                var agentSideGif = gifEnabled && (scrub == null || scrub.Values.Length == 0);
                var captureResult = await agent.CaptureScreenshotAsync(new CaptureSettings
                {
                    Width = captureWidth,
                    Height = captureHeight,
                    OutputPath = candidatePath,
                    IncludeFullScreen = true,
                    RecordGif = agentSideGif,
                    GifFrameCount = gifFrames,
                    GifFrameIntervalMs = gifInterval
                }).ConfigureAwait(false);

                cpResult.CandidatePath = captureResult.FilePath;
                if (!string.IsNullOrEmpty(captureResult.FullScreenPath))
                    _logger.Log($"  + full-screen capture: {captureResult.FullScreenPath}");

                if (agentSideGif && captureResult.FramePaths.Count > 0)
                {
                    cpResult.GifPath = EncodeGifAndCleanup(captureResult, candidatePath, gifInterval);
                }
                else if (gifEnabled && scrub != null && scrub.Values.Length > 0)
                {
                    var scrubFrames = await ScrubAndCaptureFramesAsync(
                        (a, p) => agent.ExecuteAsync(a, p),
                        s => agent.CaptureScreenshotAsync(s),
                        scrub,
                        candidatePath,
                        captureWidth, captureHeight,
                        ct).ConfigureAwait(false);
                    if (scrubFrames.Count > 0)
                        cpResult.GifPath = EncodeGifFromFrames(scrubFrames, candidatePath, gifInterval);
                    CleanupFrames(scrubFrames);
                }
            }

            if (cpResult.CandidatePath != null)
                Progress.OnScreenshotCaptured(testName, displayName, cpResult.CandidatePath);
            if (cpResult.GifPath != null)
                Progress.OnGifCaptured(testName, displayName, cpResult.GifPath);

            // Branch on comparison mode. forceMode (set by ResolveEffectiveModes)
            // wins over checkpoint.Mode. forceMode == null preserves the original
            // per-checkpoint behaviour for backwards compatibility.
            var effective = forceMode
                ?? (IsCaptureOnly(checkpoint)
                    ? CheckpointMode.Capture
                    : string.Equals(checkpoint.Mode, "vlm", StringComparison.OrdinalIgnoreCase)
                        ? CheckpointMode.Vlm
                        : CheckpointMode.PixelDiff);
            if (effective == CheckpointMode.Capture)
            {
                // Capture-only: the screenshot was already saved as the candidate above.
                // Run no comparison (no pixel-diff, no VLM) and produce no failure — this
                // just records the image for manual review.
                cpResult.Status = TestStatus.Passed;
                return cpResult;
            }
            if (effective == CheckpointMode.Vlm)
            {
                return await ProcessVlmCheckpointAsync(cpResult, checkpoint, testName, displayName, ct).ConfigureAwait(false);
            }

            // Default: pixel-diff mode
            var baselinePath = Path.Combine(testDir, "baselines", $"{checkpoint.Name}.png");
            cpResult.BaselinePath = baselinePath;

            if (!File.Exists(baselinePath))
            {
                cpResult.Status = TestStatus.New;
                cpResult.ErrorMessage = "No baseline exists. Run 'canary approve' to establish.";
                return cpResult;
            }

            // Compare
            using var baseline = await Image.LoadAsync<Rgba32>(baselinePath, ct).ConfigureAwait(false);
            using var candidate = await Image.LoadAsync<Rgba32>(cpResult.CandidatePath!, ct).ConfigureAwait(false);

            if (baseline.Width != candidate.Width || baseline.Height != candidate.Height)
            {
                cpResult.Status = TestStatus.Failed;
                cpResult.ErrorMessage = $"Viewport size mismatch: baseline {baseline.Width}x{baseline.Height}, candidate {candidate.Width}x{candidate.Height}";
                return cpResult;
            }

            using var compResult = _pixelDiff.Compare(baseline, candidate, colorThreshold: 3, tolerance: checkpoint.Tolerance);
            cpResult.DiffPercentage = compResult.DiffPercentage;
            cpResult.Status = compResult.Passed ? TestStatus.Passed : TestStatus.Failed;

            // Save diff image
            if (compResult.DiffImage != null)
            {
                var diffPath = Path.Combine(testDir, "diffs", $"{checkpoint.Name}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
                await compResult.DiffImage.SaveAsPngAsync(diffPath, ct).ConfigureAwait(false);
                cpResult.DiffImagePath = diffPath;
            }

            // Compute SSIM (secondary, logged but not gating)
            cpResult.SsimScore = _ssim.ComputeSsim(baseline, candidate);
        }
        catch (Exception ex)
        {
            cpResult.Status = TestStatus.Crashed;
            cpResult.ErrorMessage = ex.Message;
        }

        return cpResult;
    }

    private async Task SendSetupCommandsAsync(
        HarnessClient client, TestSetup setup, WorkloadConfig workload, CancellationToken ct)
    {
        // Open file if specified. Dispatch by extension: .3dm and friends go
        // through OpenFile (Rhino doc), .gh through OpenGrasshopperDefinition.
        if (!string.IsNullOrWhiteSpace(setup.File))
        {
            // Resolve relative paths against the workload directory so the
            // Rhino agent receives an absolute path it can open.
            var resolvedPath = setup.File;
            if (!Path.IsPathRooted(resolvedPath))
            {
                resolvedPath = Path.Combine(_workloadsDir, workload.Name, setup.File);
            }
            var ext = Path.GetExtension(resolvedPath).ToLowerInvariant();
            _logger.Log($"Opening file: {resolvedPath}");
            if (ext == ".gh" || ext == ".ghx")
            {
                await client.ExecuteAsync("OpenGrasshopperDefinition", new Dictionary<string, string>
                {
                    ["path"] = resolvedPath
                }, ct).ConfigureAwait(false);
            }
            else
            {
                await client.ExecuteAsync("OpenFile", new Dictionary<string, string>
                {
                    ["path"] = resolvedPath
                }, ct).ConfigureAwait(false);
            }
        }

        // Set viewport projection/display mode if specified (size is handled by WindowPositioner)
        if (setup.Viewport != null)
        {
            var vparams = new Dictionary<string, string>
            {
                ["projection"] = setup.Viewport.Projection,
                ["displayMode"] = setup.Viewport.DisplayMode
            };
            // Only send explicit size if the test definition specifies non-zero values
            if (setup.Viewport.Width > 0)
                vparams["width"] = setup.Viewport.Width.ToString();
            if (setup.Viewport.Height > 0)
                vparams["height"] = setup.Viewport.Height.ToString();
            await client.ExecuteAsync("SetViewport", vparams, ct).ConfigureAwait(false);
        }

        // Run setup commands
        foreach (var cmd in setup.Commands)
        {
            _logger.Log($"Running: {cmd}");
            await client.ExecuteAsync("RunCommand", new Dictionary<string, string>
            {
                ["command"] = cmd
            }, ct).ConfigureAwait(false);
        }

        // Let the app settle after setup (render, redraw, etc.)
        await Task.Delay(2000, ct).ConfigureAwait(false);
    }

    private async Task<CheckpointResult> ProcessCheckpointAsync(
        HarnessClient client,
        TestCheckpoint checkpoint,
        string testDir,
        int captureWidth,
        int captureHeight,
        CancellationToken ct,
        CheckpointMode? forceMode = null)
    {
        // When forceMode is provided, append a suffix to the checkpoint name
        // so two-mode runs (override=Both) emit distinguishable result rows.
        // No suffix is added if the test only ran in a single mode.
        var displayName = forceMode == CheckpointMode.Vlm && checkpoint.Mode != "vlm"
            ? $"{checkpoint.Name}-vlm"
            : checkpoint.Name;
        var cpResult = new CheckpointResult
        {
            Name = displayName,
            Tolerance = checkpoint.Tolerance
        };

        try
        {
            var candidatePath = Path.Combine(testDir, "candidates", $"{checkpoint.Name}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);

            if (string.Equals(checkpoint.Source, "file", StringComparison.OrdinalIgnoreCase))
            {
                // File-source checkpoint: resolve a path from a GH panel, or a literal FilePath.
                string filePath;
                if (!string.IsNullOrWhiteSpace(checkpoint.PanelNickname))
                {
                    _logger.Log($"Reading file path from panel '{checkpoint.PanelNickname}'...");
                    var panelResp = await client.ExecuteAsync("GrasshopperGetPanelText",
                        new Dictionary<string, string> { ["nickname"] = checkpoint.PanelNickname }, ct).ConfigureAwait(false);
                    if (!panelResp.Success)
                    {
                        cpResult.Status = TestStatus.Crashed;
                        cpResult.ErrorMessage = $"Failed to read panel '{checkpoint.PanelNickname}': {panelResp.Message}";
                        return cpResult;
                    }
                    filePath = (panelResp.Data != null && panelResp.Data.TryGetValue("text", out var t) ? t : string.Empty).Trim();
                }
                else if (!string.IsNullOrWhiteSpace(checkpoint.FilePath))
                {
                    filePath = Environment.ExpandEnvironmentVariables(checkpoint.FilePath).Trim();
                }
                else
                {
                    cpResult.Status = TestStatus.Crashed;
                    cpResult.ErrorMessage = "File-source checkpoint requires 'panelNickname' or 'filePath'.";
                    return cpResult;
                }
                _logger.Log($"File-source path: {filePath}");

                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    cpResult.Status = TestStatus.Crashed;
                    cpResult.ErrorMessage = $"File-source path does not exist: '{filePath}'";
                    return cpResult;
                }

                File.Copy(filePath, candidatePath, overwrite: true);
                cpResult.CandidatePath = candidatePath;
                _logger.Log($"Copied render output to {candidatePath}");
            }
            else
            {
                // Default: viewport capture + full-screen sibling (Phase 4.6.E.A.2 —
                // catches warning balloons / modal toasts that the viewport-only capture misses).
                // Phase 4.6.F Session B: optional GIF capture (see in-process branch above for
                // the agent-side-loop vs orchestrator-driven scrub split).
                _logger.Log($"Capturing checkpoint: {checkpoint.Name}");
                await ApplyCheckpointViewportAsync(client, checkpoint, ct).ConfigureAwait(false);
                // Per-checkpoint stabilize wait (parallel of the ICanaryAgent path above) —
                // see the comment there for the full rationale. HarnessClient path (Penumbra
                // web workload + Pigture). The stabilize is just as load-bearing here: any
                // progressive renderer (WebGPU compute marcher, Cycles, etc.) needs idle time
                // before the capture grabs a converged frame.
                if (checkpoint.StabilizeMs.HasValue && checkpoint.StabilizeMs.Value > 0)
                {
                    _logger.Log($"  stabilizing {checkpoint.StabilizeMs.Value}ms before capture...");
                    await Task.Delay(checkpoint.StabilizeMs.Value, ct).ConfigureAwait(false);
                }
                var gifEnabled = checkpoint.Capture?.Gif == true;
                var scrub = checkpoint.Capture?.Scrub;
                var gifFrames = checkpoint.Capture?.FrameCount ?? 30;
                var gifInterval = checkpoint.Capture?.IntervalMs ?? 100;
                var agentSideGif = gifEnabled && (scrub == null || scrub.Values.Length == 0);
                var captureResult = await client.CaptureScreenshotAsync(new CaptureSettings
                {
                    Width = captureWidth,
                    Height = captureHeight,
                    OutputPath = candidatePath,
                    IncludeFullScreen = true,
                    RecordGif = agentSideGif,
                    GifFrameCount = gifFrames,
                    GifFrameIntervalMs = gifInterval
                }, ct).ConfigureAwait(false);

                cpResult.CandidatePath = captureResult.FilePath;
                if (!string.IsNullOrEmpty(captureResult.FullScreenPath))
                    _logger.Log($"  + full-screen capture: {captureResult.FullScreenPath}");

                if (agentSideGif && captureResult.FramePaths.Count > 0)
                {
                    cpResult.GifPath = EncodeGifAndCleanup(captureResult, candidatePath, gifInterval);
                }
                else if (gifEnabled && scrub != null && scrub.Values.Length > 0)
                {
                    var scrubFrames = await ScrubAndCaptureFramesAsync(
                        (a, p) => client.ExecuteAsync(a, p, ct),
                        s => client.CaptureScreenshotAsync(s, ct),
                        scrub,
                        candidatePath,
                        captureWidth, captureHeight,
                        ct).ConfigureAwait(false);
                    if (scrubFrames.Count > 0)
                        cpResult.GifPath = EncodeGifFromFrames(scrubFrames, candidatePath, gifInterval);
                    CleanupFrames(scrubFrames);
                }
            }

            // Branch on comparison mode. forceMode (set by ResolveEffectiveModes)
            // wins over checkpoint.Mode. forceMode == null preserves the original
            // per-checkpoint behaviour for backwards compatibility.
            var effective = forceMode
                ?? (IsCaptureOnly(checkpoint)
                    ? CheckpointMode.Capture
                    : string.Equals(checkpoint.Mode, "vlm", StringComparison.OrdinalIgnoreCase)
                        ? CheckpointMode.Vlm
                        : CheckpointMode.PixelDiff);
            if (effective == CheckpointMode.Capture)
            {
                // Capture-only: the screenshot was already saved as the candidate above.
                // Run no comparison (no pixel-diff, no VLM) and produce no failure — this
                // just records the image for manual review.
                cpResult.Status = TestStatus.Passed;
                return cpResult;
            }
            if (effective == CheckpointMode.Vlm)
            {
                // Client (named-pipe) path doesn't track testName here; pass "" so events still fire.
                return await ProcessVlmCheckpointAsync(cpResult, checkpoint, "", displayName, ct).ConfigureAwait(false);
            }

            // Default: pixel-diff mode
            var baselinePath = Path.Combine(testDir, "baselines", $"{checkpoint.Name}.png");
            cpResult.BaselinePath = baselinePath;

            if (!File.Exists(baselinePath))
            {
                cpResult.Status = TestStatus.New;
                cpResult.ErrorMessage = "No baseline exists. Run 'canary approve' to establish.";
                return cpResult;
            }

            // Compare
            using var baseline = await Image.LoadAsync<Rgba32>(baselinePath, ct).ConfigureAwait(false);
            using var candidate = await Image.LoadAsync<Rgba32>(cpResult.CandidatePath!, ct).ConfigureAwait(false);

            if (baseline.Width != candidate.Width || baseline.Height != candidate.Height)
            {
                cpResult.Status = TestStatus.Failed;
                cpResult.ErrorMessage = $"Viewport size mismatch: baseline {baseline.Width}x{baseline.Height}, candidate {candidate.Width}x{candidate.Height}";
                return cpResult;
            }

            using var compResult = _pixelDiff.Compare(baseline, candidate, colorThreshold: 3, tolerance: checkpoint.Tolerance);
            cpResult.DiffPercentage = compResult.DiffPercentage;
            cpResult.Status = compResult.Passed ? TestStatus.Passed : TestStatus.Failed;

            // Save diff image
            if (compResult.DiffImage != null)
            {
                var diffPath = Path.Combine(testDir, "diffs", $"{checkpoint.Name}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
                await compResult.DiffImage.SaveAsPngAsync(diffPath, ct).ConfigureAwait(false);
                cpResult.DiffImagePath = diffPath;
            }

            // Compute SSIM (secondary, logged but not gating)
            cpResult.SsimScore = _ssim.ComputeSsim(baseline, candidate);
        }
        catch (Exception ex)
        {
            cpResult.Status = TestStatus.Crashed;
            cpResult.ErrorMessage = ex.Message;
        }

        return cpResult;
    }

    /// <summary>
    /// Lazily initialize the VLM provider if the test definition has any vlm-mode
    /// checkpoints and a provider hasn't been created yet. Also stashes the
    /// test's <c>setup.vlmDescription</c> default so it can be used when a
    /// VLM checkpoint doesn't carry its own <c>description</c>.
    /// </summary>
    private void InitVlmProviderIfNeeded(TestDefinition testDef)
    {
        // Stash the per-test default description regardless of provider state.
        _currentVlmDescription = testDef.Setup?.VlmDescription;

        if (_vlmProvider != null) return;

        bool checkpointWantsVlm = testDef.Checkpoints.Any(c =>
            string.Equals(c.Mode, "vlm", StringComparison.OrdinalIgnoreCase));
        bool overrideWantsVlm = ModeOverride == ModeOverride.Vlm || ModeOverride == ModeOverride.Both;
        if (!checkpointWantsVlm && !overrideWantsVlm) return;

        _vlmConfig = testDef.Setup?.Vlm ?? new VlmConfig();
        try
        {
            _vlmProvider = VlmEvaluator.Create(_vlmConfig);
            _logger.Log($"VLM provider initialized: {_vlmConfig.Provider}/{_vlmConfig.Model}");
        }
        catch (Exception ex)
        {
            _logger.Log($"Warning: VLM provider init failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolve the effective comparison mode(s) for a checkpoint, applying
    /// the precedence rule: per-checkpoint <c>mode = "vlm"</c> wins, then
    /// <see cref="ModeOverride"/> applies, otherwise pixel-diff.
    /// Returns 1 or 2 modes (2 only when override is <see cref="ModeOverride.Both"/>
    /// and the checkpoint isn't already explicit).
    /// </summary>
    private IReadOnlyList<CheckpointMode> ResolveEffectiveModes(TestCheckpoint checkpoint)
    {
        // Capture-only wins over everything — even a --mode override: the checkpoint is
        // opted out of both comparisons. Just save the screenshot, produce no verdict.
        if (IsCaptureOnly(checkpoint))
            return new[] { CheckpointMode.Capture };

        bool checkpointIsExplicitVlm = string.Equals(checkpoint.Mode, "vlm", StringComparison.OrdinalIgnoreCase);
        if (checkpointIsExplicitVlm)
            return new[] { CheckpointMode.Vlm };

        return ModeOverride switch
        {
            ModeOverride.Vlm  => new[] { CheckpointMode.Vlm },
            ModeOverride.PixelDiff => new[] { CheckpointMode.PixelDiff },
            ModeOverride.Both => new[] { CheckpointMode.PixelDiff, CheckpointMode.Vlm },
            _ => new[] { CheckpointMode.PixelDiff },
        };
    }

    /// <summary>
    /// True when a checkpoint opts out of comparison entirely (<c>mode = "capture"</c>,
    /// or the aliases "none"/"off"). Such checkpoints only save the candidate image.
    /// </summary>
    private static bool IsCaptureOnly(TestCheckpoint checkpoint) =>
        string.Equals(checkpoint.Mode, "capture", StringComparison.OrdinalIgnoreCase)
        || string.Equals(checkpoint.Mode, "none", StringComparison.OrdinalIgnoreCase)
        || string.Equals(checkpoint.Mode, "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Roll the rolling test-level status forward based on a single
    /// checkpoint result. Centralises the same Failed/Crashed/New escalation
    /// logic that every call site duplicated before <see cref="ModeOverride"/>
    /// expanded the model from "1 result per checkpoint" to "1 or 2".
    /// </summary>
    private static void EscalateStatus(TestResult result, CheckpointResult cpResult)
    {
        if (cpResult.Status == TestStatus.Failed)
            result.Status = TestStatus.Failed;
        else if (cpResult.Status == TestStatus.Crashed)
            result.Status = TestStatus.Crashed;
        else if (cpResult.Status == TestStatus.New && result.Status == TestStatus.Passed)
            result.Status = TestStatus.New;
    }

    /// <summary>
    /// Run <see cref="ProcessCheckpointAsync"/> once per resolved effective mode
    /// (1 or 2 invocations), append each result to <paramref name="result"/>,
    /// and roll the test-level status forward.
    /// </summary>
    private async Task DispatchClientCheckpointAsync(
        HarnessClient client, TestCheckpoint checkpoint, string testDir,
        int captureWidth, int captureHeight, TestResult result, CancellationToken ct)
    {
        foreach (var mode in ResolveEffectiveModes(checkpoint))
        {
            var cpResult = await ProcessCheckpointAsync(
                client, checkpoint, testDir, captureWidth, captureHeight, ct, mode).ConfigureAwait(false);
            result.CheckpointResults.Add(cpResult);
            EscalateStatus(result, cpResult);
        }
    }

    /// <summary>
    /// Agent-flavoured analogue of <see cref="DispatchClientCheckpointAsync"/>.
    /// </summary>
    private async Task DispatchAgentCheckpointAsync(
        ICanaryAgent agent, TestCheckpoint checkpoint, string testName, string testDir,
        int captureWidth, int captureHeight, TestResult result, CancellationToken ct)
    {
        foreach (var mode in ResolveEffectiveModes(checkpoint))
        {
            var cpResult = await ProcessAgentCheckpointAsync(
                agent, checkpoint, testName, testDir, captureWidth, captureHeight, ct, mode).ConfigureAwait(false);
            result.CheckpointResults.Add(cpResult);
            EscalateStatus(result, cpResult);
        }
    }

    /// <summary>
    /// Evaluate a screenshot using the VLM oracle. Shared by both the named-pipe
    /// and in-process agent checkpoint paths.
    /// </summary>
    private async Task<CheckpointResult> ProcessVlmCheckpointAsync(
        CheckpointResult cpResult,
        TestCheckpoint checkpoint,
        string testName,
        string displayName,
        CancellationToken ct)
    {
        // Description precedence: per-checkpoint description (most specific)
        // wins over the test-level setup.vlmDescription default.
        var description = !string.IsNullOrWhiteSpace(checkpoint.Description)
            ? checkpoint.Description
            : (_currentVlmDescription ?? string.Empty);
        cpResult.VlmDescription = description;

        if (_vlmProvider == null)
        {
            cpResult.Status = TestStatus.Crashed;
            cpResult.ErrorMessage = "VLM provider not initialized. Check API key configuration.";
            return cpResult;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            cpResult.Status = TestStatus.Crashed;
            cpResult.ErrorMessage = "VLM checkpoint requires a non-empty 'description' on the checkpoint or 'setup.vlmDescription' on the test.";
            return cpResult;
        }

        if (cpResult.CandidatePath == null || !File.Exists(cpResult.CandidatePath))
        {
            cpResult.Status = TestStatus.Crashed;
            cpResult.ErrorMessage = "Screenshot file not found for VLM evaluation.";
            return cpResult;
        }

        try
        {
            _logger.Log($"VLM evaluating: {description}");
            Progress.OnVlmEvaluating(testName, displayName, description);
            var imageBytes = await File.ReadAllBytesAsync(cpResult.CandidatePath, ct).ConfigureAwait(false);
            var verdict = await _vlmProvider.EvaluateAsync(imageBytes, description, ct).ConfigureAwait(false);

            cpResult.VlmReasoning = verdict.Reasoning;
            cpResult.VlmConfidence = verdict.Confidence;
            cpResult.Status = verdict.Passed ? TestStatus.Passed : TestStatus.Failed;

            _logger.Log($"VLM verdict: {(verdict.Passed ? "PASS" : "FAIL")} (confidence={verdict.Confidence:F2})");
            if (!string.IsNullOrEmpty(verdict.Reasoning))
                _logger.Log($"  Reasoning: {verdict.Reasoning}");
            Progress.OnVlmVerdict(testName, displayName, verdict.Passed, verdict.Confidence, verdict.Reasoning ?? string.Empty);
        }
        catch (Exception ex)
        {
            cpResult.Status = TestStatus.Crashed;
            cpResult.ErrorMessage = $"VLM evaluation failed: {ex.Message}";
            Progress.OnVlmVerdict(testName, displayName, false, 0, $"Crash: {ex.Message}");
        }

        return cpResult;
    }

    private async Task<string?> BuildCompositeAsync(TestResult result, string testDir)
    {
        var comparisons = new List<CheckpointComparison>();
        try
        {
            foreach (var cp in result.CheckpointResults)
            {
                if (cp.BaselinePath == null || cp.CandidatePath == null || !File.Exists(cp.BaselinePath) || !File.Exists(cp.CandidatePath))
                    continue;

                var baseline = await Image.LoadAsync<Rgba32>(cp.BaselinePath).ConfigureAwait(false);
                var candidate = await Image.LoadAsync<Rgba32>(cp.CandidatePath).ConfigureAwait(false);

                Image<Rgba32> diffImage;
                if (cp.DiffImagePath != null && File.Exists(cp.DiffImagePath))
                    diffImage = await Image.LoadAsync<Rgba32>(cp.DiffImagePath).ConfigureAwait(false);
                else
                    diffImage = new Image<Rgba32>(baseline.Width, baseline.Height);

                comparisons.Add(new CheckpointComparison
                {
                    Name = cp.Name,
                    Baseline = baseline,
                    Candidate = candidate,
                    DiffImage = diffImage,
                    Passed = cp.Status == TestStatus.Passed,
                    DiffPercentage = cp.DiffPercentage,
                    Tolerance = cp.Tolerance
                });
            }

            if (comparisons.Count == 0) return null;

            var compositeBuilder = new CompositeBuilder();
            var compositePath = Path.Combine(testDir, "composite.png");
            await compositeBuilder.SaveAsync(comparisons, compositePath).ConfigureAwait(false);
            return compositePath;
        }
        finally
        {
            foreach (var comp in comparisons)
            {
                comp.Baseline.Dispose();
                comp.Candidate.Dispose();
                comp.DiffImage.Dispose();
            }
        }
    }

    private string ResolveRecordingPath(string workloadName, string recordingRef)
    {
        // Try as-is relative to workload directory
        var direct = Path.Combine(_workloadsDir, workloadName, recordingRef);
        if (File.Exists(direct))
            return direct;

        // Try under recordings/ subdirectory
        var inRecordings = Path.Combine(_workloadsDir, workloadName, "recordings", recordingRef);
        if (File.Exists(inRecordings))
            return inRecordings;

        // Return the direct path (caller will report "not found")
        return direct;
    }

    private string GetTestDirectory(string workloadName, string testName, string? suiteName = null)
    {
        if (suiteName != null)
            return Path.Combine(_workloadsDir, workloadName, "results", suiteName, testName);
        return Path.Combine(_workloadsDir, workloadName, "results", testName);
    }

    // Phase 3 / §C2 — per-run dir layout. Writes the run's result.json +
    // REPORT.md into `<testDir>/runs/<timestamp>/`. Baselines, candidates,
    // diffs, composite stay at the test level (overwriting per run) for
    // this phase; a future phase can deepen if past-runs image preservation
    // becomes required.
    //
    // Errors are swallowed + logged — a failed report write must not flip
    // the test verdict.
    private async Task SavePerRunArtifactsAsync(
        TestResult result,
        WorkloadConfig workload,
        string testDir,
        DateTime startedUtc,
        DateTime finishedUtc)
    {
        try
        {
            var runId = GenerateRunId(startedUtc);
            var runDir = Path.Combine(testDir, "runs", runId);
            Directory.CreateDirectory(runDir);

            await TestResultSerializer.SaveAsync(result, Path.Combine(runDir, "result.json")).ConfigureAwait(false);

            await MarkdownReportGenerator.SaveAsync(
                result,
                new MarkdownReportGenerator.ReportOptions
                {
                    RunId = runId,
                    WorkloadDisplayName = workload.DisplayName,
                    WorkloadAgentType = workload.AgentType,
                    Mode = ModeOverride switch
                    {
                        ModeOverride.Vlm => "vlm",
                        ModeOverride.Both => "both",
                        _ => "pixel-diff",
                    },
                    StartedUtc = startedUtc,
                    FinishedUtc = finishedUtc,
                    // From <test>/runs/<timestamp>/REPORT.md, the per-suite
                    // telemetry sink sits three levels up.
                    TelemetryNdjsonRelativePath = "../../../telemetry.ndjson",
                },
                Path.Combine(runDir, "REPORT.md")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Log($"Warning: failed to write per-run artifacts for '{result.TestName}': {ex.Message}");
        }
    }

    private static string GenerateRunId(DateTime startedUtc)
    {
        // Timestamp + 4 hex chars to avoid same-second collisions in suite
        // mode (multiple tests starting within the same second window).
        var rand = (uint)Random.Shared.Next();
        return $"{startedUtc:yyyyMMdd-HHmmss}-{rand:x4}";
    }

    /// <summary>
    /// Evaluate a <see cref="TestAssert"/> by querying the agent and string-comparing.
    /// Returns (success, message). Unknown assert types fail with a typo-pinpointing
    /// message rather than silently passing.
    /// </summary>
    private static async Task<(bool ok, string message)> EvaluateAssertAsync(
        ICanaryAgent agent, TestAssert assert, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assert.Nickname))
            return (false, $"{assert.Type}: missing 'nickname'");

        // All current assert types read a panel; keep the read in one place.
        var resp = await agent.ExecuteAsync("GrasshopperGetPanelText",
            new Dictionary<string, string> { ["nickname"] = assert.Nickname }).ConfigureAwait(false);

        if (!resp.Success)
            return (false, $"GetPanelText('{assert.Nickname}') failed: {resp.Message}");

        string text = resp.Data != null && resp.Data.TryGetValue("text", out var t) ? t : string.Empty;

        return assert.Type switch
        {
            "PanelEquals" => string.Equals(text.Trim(), assert.Text.Trim(), StringComparison.Ordinal)
                ? (true, string.Empty)
                : (false, $"PanelEquals '{assert.Nickname}': expected \"{assert.Text}\", got \"{Truncate(text)}\""),

            "PanelContains" => text.Contains(assert.Text, StringComparison.Ordinal)
                ? (true, string.Empty)
                : (false, $"PanelContains '{assert.Nickname}': \"{assert.Text}\" not found in \"{Truncate(text)}\""),

            "PanelDoesNotContain" => !text.Contains(assert.Text, StringComparison.Ordinal)
                ? (true, string.Empty)
                : (false, $"PanelDoesNotContain '{assert.Nickname}': \"{assert.Text}\" found in panel"),

            _ => (false, $"Unknown assert type '{assert.Type}' (typo? supported: PanelEquals, PanelContains, PanelDoesNotContain)")
        };
    }

    private static string Truncate(string s, int max = 120)
        => s.Length <= max ? s : s.Substring(0, max - 3) + "...";

    /// <summary>
    /// Same as <see cref="EvaluateAssertAsync"/> but for the named-pipe path
    /// (HarnessClient instead of in-process ICanaryAgent). Used by the Rhino
    /// workload's CPig regression tests.
    /// </summary>
    private static async Task<(bool ok, string message)> EvaluateClientAssertAsync(
        HarnessClient client, TestAssert assert, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assert.Nickname))
            return (false, $"{assert.Type}: missing 'nickname'");

        var resp = await client.ExecuteAsync("GrasshopperGetPanelText",
            new Dictionary<string, string> { ["nickname"] = assert.Nickname }, ct).ConfigureAwait(false);

        if (!resp.Success)
            return (false, $"GetPanelText('{assert.Nickname}') failed: {resp.Message}");

        string text = resp.Data != null && resp.Data.TryGetValue("text", out var t) ? t : string.Empty;

        return assert.Type switch
        {
            "PanelEquals" => string.Equals(text.Trim(), assert.Text.Trim(), StringComparison.Ordinal)
                ? (true, string.Empty)
                : (false, $"PanelEquals '{assert.Nickname}': expected \"{assert.Text}\", got \"{Truncate(text)}\""),

            "PanelContains" => text.Contains(assert.Text, StringComparison.Ordinal)
                ? (true, string.Empty)
                : (false, $"PanelContains '{assert.Nickname}': \"{assert.Text}\" not found in \"{Truncate(text)}\""),

            "PanelDoesNotContain" => !text.Contains(assert.Text, StringComparison.Ordinal)
                ? (true, string.Empty)
                : (false, $"PanelDoesNotContain '{assert.Nickname}': \"{assert.Text}\" found in panel"),

            _ => (false, $"Unknown assert type '{assert.Type}' (typo? supported: PanelEquals, PanelContains, PanelDoesNotContain)")
        };
    }

    /// <summary>
    /// Phase 4.6.F Session B helper: encode the captured agent-side frame PNGs into an
    /// animated GIF sibling of <paramref name="candidatePath"/>, delete the intermediate
    /// frame files, and return the GIF path (null if encoding failed). Used by the
    /// agent-side internal frame-grabber path (CaptureSettings.RecordGif=true, no scrub).
    /// </summary>
    private string? EncodeGifAndCleanup(Canary.Agent.ScreenshotResult captureResult, string candidatePath, int intervalMs)
    {
        var gifPath = EncodeGifFromFrames(captureResult.FramePaths, candidatePath, intervalMs);
        CleanupFrames(captureResult.FramePaths);
        return gifPath;
    }

    /// <summary>
    /// Phase 4.6.F Session B+ shared core: encode a list of frame PNG paths into an
    /// animated GIF sibling of <paramref name="candidatePath"/>. Does NOT delete the
    /// frames — the caller decides cleanup policy. <paramref name="intervalMs"/> is the
    /// requested per-frame GIF playback delay in milliseconds (converted to GIF
    /// centiseconds, clamped ≥ 1).
    /// </summary>
    private string? EncodeGifFromFrames(IReadOnlyList<string> framePaths, string candidatePath, int intervalMs)
    {
        if (framePaths == null || framePaths.Count == 0) return null;

        var dir = Path.GetDirectoryName(candidatePath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(candidatePath);
        var gifPath = Path.Combine(dir, baseName + ".gif");

        int delayCs = Math.Max(1, (int)Math.Round(intervalMs / 10.0));
        try
        {
            int encoded = Canary.Comparison.AnimatedGifEncoder.Encode(framePaths, gifPath, delayCs);
            _logger.Log($"  + GIF capture: {encoded} frame(s)  {gifPath}");
        }
        catch (Exception ex)
        {
            _logger.Log($"  ! GIF encoding failed ({framePaths.Count} frames): {ex.Message}");
            return null;
        }

        return File.Exists(gifPath) ? gifPath : null;
    }

    private static void CleanupFrames(IReadOnlyList<string> framePaths)
    {
        foreach (var f in framePaths)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Phase 4.6.F Session B+ orchestrator-driven per-frame slider scrub. For each
    /// value in <paramref name="scrub"/>.Values: drive the named Grasshopper slider,
    /// wait for the canvas to re-solve, optionally settle, then capture a single
    /// frame PNG sibling of <paramref name="candidatePath"/>. Returns the list of
    /// frame paths that were actually written (skipping any frame whose set/wait/
    /// capture failed). The caller assembles + cleans them.
    /// </summary>
    private async Task<List<string>> ScrubAndCaptureFramesAsync(
        Func<string, Dictionary<string, string>, Task<AgentResponse>> executeAsync,
        Func<CaptureSettings, Task<ScreenshotResult>> captureAsync,
        TestCheckpointScrub scrub,
        string candidatePath,
        int width, int height,
        CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(candidatePath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(candidatePath);
        var frames = new List<string>(scrub.Values.Length);
        var solveTimeout = scrub.SolveTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture);

        _logger.Log($"  scrub start: {scrub.Values.Length} frames, slider='{scrub.Nickname}', settle={scrub.SettleMs}ms");
        for (int i = 0; i < scrub.Values.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var v = scrub.Values[i];
            var sliderResp = await executeAsync("GrasshopperSetSlider", new Dictionary<string, string>
            {
                ["nickname"] = scrub.Nickname,
                ["value"] = v.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }).ConfigureAwait(false);
            if (!sliderResp.Success)
            {
                _logger.Log($"  ! scrub frame {i:D2} SetSlider('{scrub.Nickname}'={v}) failed: {sliderResp.Message}");
                continue;
            }
            // Log the actualValue the agent reports back — if it doesn't
            // match `v`, SetSliderValue silently failed and the downstream
            // Animate Bound is reading a stale Index.
            string actual = "(no data)";
            if (sliderResp.Data != null && sliderResp.Data.TryGetValue("actualValue", out var a)) actual = a;
            _logger.Log($"  scrub frame {i:D2}/{scrub.Values.Length - 1}: {scrub.Nickname}={v} (actual={actual})");

            var waitResp = await executeAsync("WaitForGrasshopperSolution", new Dictionary<string, string>
            {
                ["timeoutMs"] = solveTimeout
            }).ConfigureAwait(false);
            if (!waitResp.Success)
            {
                _logger.Log($"  ! scrub frame {i:D2} WaitForGrasshopperSolution failed: {waitResp.Message}");
                continue;
            }

            if (scrub.SettleMs > 0)
                await Task.Delay(scrub.SettleMs, ct).ConfigureAwait(false);

            var framePath = Path.Combine(dir, $"{baseName}.frame{i:D2}.png");
            try
            {
                var captureResp = await captureAsync(new CaptureSettings
                {
                    Width = width,
                    Height = height,
                    OutputPath = framePath,
                    IncludeFullScreen = false,
                    RecordGif = false
                }).ConfigureAwait(false);
                if (File.Exists(captureResp.FilePath))
                    frames.Add(captureResp.FilePath);
            }
            catch (Exception ex)
            {
                _logger.Log($"  ! scrub frame {i:D2} capture failed: {ex.Message}");
            }
        }

        return frames;
    }
}
