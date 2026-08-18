using Avalonia.Controls;

namespace Canary.UI.Avalonia.Views;

/// <summary>
/// Code-behind for the Environment tab.
/// </summary>
/// <remarks>
/// Deliberately empty beyond initialisation. Unlike the Localhost and Telemetry tabs there is
/// nothing to poll: the plug-in half of this tab is a file written by the last run, and the
/// requirement half is driven by an explicit button. A timer here would repeatedly re-read a
/// file that only a run can change.
/// </remarks>
public partial class EnvironmentView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public EnvironmentView()
    {
        InitializeComponent();
    }
}
