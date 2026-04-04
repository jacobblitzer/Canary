using System.Diagnostics;
using Canary.Agent;
using Canary.Comparison;
using Canary.Config;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Canary.Orchestration;

/// <summary>
/// Orchestrates the full test lifecycle: launch app → connect → setup → replay → capture → compare.
/// </summary>
public sealed class TestRunner
{
    private readonly ProcessManager _processManager;
    private readonly string _workloadsDir;
    private readonly PixelDiffComparer _pixelDiff = new();
    private readonly SsimComparer _ssim = new();

    public TestRunner(ProcessManager processManager, string workloadsDir)
    {
        _processManager = processManager;
        _workloadsDir = workloadsDir;
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
            Program.Log($"Launching {workload.DisplayName}...");
            appProcess = AppLauncher.Launch(workload);
            _processManager.Track(appProcess);

            var pipeName = $"{workload.PipeName}-{appProcess.Id}";

            // 2. Wait for agent
            Program.Log($"Waiting for agent on pipe '{pipeName}'...");
            var agentReady = await AppLauncher.WaitForAgentAsync(
                pipeName,
                TimeSpan.FromMilliseconds(workload.StartupTimeoutMs),
                cancellationToken).ConfigureAwait(false);

            if (!agentReady)
            {
                result.Status = TestStatus.Crashed;
                result.ErrorMessage = "Agent did not become available within startup timeout.";
                result.Duration = sw.Elapsed;
                return result;
            }

            // 3. Connect client
            client = new HarnessClient(pipeName);
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

            // Verify heartbeat
            var hb = await client.HeartbeatAsync(cancellationToken).ConfigureAwait(false);
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

            // 5. Send setup commands
            if (testDef.Setup != null)
                await SendSetupCommandsAsync(client, testDef.Setup, workload, cancellationToken).ConfigureAwait(false);

            // 6. Process checkpoints (capture + compare)
            var testDir = GetTestDirectory(workload.Name, testDef.Name);
            Directory.CreateDirectory(testDir);

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
                    client, checkpoint, testDir, workload, cancellationToken).ConfigureAwait(false);

                result.CheckpointResults.Add(cpResult);

                if (cpResult.Status == TestStatus.Failed)
                    result.Status = TestStatus.Failed;
                else if (cpResult.Status == TestStatus.Crashed)
                    result.Status = TestStatus.Crashed;
                else if (cpResult.Status == TestStatus.New && result.Status == TestStatus.Passed)
                    result.Status = TestStatus.New;
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
                    Program.Log($"Warning: Failed to build composite image: {ex.Message}");
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

            var (symbol, color) = result.Status switch
            {
                TestStatus.Passed => ("PASS", ConsoleColor.Green),
                TestStatus.Failed => ("FAIL", ConsoleColor.Red),
                TestStatus.Crashed => ("CRASH", ConsoleColor.Magenta),
                TestStatus.New => ("NEW", ConsoleColor.Yellow),
                _ => ("???", ConsoleColor.Gray)
            };

            var maxDiff = result.CheckpointResults.Count > 0
                ? result.CheckpointResults.Max(c => c.DiffPercentage)
                : 0;

            Program.LogStatus(symbol, $"{result.TestName} ({maxDiff:P1} max diff)", color);

            // Verbose: show per-checkpoint details
            if (Program.Verbose)
            {
                foreach (var cp in result.CheckpointResults)
                {
                    var cpSymbol = cp.Status == TestStatus.Passed ? "  +" : "  -";
                    Program.Log($"{cpSymbol} {cp.Name}: diff={cp.DiffPercentage:P2}, ssim={cp.SsimScore:F4}, tol={cp.Tolerance:P2}");
                }
            }
        }

        // Summary always prints (even in quiet mode)
        Console.WriteLine($"Results: {suite.Passed} passed, {suite.Failed} failed, {suite.Crashed} crashed, {suite.New} new");
        return suite;
    }

    private static async Task SendSetupCommandsAsync(
        HarnessClient client, TestSetup setup, WorkloadConfig workload, CancellationToken ct)
    {
        // Open file if specified
        if (!string.IsNullOrWhiteSpace(setup.File))
        {
            Program.Log($"Opening file: {setup.File}");
            await client.ExecuteAsync("OpenFile", new Dictionary<string, string>
            {
                ["path"] = setup.File
            }, ct).ConfigureAwait(false);
        }

        // Set viewport if specified
        if (setup.Viewport != null)
        {
            await client.ExecuteAsync("SetViewport", new Dictionary<string, string>
            {
                ["width"] = setup.Viewport.Width.ToString(),
                ["height"] = setup.Viewport.Height.ToString(),
                ["projection"] = setup.Viewport.Projection,
                ["displayMode"] = setup.Viewport.DisplayMode
            }, ct).ConfigureAwait(false);
        }

        // Run setup commands
        foreach (var cmd in setup.Commands)
        {
            Program.Log($"Running: {cmd}");
            await client.ExecuteAsync("RunCommand", new Dictionary<string, string>
            {
                ["command"] = cmd
            }, ct).ConfigureAwait(false);
        }
    }

    private async Task<CheckpointResult> ProcessCheckpointAsync(
        HarnessClient client,
        TestCheckpoint checkpoint,
        string testDir,
        WorkloadConfig workload,
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

            Program.Log($"Capturing checkpoint: {checkpoint.Name}");
            var captureResult = await client.CaptureScreenshotAsync(new CaptureSettings
            {
                Width = 800,
                Height = 600,
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

    private string GetTestDirectory(string workloadName, string testName)
    {
        return Path.Combine(_workloadsDir, workloadName, "results", testName);
    }

    /// <summary>
    /// Discover all test definitions for a workload.
    /// </summary>
    public static async Task<List<TestDefinition>> DiscoverTestsAsync(string workloadsDir, string workloadName)
    {
        var testsDir = Path.Combine(workloadsDir, workloadName, "tests");
        if (!Directory.Exists(testsDir))
            return new List<TestDefinition>();

        var tests = new List<TestDefinition>();
        foreach (var file in Directory.GetFiles(testsDir, "*.json"))
        {
            try
            {
                tests.Add(await TestDefinition.LoadAsync(file).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Program.Log($"Warning: Failed to parse test '{file}': {ex.Message}");
            }
        }
        return tests;
    }

    /// <summary>
    /// Approve a test by copying candidates to baselines.
    /// </summary>
    public static void ApproveTest(string workloadsDir, string workloadName, string testName)
    {
        var testDir = Path.Combine(workloadsDir, workloadName, "results", testName);
        var candidatesDir = Path.Combine(testDir, "candidates");
        var baselinesDir = Path.Combine(testDir, "baselines");

        if (!Directory.Exists(candidatesDir))
            throw new DirectoryNotFoundException($"No candidates found for test '{testName}'. Run the test first.");

        Directory.CreateDirectory(baselinesDir);

        int count = 0;
        foreach (var file in Directory.GetFiles(candidatesDir, "*.png"))
        {
            var dest = Path.Combine(baselinesDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
            count++;
        }

        Program.Log($"Approved {count} baseline(s) for test '{testName}'.");
    }
}
