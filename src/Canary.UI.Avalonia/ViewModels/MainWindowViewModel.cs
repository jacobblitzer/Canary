using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public MainWindowViewModel()
    {
        Sessions = new SessionsViewModel();

        // Phase 0 spike: NavigationView populated with the Sessions item
        // only. Phase 1 expands to the full nav set (Localhost, Feedback,
        // Telemetry, Settings, Tests, Past Runs).
        NavItems.Add(new NavItem
        {
            Title = "Sessions",
            IconGlyph = "",
            ViewModel = Sessions,
        });
        SelectedNavItem = NavItems[0];

        var detected = WorkloadsLocator.AutoDetect();
        if (detected != null)
        {
            WorkloadsDir = detected;
            _ = Sessions.LoadWorkloadsAsync(detected);
        }
    }

    public void HandleAutoRun(AutoRunArgs args)
    {
        // Phase 0 spike: just bring the window forward; full --workload /
        // --test routing lands in Phase 5 with AutoRunRequestHandler.
    }
}
