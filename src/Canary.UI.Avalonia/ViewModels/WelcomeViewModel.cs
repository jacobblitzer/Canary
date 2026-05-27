using CommunityToolkit.Mvvm.ComponentModel;

namespace Canary.UI.Avalonia.ViewModels;

public sealed partial class WelcomeViewModel : ObservableObject
{
    public string Title => "Canary";
    public string Subtitle => "Visual Regression Testing Harness";
    public string Instructions =>
        "Select a workload, suite, or test from the tree to get started.\n\n" +
        "Use the toolbar to:\n" +
        "  • Open a workloads folder\n" +
        "  • Run tests or suites (double-click)\n" +
        "  • Record input for new tests\n" +
        "  • Approve candidate baselines\n\n" +
        "Features:\n" +
        "  • Suites group tests for batch execution\n" +
        "  • VLM checkpoints use AI vision to evaluate screenshots\n" +
        "  • Shared-instance mode runs multiple tests in one app session";
}
