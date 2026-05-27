using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Canary.Config;
using Canary.UI.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Canary.UI.Avalonia.ViewModels;

public enum WorkloadNodeKind { Workload, SuitesGroup, Suite, TestsGroup, Test, RecordingsGroup, Recording }

public sealed partial class WorkloadNode : ObservableObject
{
    public required string Label { get; init; }
    public required WorkloadNodeKind Kind { get; init; }
    public object? Payload { get; init; }       // WorkloadExplorer.WorkloadEntry / SuiteDefinition / TestDefinition / recording path
    public WorkloadExplorer.WorkloadEntry? OwningWorkload { get; init; }
    public ObservableCollection<WorkloadNode> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    public string Color { get; init; } = "#DCDCDC";
}

public partial class WorkloadTreeViewModel : ObservableObject
{
    public ObservableCollection<WorkloadNode> Roots { get; } = new();

    [ObservableProperty]
    private WorkloadNode? _selectedNode;

    [ObservableProperty]
    private string _statusText = "(no workloads loaded)";

    public string? WorkloadsDir { get; private set; }
    public IReadOnlyList<WorkloadExplorer.WorkloadEntry> LoadedWorkloads { get; private set; } = Array.Empty<WorkloadExplorer.WorkloadEntry>();

    public async Task LoadAsync(string workloadsDir)
    {
        WorkloadsDir = workloadsDir;
        var explorer = new WorkloadExplorer();
        var entries = await explorer.LoadWorkloadsAsync(workloadsDir).ConfigureAwait(false);
        LoadedWorkloads = entries;

        Roots.Clear();
        int totalSuites = 0, totalTests = 0, totalRecordings = 0;
        foreach (var entry in entries)
        {
            var workloadNode = new WorkloadNode
            {
                Label = entry.Config.DisplayName,
                Kind = WorkloadNodeKind.Workload,
                Payload = entry,
                OwningWorkload = entry,
                IsExpanded = true,
            };

            if (entry.Suites.Count > 0)
            {
                var suitesGroup = new WorkloadNode
                {
                    Label = $"Suites ({entry.Suites.Count})",
                    Kind = WorkloadNodeKind.SuitesGroup,
                    OwningWorkload = entry,
                    Color = "#96DC82",
                };
                foreach (var suite in entry.Suites)
                {
                    suitesGroup.Children.Add(new WorkloadNode
                    {
                        Label = $"{suite.Name} ({suite.Tests.Count} tests)",
                        Kind = WorkloadNodeKind.Suite,
                        Payload = suite,
                        OwningWorkload = entry,
                        Color = "#96DC82",
                    });
                }
                workloadNode.Children.Add(suitesGroup);
                totalSuites += entry.Suites.Count;
            }

            if (entry.Tests.Count > 0)
            {
                var testsGroup = new WorkloadNode
                {
                    Label = $"All Tests ({entry.Tests.Count})",
                    Kind = WorkloadNodeKind.TestsGroup,
                    OwningWorkload = entry,
                    Color = "#64B4FF",
                };
                foreach (var test in entry.Tests)
                {
                    testsGroup.Children.Add(new WorkloadNode
                    {
                        Label = test.Name,
                        Kind = WorkloadNodeKind.Test,
                        Payload = test,
                        OwningWorkload = entry,
                    });
                }
                workloadNode.Children.Add(testsGroup);
                totalTests += entry.Tests.Count;
            }

            if (entry.Recordings.Count > 0)
            {
                var recGroup = new WorkloadNode
                {
                    Label = $"Recordings ({entry.Recordings.Count})",
                    Kind = WorkloadNodeKind.RecordingsGroup,
                    OwningWorkload = entry,
                    Color = "#DCB432",
                };
                foreach (var recPath in entry.Recordings)
                {
                    var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(recPath));
                    recGroup.Children.Add(new WorkloadNode
                    {
                        Label = name,
                        Kind = WorkloadNodeKind.Recording,
                        Payload = recPath,
                        OwningWorkload = entry,
                        Color = "#DCB432",
                    });
                }
                workloadNode.Children.Add(recGroup);
                totalRecordings += entry.Recordings.Count;
            }

            Roots.Add(workloadNode);
        }

        StatusText = $"{entries.Count} workloads · {totalSuites} suites · {totalTests} tests · {totalRecordings} recordings · {workloadsDir}";
    }
}
