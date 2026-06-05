using System.Collections.Generic;
using System.Threading.Tasks;
using Canary.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Canary.UI.Avalonia.ViewModels;

public partial class SessionsViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedTabIndex;

    public SessionsLiveViewModel Live { get; }
    public SessionsPastViewModel Past { get; }

    public SessionsViewModel()
    {
        Live = new SessionsLiveViewModel();
        Past = new SessionsPastViewModel();
    }

    public async Task LoadWorkloadsAsync(string workloadsDir)
    {
        // ConfigureAwait(true): the continuation calls into Live/Past which mutate
        // UI-bound collections. Off-thread mutation races Avalonia's container
        // generator (same class of bug as WorkloadTreeViewModel.LoadAsync).
        var configs = await DiscoverWorkloadConfigsAsync(workloadsDir).ConfigureAwait(true);
        Live.SetWorkloads(workloadsDir, configs);
        Past.SetWorkloadsDir(workloadsDir);
    }

    private static async Task<IReadOnlyList<WorkloadConfig>> DiscoverWorkloadConfigsAsync(string workloadsDir)
    {
        var configs = new List<WorkloadConfig>();
        if (!Directory.Exists(workloadsDir)) return configs;

        foreach (var dir in Directory.GetDirectories(workloadsDir))
        {
            var configPath = Path.Combine(dir, "workload.json");
            if (!File.Exists(configPath)) continue;
            try
            {
                var cfg = await WorkloadConfig.LoadAsync(configPath).ConfigureAwait(false);
                configs.Add(cfg);
            }
            catch
            {
                // Skip malformed entries; Sessions panel only filters by AgentType.
            }
        }
        return configs;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 1) Past.Reload();
    }
}
