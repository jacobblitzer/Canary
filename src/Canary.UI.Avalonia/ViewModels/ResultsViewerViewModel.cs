using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Canary.Orchestration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Canary.UI.Avalonia.ViewModels;

public sealed partial class CheckpointCardViewModel : ObservableObject
{
    public required string Name { get; init; }
    public required string TestName { get; init; }
    public required string Status { get; init; }
    public required string StatusColor { get; init; }
    public required double DiffPercentage { get; init; }
    public required double Tolerance { get; init; }
    public required double SsimScore { get; init; }
    public string? BaselinePath { get; init; }
    public string? CandidatePath { get; init; }
    public string? DiffImagePath { get; init; }
    public string? GifPath { get; init; }
    public string? VlmReasoning { get; init; }
    public double VlmConfidence { get; init; }

    // Phase 14.5 — per-checkpoint error surfaced on the card so the
    // operator doesn't have to dig into result.json for the failure cause.
    public string? ErrorMessage { get; init; }

    // Phase 14.7 — filetype labels on each card cell so the operator can
    // tell PNG (static) from GIF (animated scrub) at a glance. Derived
    // from the path extension; "" when the path is absent.
    public string BaselineHeader => CellHeader("baseline", BaselinePath);
    public string CandidateHeader => CellHeader("candidate", CandidatePath);
    public string DiffHeader => CellHeader("diff", DiffImagePath);

    private static string CellHeader(string label, string? path)
    {
        if (string.IsNullOrEmpty(path)) return label;
        var ext = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrEmpty(ext) ? label : $"{label}  ·  {ext}";
    }

    [ObservableProperty] private Bitmap? _baselineThumb;
    [ObservableProperty] private Bitmap? _candidateThumb;
    [ObservableProperty] private Bitmap? _diffThumb;
    [ObservableProperty] private bool _resolved;

    // Phase 14.5 — post-action label shown on the card after Approve / Reject
    // so the operator gets visible feedback. "✓ Approved" / "✗ Rejected".
    [ObservableProperty] private string? _resolutionLabel;
    [ObservableProperty] private string _resolutionColor = "#969696";
}

public partial class ResultsViewerViewModel : ObservableObject
{
    private string? _workloadsDir;

    public ObservableCollection<CheckpointCardViewModel> Cards { get; } = new();

    [ObservableProperty] private string _header = "(no result loaded)";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _activeTestName;
    [ObservableProperty] private string? _activeSuiteName;
    [ObservableProperty] private string? _activeWorkloadName;

    // Phase 14.5 — short-lived toast banner at the top of the view. Set by
    // every Approve / Reject / Save Snapshot so the operator sees feedback
    // immediately (the StatusText footer is below the scroll viewport and
    // easy to miss). Cleared by the next action.
    [ObservableProperty] private string? _toastMessage;
    [ObservableProperty] private string _toastColor = "#3CC850";

    private void Toast(string message, bool success = true)
    {
        ToastMessage = message;
        ToastColor = success ? "#3CC850" : "#DC3C3C";
        StatusText = message;
    }

    public void SetContext(string workloadsDir, string workloadName, string? suiteName)
    {
        _workloadsDir = workloadsDir;
        ActiveWorkloadName = workloadName;
        ActiveSuiteName = suiteName;
    }

