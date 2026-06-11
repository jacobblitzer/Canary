using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Canary.Cli;
using Canary.Orchestration;
using Canary.UI.Avalonia.Services;

namespace Canary.UI.Avalonia.ViewModels;

public sealed class NavItem
{
    public required string Title { get; init; }
    public required string IconGlyph { get; init; }
    public required ObservableObject ViewModel { get; init; }
}

public partial class MainWindowViewModel : ObservableObject
{
    public ObservableCollection<NavItem> NavItems { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTestsActive))]
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private string? _workloadsDir;

    public TestsViewModel Tests { get; }
    public SessionsViewModel Sessions { get; }
    public LocalhostViewModel Localhost { get; }
    public FeedbackViewModel Feedback { get; }
    public TelemetryViewModel Telemetry { get; }
    public SettingsViewModel Settings { get; }
    // Docked Run History pane (feedback 2026-06-10-run-history-log-window) —
    // lives below the NavigationView so it stays visible in every tab.
    public RunHistoryViewModel RunHistory { get; }

    public IReadOnlyList<ModeOverride> ModeOverrides { get; } = new[] { ModeOverride.None, ModeOverride.PixelDiff, ModeOverride.Vlm, ModeOverride.Both };

    public Func<Task<string?>>? PickWorkloadsDirAsync { get; set; }

    public bool IsTestsActive => SelectedNavItem?.ViewModel is TestsViewModel;

    public MainWindowViewModel()
    {
        Tests = new TestsViewModel();
        Sessions = new SessionsViewModel();
        Localhost = new LocalhostViewModel();
        Feedback = new FeedbackViewModel();
        Telemetry = new TelemetryViewModel();
        Settings = new SettingsViewModel();
        RunHistory = new RunHistoryViewModel();
        // Keep the log current: every in-UI run appends a runs/<stamp>/ dir,
        // so re-scan when the runner finishes. CLI runs land on the next
        // manual Refresh / workloads-dir load.
        Tests.Runner.RunCompleted += () => _ = RunHistory.RefreshAsync();

        // Phase 2 full nav set. Tests is the operator's primary work
        // surface so it leads the rail.
        NavItems.Add(new NavItem { Title = "Tests",     IconGlyph = "", ViewModel = Tests });
        NavItems.Add(new NavItem { Title = "Sessions",  IconGlyph = "", ViewModel = Sessions });
        NavItems.Add(new NavItem { Title = "Localhost", IconGlyph = "", ViewModel = Localhost });
        NavItems.Add(new NavItem { Title = "Feedback",  IconGlyph = "", ViewModel = Feedback });
        NavItems.Add(new NavItem { Title = "Telemetry", IconGlyph = "", ViewModel = Telemetry });
        NavItems.Add(new NavItem { Title = "Settings",  IconGlyph = "", ViewModel = Settings });
        SelectedNavItem = NavItems[0];

        var detected = WorkloadsLocator.AutoDetect();
        if (detected != null) ApplyWorkloadsDir(detected);
    }

    [RelayCommand]
    private async Task OpenWorkloadsFolderAsync()
    {
        if (PickWorkloadsDirAsync == null) return;
        var picked = await PickWorkloadsDirAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(picked) && Directory.Exists(picked))
        {
            ApplyWorkloadsDir(picked);
        }
    }

    [RelayCommand]
    private async Task RunSelectedAsync()
    {
        await Tests.RunSelectionAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void RecordNew()
    {
        SelectedNavItem = NavItems.FirstOrDefault(n => n.ViewModel is TestsViewModel) ?? SelectedNavItem;
        Tests.ShowRecording();
    }

    private void ApplyWorkloadsDir(string dir)
    {
        WorkloadsDir = dir;
        _ = Sessions.LoadWorkloadsAsync(dir);
        _ = Tests.LoadWorkloadsAsync(dir);
        Telemetry.SetWorkloadsDir(dir);
        RunHistory.SetWorkloadsDir(dir);
    }

    public async Task HandleAutoRunAsync(AutoRunArgs args)
    {
        if (args.IsEmpty) return;
        if (string.IsNullOrEmpty(args.Workload)) return;

        // Switch to the Tests tab so the run is visible.
        var testsItem = NavItems.FirstOrDefault(n => n.ViewModel is TestsViewModel);
        if (testsItem != null) SelectedNavItem = testsItem;

        // Tree may still be loading from the constructor's auto-detect; poll
        // briefly (up to ~10s) until it appears.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && (WorkloadsDir == null || Tests.Tree.Roots.Count == 0))
        {
            await Task.Delay(100).ConfigureAwait(true);
        }
        if (WorkloadsDir == null || Tests.Tree.Roots.Count == 0) return;

        var target = AutoRunRequestHandler.FindNode(Tests.Tree, args);
        if (target == null) return;

        Tests.Tree.SelectedNode = target;
        Tests.ModeOverride = AutoRunRequestHandler.ParseMode(args.Mode);
        await Tests.RunSelectionAsync().ConfigureAwait(true);
    }

    public void HandleAutoRun(AutoRunArgs args) => _ = HandleAutoRunAsync(args);
}
