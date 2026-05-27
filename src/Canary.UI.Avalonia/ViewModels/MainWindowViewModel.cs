using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Canary.Cli;
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
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private string? _workloadsDir;

    public SessionsViewModel Sessions { get; }
    public LocalhostViewModel Localhost { get; }
    public FeedbackViewModel Feedback { get; }
    public TelemetryViewModel Telemetry { get; }
    public SettingsViewModel Settings { get; }

    // Set by the View at construction time so OpenWorkloadsFolderCommand
    // can show an Avalonia folder picker (the picker needs a TopLevel).
    public Func<Task<string?>>? PickWorkloadsDirAsync { get; set; }

    public MainWindowViewModel()
    {
        Sessions = new SessionsViewModel();
        Localhost = new LocalhostViewModel();
        Feedback = new FeedbackViewModel();
        Telemetry = new TelemetryViewModel();
        Settings = new SettingsViewModel();

        // Phase 1 full nav set. Phase 2 prepends Tests + Past Runs items.
        NavItems.Add(new NavItem { Title = "Sessions",  IconGlyph = "", ViewModel = Sessions });
        NavItems.Add(new NavItem { Title = "Localhost", IconGlyph = "", ViewModel = Localhost });
        NavItems.Add(new NavItem { Title = "Feedback",  IconGlyph = "", ViewModel = Feedback });
        NavItems.Add(new NavItem { Title = "Telemetry", IconGlyph = "", ViewModel = Telemetry });
        NavItems.Add(new NavItem { Title = "Settings",  IconGlyph = "", ViewModel = Settings });
        SelectedNavItem = NavItems[0];

        var detected = WorkloadsLocator.AutoDetect();
        if (detected != null)
        {
            ApplyWorkloadsDir(detected);
        }
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

    private void ApplyWorkloadsDir(string dir)
    {
        WorkloadsDir = dir;
        _ = Sessions.LoadWorkloadsAsync(dir);
        Telemetry.SetWorkloadsDir(dir);
    }

    public void HandleAutoRun(AutoRunArgs args)
    {
        // Phase 0 spike: just bring the window forward; full --workload /
        // --test routing lands in Phase 5 with AutoRunRequestHandler.
    }
}