    public void LoadResult(TestResult result)
    {
        ActiveTestName = result.TestName;
        Header = $"{result.TestName}  —  {result.Status}";
        ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage) ? null : result.ErrorMessage;
        Cards.Clear();
        foreach (var cp in result.CheckpointResults)
        {
            Cards.Add(BuildCard(cp, result.TestName));
        }
        StatusText = $"{Cards.Count} checkpoint(s)";
    }

    /// <summary>
    /// Phase 14.3 — load an arbitrary past run's <c>result.json</c> from disk
    /// into the viewer. Uses the existing <see cref="TestResultSerializer"/>;
    /// the resulting <see cref="TestResult"/> feeds <see cref="LoadResult"/>.
    /// <c>SetContext</c> still needs to be called beforehand so Approve /
    /// Reject know where to write the baseline back.
    /// </summary>
    public async Task LoadFromPathAsync(string resultJsonPath)
    {
        try
        {
            var result = await TestResultSerializer.LoadAsync(resultJsonPath).ConfigureAwait(true);
            LoadResult(result);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load past run: {ex.Message}";
            Header = "(load failed)";
            Cards.Clear();
            StatusText = ex.Message;
        }
    }

    public void LoadSuiteResult(SuiteResult suite, string suiteName)
    {
        ActiveTestName = null;
        ActiveSuiteName = suiteName;
        Header = $"Suite: {suiteName} — {(suite.Failed == 0 && suite.Crashed == 0 ? "ALL PASSED" : "FAILURES")}";
        ErrorMessage = null;
        Cards.Clear();
        foreach (var result in suite.TestResults)
        {
            foreach (var cp in result.CheckpointResults)
            {
                Cards.Add(BuildCard(cp, result.TestName));
            }
        }
        StatusText = $"{suite.Passed} passed · {suite.Failed} failed · {suite.Crashed} crashed · {suite.New} new · {suite.TotalDuration.TotalSeconds:F1}s";
    }

    [RelayCommand]
    private void ApproveCheckpoint(CheckpointCardViewModel? card)
    {
        if (card == null || _workloadsDir == null || ActiveWorkloadName == null) return;
        try
        {
            BaselineManager.ApproveCheckpoint(_workloadsDir, ActiveWorkloadName, card.TestName, card.Name, ActiveSuiteName);
            card.Resolved = true;
            card.ResolutionLabel = "✓ Approved";
            card.ResolutionColor = "#3CC850";
            Toast($"Approved {card.TestName}/{card.Name} → baseline updated.");
        }
        catch (Exception ex)
        {
            Toast($"Approve failed: {ex.Message}", success: false);
        }
    }

    [RelayCommand]
    private void RejectCheckpoint(CheckpointCardViewModel? card)
    {
        if (card == null || _workloadsDir == null || ActiveWorkloadName == null) return;
        try
        {
            BaselineManager.RejectCheckpoint(_workloadsDir, ActiveWorkloadName, card.TestName, card.Name, ActiveSuiteName);
            card.Resolved = true;
            card.ResolutionLabel = "✗ Rejected";
            card.ResolutionColor = "#DC3C3C";
            Toast($"Rejected candidate for {card.TestName}/{card.Name}.");
        }
        catch (Exception ex)
        {
            Toast($"Reject failed: {ex.Message}", success: false);
        }
    }

    [RelayCommand]
    private void ApproveAll()
    {
        if (_workloadsDir == null || ActiveWorkloadName == null)
        {
            Toast("Approve All: no run loaded.", success: false);
            return;
        }
        int count = 0;
        int testCount = Cards.GroupBy(c => c.TestName).Count();
        foreach (var byTest in Cards.GroupBy(c => c.TestName))
        {
            try
            {
                count += BaselineManager.ApproveTest(_workloadsDir, ActiveWorkloadName, byTest.Key, ActiveSuiteName);
                foreach (var c in byTest)
                {
                    c.Resolved = true;
                    c.ResolutionLabel = "✓ Approved";
                    c.ResolutionColor = "#3CC850";
                }
            }
            catch { /* surface per-test failures via Toast below */ }
        }
        Toast($"Approved {count} checkpoint(s) across {testCount} test(s).");
    }

    /// <summary>
    /// Phase 14.5 — freeze the currently-loaded run's artifacts into
    /// <c>&lt;testDir&gt;/archived/&lt;stamp&gt;/</c>. Does NOT touch baselines and
    /// does NOT mark anything approved/rejected — just preserves the bytes.
    /// Mirrors <c>TestRunnerViewModel.SaveSnapshot</c> but works against
    /// arbitrary loaded runs (fresh or past).
    /// </summary>
    [RelayCommand]
    private void SaveSnapshot() => SnapshotInner(@override: false);

    /// <summary>
    /// Phase 14.7 — Override-mode snapshot. Writes to a fixed
    /// <c>archived/latest/</c> slot, replacing the prior contents.
    /// </summary>
    [RelayCommand]
    private void OverrideSnapshot() => SnapshotInner(@override: true);

    private void SnapshotInner(bool @override)
    {
        if (_workloadsDir == null || ActiveWorkloadName == null)
        {
            Toast("Save Snapshot: no run loaded.", success: false);
            return;
        }
        var slot = @override ? "latest" : DateTime.Now.ToString("yyyyMMdd-HHmmss");
        int snapped = 0;
        try
        {
            foreach (var testName in Cards.Select(c => c.TestName).Distinct())
            {
                var testDir = Path.Combine(_workloadsDir, ActiveWorkloadName, "results", testName);
                if (!Directory.Exists(testDir)) continue;
                var archiveRoot = Path.Combine(testDir, "archived", slot);
                if (@override && Directory.Exists(archiveRoot))
                {
                    try { Directory.Delete(archiveRoot, recursive: true); }
                    catch { /* fall through — directory create will surface errors */ }
                }
                Directory.CreateDirectory(archiveRoot);

                // Source set: when ActiveSuiteName looks like a timestamp dir
                // (past-run view), pull from runs/<ts>/. Otherwise from the
                // test's top-level state (fresh in-session run).
                string sourceBase = LooksLikeTimestampDir(ActiveSuiteName)
                    ? Path.Combine(testDir, "runs", ActiveSuiteName!)
                    : testDir;

                foreach (var sub in new[] { "candidates", "manual-captures", "logs" })
                {
                    var src = Path.Combine(sourceBase, sub);
                    if (Directory.Exists(src))
                        CopyDirectoryRecursive(src, Path.Combine(archiveRoot, sub));
                }
                foreach (var f in Directory.EnumerateFiles(sourceBase, "*.json"))
                {
                    try { File.Copy(f, Path.Combine(archiveRoot, Path.GetFileName(f)), overwrite: true); }
                    catch { /* per-file copy failures non-fatal */ }
                }
                // Phase 14.7 — if the source is testDir (fresh in-session run),
                // pick up the most-recent runs/<ts>/result.json so the snapshot
                // renders in the Past Runs viewer. Past-run loads already have
                // result.json sitting in sourceBase via the *.json copy above.
                if (!LooksLikeTimestampDir(ActiveSuiteName))
                {
                    var runsDir = Path.Combine(testDir, "runs");
                    if (Directory.Exists(runsDir))
                    {
                        var latestRun = Directory.GetDirectories(runsDir)
                            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                            .FirstOrDefault();
                        if (latestRun != null)
                        {
                            var src = Path.Combine(latestRun, "result.json");
                            if (File.Exists(src))
                                File.Copy(src, Path.Combine(archiveRoot, "result.json"), overwrite: true);
                        }
                    }
                }
                snapped++;
            }
            var icon = @override ? "📌 Override" : "💾";
            Toast($"{icon} Snapshot saved → archived/{slot}/  ({snapped} test(s))");
        }
        catch (Exception ex)
        {
            Toast($"Save Snapshot failed: {ex.Message}", success: false);
        }
    }

    private static bool LooksLikeTimestampDir(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 15) return false;
        // Format: yyyyMMdd-HHmmss-<hex>
        return s[8] == '-' && s.Take(8).All(char.IsDigit) && s.Skip(9).Take(6).All(char.IsDigit);
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            if (string.Equals(Path.GetFileName(dir), "archived", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    /// <summary>
    /// Phase 14.5 — click a thumb in the card to open the underlying PNG in
    /// the OS default image viewer.
    /// </summary>
    [RelayCommand]
    private void OpenImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch { /* shell open fails silently — operator can still copy the path */ }
    }

    [RelayCommand]
    private void OpenImageInExplorer(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
        }
        catch { /* ignore */ }
    }

    private static CheckpointCardViewModel BuildCard(CheckpointResult cp, string testName)
    {
        var card = new CheckpointCardViewModel
        {
            Name = cp.Name,
            TestName = testName,
            Status = cp.Status.ToString(),
            StatusColor = StatusHex(cp.Status),
            DiffPercentage = cp.DiffPercentage,
            Tolerance = cp.Tolerance,
            SsimScore = cp.SsimScore,
            BaselinePath = cp.BaselinePath,
            CandidatePath = cp.CandidatePath,
            DiffImagePath = cp.DiffImagePath,
            GifPath = cp.GifPath,
            VlmReasoning = cp.VlmReasoning,
            VlmConfidence = cp.VlmConfidence,
            ErrorMessage = cp.ErrorMessage,
        };
        card.BaselineThumb = TryLoadBitmap(cp.BaselinePath);
        card.CandidateThumb = TryLoadBitmap(cp.CandidatePath);
        card.DiffThumb = TryLoadBitmap(cp.DiffImagePath);
        return card;
    }

    private static Bitmap? TryLoadBitmap(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            return new Bitmap(fs);
        }
        catch
        {
            return null;
        }
    }

    private static string StatusHex(TestStatus status) => status switch
    {
        TestStatus.Passed => "#50C850",
        TestStatus.Failed => "#DC3C3C",
        TestStatus.Crashed => "#B450DC",
        TestStatus.New => "#DCB432",
        _ => "#969696",
    };
}
