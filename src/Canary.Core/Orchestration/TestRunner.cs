using System.Diagnostics;
using Canary.Agent;
using Canary.Comparison;
using Canary.Config;
using Canary.Input;
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
        var result = new TestResult
        {
            TestName = testDef.Name,
            Workload = workload.Name,
            Status = TestStatus.Passed
        };

        Process? appProcess = null;
        HarnessClient? client = null;
        Task? watchdogTask = null;
        var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool appDead = false;

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

            // 6. Process checkpoints (capture + compare)
            var testDir = GetTestDirectory(workload.Name, testDef.Name, suiteName);
            Directory.CreateDirectory(testDir);

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

                        var testDir = GetTestDirectory(workload.Name, test.Name);
                        Directory.CreateDirectory(testDir);

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
        var result = new TestResult
        {
            TestName = testDef.Name,
            Workload = workload.Name,
            Status = TestStatus.Passed
        };

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

            // 3. Process checkpoints with camera positioning
            var testDir = GetTestDirectory(workload.Name, testDef.Name, suiteName);
            Directory.CreateDirectory(testDir);

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
                    agent, checkpoint, testDir, captureWidth, captureHeight, result, cancellationToken).ConfigureAwait(false);
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

        try
        {
            var candidatePath = Path.Combine(testDir, "candidates", $"{checkpoint.Name}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);

            if (string.Equals(checkpoint.Source, "file", StringComparison.OrdinalIgnoreCase))
            {
                // File-source checkpoint: read the file path from a GH panel
                if (string.IsNullOrWhiteSpace(checkpoint.PanelNickname))
                {
                    cpResult.Status = TestStatus.Crashed;
                    cpResult.ErrorMessage = "File-source checkpoint requires 'panelNickname'.";
                    return cpResult;
                }

                _logger.Log($"Reading file path from panel '{checkpoint.PanelNickname}'...");
                var panelResp = await agent.ExecuteAsync("GrasshopperGetPanelText",
                    new Dictionary<string, string> { ["nickname"] = checkpoint.PanelNickname }).ConfigureAwait(false);

                if (!panelResp.Success)
                {
                    cpResult.Status = TestStatus.Crashed;
                    cpResult.ErrorMessage = $"Failed to read panel '{checkpoint.PanelNickname}': {panelResp.Message}";
                    return cpResult;
                }

                var filePath = (panelResp.Data != null && panelResp.Data.TryGetValue("text", out var t) ? t : string.Empty).Trim();
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
                // Default: viewport capture
                _logger.Log($"Capturing checkpoint: {checkpoint.Name}");
                var captureResult = await agent.CaptureScreenshotAsync(new CaptureSettings
                {
                    Width = captureWidth,
                    Height = captureHeight,
                    OutputPath = candidatePath
                }).ConfigureAwait(false);

                cpResult.CandidatePath = captureResult.FilePath;
            }

            // Branch on comparison mode. forceMode (set by ResolveEffectiveModes)
            // wins over checkpoint.Mode. forceMode == null preserves the original
            // per-checkpoint behaviour for backwards compatibility.
            var effective = forceMode
                ?? (string.Equals(checkpoint.Mode, "vlm", StringComparison.OrdinalIgnoreCase)
                    ? CheckpointMode.Vlm
                    : CheckpointMode.PixelDiff);
            if (effective == CheckpointMode.Vlm)
            {
                return await ProcessVlmCheckpointAsync(cpResult, checkpoint, ct).ConfigureAwait(false);
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
            using var candidate = await Image.LoadAsync<Rgba32>(cpResult.CandidatePath, ct).ConfigureAwait(false);

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
                // File-source checkpoint: read the file path from a GH panel
                // and copy it into the candidates directory.
                if (string.IsNullOrWhiteSpace(checkpoint.PanelNickname))
                {
                    cpResult.Status = TestStatus.Crashed;
                    cpResult.ErrorMessage = "File-source checkpoint requires 'panelNickname'.";
                    return cpResult;
                }

                _logger.Log($"Reading file path from panel '{checkpoint.PanelNickname}'...");
                var panelResp = await client.ExecuteAsync("GrasshopperGetPanelText",
                    new Dictionary<string, string> { ["nickname"] = checkpoint.PanelNickname }, ct).ConfigureAwait(false);

                if (!panelResp.Success)
                {
                    cpResult.Status = TestStatus.Crashed;
                    cpResult.ErrorMessage = $"Failed to read panel '{checkpoint.PanelNickname}': {panelResp.Message}";
                    return cpResult;
                }

                var filePath = (panelResp.Data != null && panelResp.Data.TryGetValue("text", out var t) ? t : string.Empty).Trim();
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
                // Default: viewport capture
                _logger.Log($"Capturing checkpoint: {checkpoint.Name}");
                var captureResult = await client.CaptureScreenshotAsync(new CaptureSettings
                {
                    Width = captureWidth,
                    Height = captureHeight,
                    OutputPath = candidatePath
                }, ct).ConfigureAwait(false);

                cpResult.CandidatePath = captureResult.FilePath;
            }

            // Branch on comparison mode. forceMode (set by ResolveEffectiveModes)
            // wins over checkpoint.Mode. forceMode == null preserves the original
            // per-checkpoint behaviour for backwards compatibility.
            var effective = forceMode
                ?? (string.Equals(checkpoint.Mode, "vlm", StringComparison.OrdinalIgnoreCase)
                    ? CheckpointMode.Vlm
                    : CheckpointMode.PixelDiff);
            if (effective == CheckpointMode.Vlm)
            {
                return await ProcessVlmCheckpointAsync(cpResult, checkpoint, ct).ConfigureAwait(false);
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
            using var candidate = await Image.LoadAsync<Rgba32>(cpResult.CandidatePath, ct).ConfigureAwait(false);

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
        ICanaryAgent agent, TestCheckpoint checkpoint, string testDir,
        int captureWidth, int captureHeight, TestResult result, CancellationToken ct)
    {
        foreach (var mode in ResolveEffectiveModes(checkpoint))
        {
            var cpResult = await ProcessAgentCheckpointAsync(
                agent, checkpoint, testDir, captureWidth, captureHeight, ct, mode).ConfigureAwait(false);
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
            var imageBytes = await File.ReadAllBytesAsync(cpResult.CandidatePath, ct).ConfigureAwait(false);
            var verdict = await _vlmProvider.EvaluateAsync(imageBytes, description, ct).ConfigureAwait(false);

            cpResult.VlmReasoning = verdict.Reasoning;
            cpResult.VlmConfidence = verdict.Confidence;
            cpResult.Status = verdict.Passed ? TestStatus.Passed : TestStatus.Failed;

            _logger.Log($"VLM verdict: {(verdict.Passed ? "PASS" : "FAIL")} (confidence={verdict.Confidence:F2})");
            if (!string.IsNullOrEmpty(verdict.Reasoning))
                _logger.Log($"  Reasoning: {verdict.Reasoning}");
        }
        catch (Exception ex)
        {
            cpResult.Status = TestStatus.Crashed;
            cpResult.ErrorMessage = $"VLM evaluation failed: {ex.Message}";
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
}
