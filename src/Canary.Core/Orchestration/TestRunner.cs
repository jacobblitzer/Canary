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
public sealed class TestRunner
{
    private readonly ProcessManager _processManager;
    private readonly string _workloadsDir;
    private readonly ITestLogger _logger;
    private readonly PixelDiffComparer _pixelDiff = new();
    private readonly SsimComparer _ssim = new();

    /// <summary>
    /// Callback invoked when the target window handle is found.
    /// Called from the test thread — callers must marshal to UI thread if needed.
    /// </summary>
    public Action<IntPtr>? OnTargetWindowFound { get; set; }

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
        CancellationToken cancellationToken)
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

            // 2. Connect to agent (waits for pipe to appear during app startup)
            _logger.Log($"Connecting to agent on pipe '{pipeName}' (timeout: {workload.StartupTimeoutMs}ms)...");
            client = new HarnessClient(pipeName);
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

            // 6. Process checkpoints (capture + compare)
            var testDir = GetTestDirectory(workload.Name, testDef.Name);
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

                        var cpResult = await ProcessCheckpointAsync(
                            client!, checkpoint, testDir, captureWidth, captureHeight, cancellationToken).ConfigureAwait(false);

                        result.CheckpointResults.Add(cpResult);

                        if (cpResult.Status == TestStatus.Failed)
                            result.Status = TestStatus.Failed;
                        else if (cpResult.Status == TestStatus.Crashed)
                            result.Status = TestStatus.Crashed;
                        else if (cpResult.Status == TestStatus.New && result.Status == TestStatus.Passed)
                            result.Status = TestStatus.New;
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

                    var cpResult = await ProcessCheckpointAsync(
                        client!, checkpoint, testDir, captureWidth, captureHeight, cancellationToken).ConfigureAwait(false);

                    result.CheckpointResults.Add(cpResult);

                    if (cpResult.Status == TestStatus.Failed)
                        result.Status = TestStatus.Failed;
                    else if (cpResult.Status == TestStatus.Crashed)
                        result.Status = TestStatus.Crashed;
                    else if (cpResult.Status == TestStatus.New && result.Status == TestStatus.Passed)
                        result.Status = TestStatus.New;
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
        CancellationToken cancellationToken)
    {
        var suite = new SuiteResult();

        foreach (var test in tests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await RunTestAsync(test, workload, cancellationToken).ConfigureAwait(false);
            suite.TestResults.Add(result);

            var (symbol, level) = result.Status switch
            {
                TestStatus.Passed => ("PASS", TestStatusLevel.Pass),
                TestStatus.Failed => ("FAIL", TestStatusLevel.Fail),
                TestStatus.Crashed => ("CRASH", TestStatusLevel.Crash),
                TestStatus.New => ("NEW", TestStatusLevel.New),
                _ => ("???", TestStatusLevel.Info)
            };

            var maxDiff = result.CheckpointResults.Count > 0
                ? result.CheckpointResults.Max(c => c.DiffPercentage)
                : 0;

            _logger.LogStatus(symbol, $"{result.TestName} ({maxDiff:P1} max diff)", level);

            // Verbose: show per-checkpoint details
            if (_logger.Verbose)
            {
                foreach (var cp in result.CheckpointResults)
                {
                    var cpSymbol = cp.Status == TestStatus.Passed ? "  +" : "  -";
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

    private async Task SendSetupCommandsAsync(
        HarnessClient client, TestSetup setup, WorkloadConfig workload, CancellationToken ct)
    {
        // Open file if specified
        if (!string.IsNullOrWhiteSpace(setup.File))
        {
            _logger.Log($"Opening file: {setup.File}");
            await client.ExecuteAsync("OpenFile", new Dictionary<string, string>
            {
                ["path"] = setup.File
            }, ct).ConfigureAwait(false);
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
        CancellationToken ct)
    {
        var cpResult = new CheckpointResult
        {
            Name = checkpoint.Name,
            Tolerance = checkpoint.Tolerance
        };

        try
        {
            // Capture screenshot
            var candidatePath = Path.Combine(testDir, "candidates", $"{checkpoint.Name}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);

            _logger.Log($"Capturing checkpoint: {checkpoint.Name}");
            var captureResult = await client.CaptureScreenshotAsync(new CaptureSettings
            {
                Width = captureWidth,
                Height = captureHeight,
                OutputPath = candidatePath
            }, ct).ConfigureAwait(false);

            cpResult.CandidatePath = captureResult.FilePath;

            // Check for baseline
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

    private string GetTestDirectory(string workloadName, string testName)
    {
        return Path.Combine(_workloadsDir, workloadName, "results", testName);
    }
}
