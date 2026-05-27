using Canary.Cli;
using Canary.Config;
using Canary.Orchestration;
using Canary.UI.Avalonia.ViewModels;

namespace Canary.UI.Avalonia.Services;

// Pure-logic helpers shared by MainWindowViewModel.HandleAutoRunAsync.
// Lives in Services/ so unit tests can call FindNode + ParseMode
// without spinning up an Avalonia window.
public static class AutoRunRequestHandler
{
    // Resolve a tree node from the AutoRunArgs the same way
    // MainForm.FindAutoRunNode does in the WinForms shell:
    //   1. Match workload by Name (case-insensitive).
    //   2. If args.Test is set: find the Test leaf node by Name; null if
    //      missing (don't fall back).
    //   3. Else if args.Suite is set: find the Suite leaf node by Name;
    //      null if missing.
    //   4. Else: return the Workload root node.
    public static WorkloadNode? FindNode(WorkloadTreeViewModel tree, AutoRunArgs args)
    {
        if (string.IsNullOrEmpty(args.Workload)) return null;
        foreach (var workloadRoot in tree.Roots)
        {
            if (workloadRoot.OwningWorkload == null) continue;
            if (!string.Equals(workloadRoot.OwningWorkload.Config.Name, args.Workload, StringComparison.OrdinalIgnoreCase)) continue;

            if (!string.IsNullOrEmpty(args.Test))
            {
                foreach (var group in workloadRoot.Children)
                {
                    foreach (var leaf in group.Children)
                    {
                        if (leaf.Kind == WorkloadNodeKind.Test
                            && leaf.Payload is TestDefinition td
                            && string.Equals(td.Name, args.Test, StringComparison.OrdinalIgnoreCase))
                        {
                            return leaf;
                        }
                    }
                }
                return null;
            }

            if (!string.IsNullOrEmpty(args.Suite))
            {
                foreach (var group in workloadRoot.Children)
                {
                    foreach (var leaf in group.Children)
                    {
                        if (leaf.Kind == WorkloadNodeKind.Suite
                            && leaf.Payload is SuiteDefinition sd
                            && string.Equals(sd.Name, args.Suite, StringComparison.OrdinalIgnoreCase))
                        {
                            return leaf;
                        }
                    }
                }
                return null;
            }

            return workloadRoot;
        }
        return null;
    }

    public static ModeOverride ParseMode(string? raw) => raw?.ToLowerInvariant() switch
    {
        "pixel-diff" => ModeOverride.PixelDiff,
        "vlm" => ModeOverride.Vlm,
        "both" => ModeOverride.Both,
        _ => ModeOverride.None,
    };
}
