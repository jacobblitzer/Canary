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
    public string? VlmReasoning { get; init; }
    public double VlmConfidence { get; init; }

    [ObservableProperty] private Bitmap? _baselineThumb;
    [ObservableProperty] private Bitmap? _candidateThumb;
    [ObservableProperty] private Bitmap? _diffThumb;
    [ObservableProperty] private bool _resolved;
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
            StatusText = $"Approved checkpoint {card.TestName}/{card.Name} as baseline.";
        }
        catch (Exception ex)
        {
            StatusText = $"Approve failed: {ex.Message}";
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
            StatusText = $"Rejected candidate for {card.TestName}/{card.Name}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Reject failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ApproveAll()
    {
        if (_workloadsDir == null || ActiveWorkloadName == null) return;
        int count = 0;
        foreach (var byTest in Cards.GroupBy(c => c.TestName))
        {
            try
            {
                count += BaselineManager.ApproveTest(_workloadsDir, ActiveWorkloadName, byTest.Key, ActiveSuiteName);
                foreach (var c in byTest) c.Resolved = true;
            }
            catch { /* surface per-test failures in StatusText below */ }
        }
        StatusText = $"Approved {count} checkpoint(s) across {Cards.GroupBy(c => c.TestName).Count()} test(s).";
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
            VlmReasoning = cp.VlmReasoning,
            VlmConfidence = cp.VlmConfidence,
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
